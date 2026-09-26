using EditSharp.Components.Media;
using EditSharp.Components;
using EditSharp.Caching.Proxy;
using EditSharp.Components.Clips;
using EditSharp.Editing;
using EditSharp.History;
using EditSharp.Playback;
using EditSharp.Rendering;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EditSharpGUI.Scripts.UI.Thumbnails;

// the frames drawn along the clips of one timeline, rendered through each
// clip's own graph - a clip is its graph, so its thumbnail is what it
// renders to, whatever fed it - and kept so a frame is rendered once and
// shown as long as it stands for its stretch of the clip
//
// a frame is keyed by four things: the clip, a height tier, a time grid and
// a slot on that grid. the tiers are fixed heights so a channel resized by
// a few pixels reuses what it has; the grid is a power of two seconds, the
// largest that still gives every slot on screen a frame of its own, so
// zooming out reuses every other frame and zooming in reuses all of them.
// slot times are anchored to the clip's media in-point, not to its head:
// a head trim shifts the in-point by exactly what it cuts, so the frames
// along the content keep their keys. what invalidates a clip's frames is
// its fingerprint changing - a speed change, a graph edit, a keyframe
// moved - checked after every history entry, whichever way it was made
//
// rendering happens on a playback of this cache's own, one frame at a time,
// newest request first, and lands on the main thread as a texture. Updated
// says which clip has something new
public sealed class ThumbnailCache : IDisposable
{
	// the heights a frame is rendered at. a strip asks for the smallest one
	// that is not shorter than it is
	public static readonly int[] Tiers = [32, 48, 64, 96, 128, 192, 256];

	public static int TierFor(float height)
	{
		foreach (int tier in Tiers) if (tier >= height) return tier;
		return Tiers[^1];
	}

	// the finest and coarsest grids: 1/256 s to 4096 s per slot
	public const int MinGrid = -8;
	public const int MaxGrid = 12;

	public static double GridSeconds(int grid) => Math.ScaleB(1d, grid);

	// how much texture memory to keep before the least recently shown go
	public long Budget { get; set; } = 96L << 20;

	// what is held right now
	public int Count => entries.Count;
	public long Bytes => bytes;
	public int Pending { get { lock (gate) return queued.Count; } }

	// width over height of every frame, from the project
	public float Aspect { get; }

	// a clip has new frames, or lost its old ones. main thread
	public event Action<Clip> Updated;

	readonly Timeline timeline;
	readonly History history;
	readonly Playback playback;

	public ThumbnailCache(Timeline timeline, RenderSettings settings, History history)
	{
		this.timeline = timeline;
		this.history = history;

		Aspect = settings.Resolution.Y > 0 ? settings.Resolution.X / settings.Resolution.Y : 16f / 9f;

		// its own playback, so it never contends with the one the user is
		// watching. the canvas it seeds is the largest frame it will make
		int largest = Tiers[^1];
		playback = new Playback
		{
			Timeline = timeline,
			RenderSettings = settings with { Resolution = new(Mathf.RoundToInt(largest * Aspect), largest), SourceMode = SourceMode.ProxiesOnly }
		};

		ProxyCache.StatusChanged += OnProxyStatusChanged;
		history.Changed += OnHistoryChanged;
	}

	// ---- the store ----

	readonly record struct Key(Clip Clip, int Tier, int Grid, long Index);

	sealed class Entry
	{
		public Texture2D Texture;
		public int Bytes;
		public bool Complete;
		public LinkedListNode<Key> Recent;
	}

	readonly Dictionary<Key, Entry> entries = [];
	readonly LinkedList<Key> recent = new();
	long bytes;

	// the frame for a slot, if it has been rendered; otherwise a request
	// for it, and a frame from another tier to stand in meanwhile, or
	// nothing. main thread
	public Texture2D Get(VideoClip clip, int tier, int grid, long index)
	{
		if (disposed) return null;

		Key key = new(clip, tier, grid, index);

		if (entries.TryGetValue(key, out Entry entry))
		{
			recent.Remove(entry.Recent);
			recent.AddLast(entry.Recent);
			return entry.Texture;
		}

		Request(key);

		foreach (int other in Tiers)
		{
			if (other != tier && entries.TryGetValue(key with { Tier = other }, out Entry standIn)) return standIn.Texture;
		}

		return null;
	}

	// everything held for a clip goes, and its strip is told
	public void Invalidate(Clip clip)
	{
		if (disposed) return;

		Drop(k => k.Clip == clip);
		fingerprints.Remove(clip);
		Updated?.Invoke(clip);
	}

	void Drop(Func<Key, bool> where)
	{
		List<Key> gone = [.. entries.Keys.Where(where)];

		foreach (Key key in gone) Remove(key);

		lock (gate)
		{
			queue.RemoveAll(j => where(j.Key));
			queued.RemoveWhere(k => where(k));
		}
	}

	void Remove(Key key)
	{
		if (!entries.Remove(key, out Entry entry)) return;

		recent.Remove(entry.Recent);
		bytes -= entry.Bytes;
	}

	void Store(Key key, ClipFrame frame)
	{
		Image image = Image.CreateFromData(frame.Width, frame.Height, false, Image.Format.Rgba8, frame.Pixels);
		ImageTexture texture = ImageTexture.CreateFromImage(image);

		Remove(key);

		Entry entry = new() { Texture = texture, Bytes = frame.Pixels.Length, Complete = frame.Complete };
		entry.Recent = recent.AddLast(key);
		entries[key] = entry;
		bytes += entry.Bytes;

		// the least recently shown make room. never below one entry, so a
		// frame bigger than the budget still shows
		while (bytes > Budget && recent.Count > 1) Remove(recent.First.Value);
	}

	// ---- the queue ----

	// a render to do: everything it needs from the model, read on the main
	// thread when it was asked for, so the worker touches the clip only
	// through the render itself
	readonly record struct Job(Key Key, VideoClip Clip, Time Content, int Fingerprint);

	readonly object gate = new();
	readonly List<Job> queue = [];
	readonly HashSet<Key> queued = [];
	readonly SemaphoreSlim signal = new(0);
	readonly CancellationTokenSource cancel = new();
	Task worker;

	// a frame that failed to render is left alone for a while rather than
	// asked for again every draw
	readonly Dictionary<Key, ulong> failed = [];
	const ulong RetryMs = 5000;

	void Request(Key key)
	{
		if (key.Clip is not VideoClip video) return;

		if (failed.TryGetValue(key, out ulong at))
		{
			if (Godot.Time.GetTicksMsec() - at < RetryMs) return;
			failed.Remove(key);
		}

		if (!fingerprints.TryGetValue(key.Clip, out int fingerprint))
			fingerprints[key.Clip] = fingerprint = ClipFingerprint.Of(key.Clip);

		Time anchored = Time.FromSeconds(key.Index * GridSeconds(key.Grid));
		Job job = new(key, video, anchored - ClipFingerprint.Anchor(key.Clip), fingerprint);

		lock (gate)
		{
			if (queued.Contains(key))
			{
				// asked for again: it is what is on screen now, so it goes first
				int index = queue.FindIndex(j => j.Key == key);
				if (index >= 0 && index != queue.Count - 1) { queue.RemoveAt(index); queue.Add(job); }
				return;
			}

			queued.Add(key);
			queue.Add(job);
		}

		worker ??= Task.Run(() => RunAsync(cancel.Token));
		signal.Release();
	}

	async Task RunAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			await signal.WaitAsync(ct);

			Job job;

			lock (gate)
			{
				if (queue.Count == 0) continue;
				job = queue[^1];
				queue.RemoveAt(queue.Count - 1);
			}

			await RenderAsync(job, ct);
		}
	}

	async Task RenderAsync(Job job, CancellationToken ct)
	{
		int width = Mathf.RoundToInt(job.Key.Tier * Aspect);

		try
		{
			ClipFrame frame = await playback.RenderClipFrameAsync(job.Clip, job.Content, width, job.Key.Tier, ct);
			Callable.From(() => Deliver(job.Key, frame, job.Fingerprint)).CallDeferred();
		}
		catch (OperationCanceledException)
		{
			lock (gate) queued.Remove(job.Key);
		}
		catch (Exception e)
		{
			GD.PushWarning($"Thumbnail for '{job.Clip.Name}' at {job.Content} failed to render: {e.Message}");
			Callable.From(() => Fail(job.Key)).CallDeferred();
		}
	}

	// main thread
	void Deliver(Key key, ClipFrame frame, int fingerprint)
	{
		lock (gate) queued.Remove(key);

		if (disposed) return;

		// the clip changed while this rendered: this frame is of the old clip
		if (fingerprints.TryGetValue(key.Clip, out int current) && current != fingerprint) return;

		Store(key, frame);
		Updated?.Invoke(key.Clip);
	}

	void Fail(Key key)
	{
		lock (gate) queued.Remove(key);

		if (disposed) return;

		failed[key] = Godot.Time.GetTicksMsec();
	}

	// ---- knowing when a frame is stale ----

	// the fingerprint each clip's frames were rendered against
	readonly Dictionary<Clip, int> fingerprints = [];

	// after any entry - a commit, an undo, a redo - every clip with frames
	// is fingerprinted again, and the ones that changed lose theirs. cheap:
	// graphs are small, and only clips this cache holds are looked at
	void OnHistoryChanged(object sender, HistoryEventArgs e)
	{
		if (disposed) return;

		foreach (Clip clip in fingerprints.Keys.ToList())
		{
			if (clip.Channel?.Timeline != timeline) continue;

			int now = ClipFingerprint.Of(clip);

			if (now == fingerprints[clip]) continue;

			Drop(k => k.Clip == clip);
			fingerprints[clip] = now;
			Updated?.Invoke(clip);
		}
	}

	// a proxy grew or finished: frames rendered with a placeholder in its
	// place may be renderable for real now
	void OnProxyStatusChanged(object sender, ProxyStatusChangedEventArgs e) => Callable.From(() =>
	{
		if (disposed) return;

		List<Clip> clips = [.. entries.Where(p => !p.Value.Complete).Select(p => p.Key.Clip).Distinct()];

		foreach (Clip clip in clips)
		{
			Drop(k => k.Clip == clip && entries.TryGetValue(k, out Entry entry) && !entry.Complete);
			Updated?.Invoke(clip);
		}
	}).CallDeferred();

	// ---- teardown ----

	bool disposed;

	public void Dispose()
	{
		if (disposed) return;
		disposed = true;

		history.Changed -= OnHistoryChanged;
		ProxyCache.StatusChanged -= OnProxyStatusChanged;

		cancel.Cancel();

		try { worker?.Wait(Time.FromSeconds(5).ToTimeout()); }
		catch (AggregateException) { /* cancelled, as asked */ }

		playback.Dispose();
		cancel.Dispose();
		signal.Dispose();

		entries.Clear();
		recent.Clear();
		bytes = 0;
	}
}
