using EditSharp.Audio.Analysis;
using EditSharp.Components;
using EditSharp.Components.Clips;
using EditSharp.Components.Nodes;
using EditSharp.Components.Nodes.Input;
using EditSharp.Editing;
using EditSharp.History;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EditSharpGUI.Scripts.UI.Thumbnails;

// the waveforms along the audio clips of one timeline: each clip's
// processed envelope, folded from its media's analysis through its graph
// in the frequency domain, as one texture covering everything the clip
// could show - the whole media around its in-point, so a trim or extend
// needs nothing new. rebuilt when the clip's fingerprint changes, and
// when a media's analysis arrives. Updated says which clip has one
public sealed class WaveformCache : IDisposable
{
	public event Action<Clip> Updated;

	public int Count => envelopes.Count;

	readonly Timeline timeline;
	readonly History history;

	public WaveformCache(Timeline timeline, History history)
	{
		this.timeline = timeline;
		this.history = history;

		history.Changed += OnHistoryChanged;
		AudioAnalysisCache.Completed += OnAnalysis;
	}

	readonly Dictionary<Clip, EnvelopeTexture> envelopes = [];
	readonly Dictionary<Clip, int> fingerprints = [];
	readonly HashSet<Clip> pending = [];

	// clips whose envelope is out of date: shown as they are until the new one lands
	readonly HashSet<Clip> stale = [];
	readonly HashSet<string> analysesAsked = new(StringComparer.OrdinalIgnoreCase);
	readonly Dictionary<Clip, ulong> failed = [];
	const ulong RetryMs = 5000;

	// the clip's envelope, if built; otherwise a request for it and null
	public EnvelopeTexture Get(AudioClip clip)
	{
		if (disposed || clip is null) return null;

		if (envelopes.TryGetValue(clip, out EnvelopeTexture made))
		{
			if (stale.Contains(clip)) Request(clip);
			return made;
		}

		Request(clip);
		return null;
	}

	// a clip edited live, in an inspector drag say: its envelope is
	// re-folded right away, and shown as it was until the new one lands
	public void RefreshEdited()
	{
		if (disposed) return;

		foreach (Clip clip in fingerprints.Keys.ToList())
		{
			if (clip.Channel?.Timeline != timeline) continue;

			int now = ClipFingerprint.Of(clip);
			if (now == fingerprints[clip]) continue;

			fingerprints.Remove(clip);
			stale.Add(clip);
			Updated?.Invoke(clip);
		}
	}

	public void Invalidate(Clip clip)
	{
		if (disposed) return;

		envelopes.Remove(clip);
		fingerprints.Remove(clip);
		stale.Remove(clip);
		Updated?.Invoke(clip);
	}

	void Request(AudioClip clip)
	{
		if (pending.Contains(clip)) return;

		if (failed.TryGetValue(clip, out ulong at))
		{
			if (Time.GetTicksMsec() - at < RetryMs) return;
			failed.Remove(clip);
		}

		// the media it reads must be analysed first; each file once
		IReadOnlyList<string> missing = ClipSpectrum.MissingMedia(clip);
		if (missing.Count > 0)
		{
			foreach (string path in missing)
			{
				if (!analysesAsked.Add(path)) continue;
				_ = AudioAnalysisCache.GetAsync(path).ContinueWith(t =>
				{
					if (!t.IsCompletedSuccessfully) GD.PushWarning($"Audio analysis of '{path}' failed: {t.Exception?.GetBaseException().Message}");
					Callable.From(() => analysesAsked.Remove(path)).CallDeferred();
				}, TaskScheduler.Default);
			}

			return;
		}

		int fingerprint = ClipFingerprint.Of(clip);
		fingerprints[clip] = fingerprint;
		pending.Add(clip);

		(TimeSpan start, int frames) = Extent(clip);
		TimeSpan anchor = ClipFingerprint.Anchor(clip);

		Task.Run(() =>
		{
			try
			{
				SpectralEnvelope envelope = ClipSpectrum.Evaluate(clip, start, frames);
				Callable.From(() => Deliver(clip, envelope is null ? null : new EnvelopeTexture(envelope, anchor), fingerprint)).CallDeferred();
			}
			catch (Exception e)
			{
				GD.PushWarning($"Waveform for '{clip.Name}' failed: {e.Message}");
				Callable.From(() => Deliver(clip, null, fingerprint)).CallDeferred();
			}
		});
	}

	// what the envelope covers: for a clip reading media, the whole media
	// around the in-point; for a generator, the clip plus room to extend
	static (TimeSpan Start, int Frames) Extent(AudioClip clip)
	{
		AudioMediaNode media = clip.Graph.AllNodes.OfType<AudioMediaNode>().FirstOrDefault(n => n.Media is not null);

		if (media is not null && AudioAnalysisCache.TryGet(media.Media.Path, out AudioAnalysis analysis))
		{
			TimeSpan inPoint = media.Start ?? TimeSpan.Zero;
			int frames = analysis.FrameCount;
			return (-inPoint, frames);
		}

		TimeSpan span = clip.ContentDuration + TimeSpan.FromSeconds(30);
		return (TimeSpan.Zero, (int)Math.Ceiling(span.TotalSeconds / AudioAnalysis.FrameSeconds));
	}

	void Deliver(Clip clip, EnvelopeTexture envelope, int fingerprint)
	{
		pending.Remove(clip);
		if (disposed) return;

		if (envelope is null) { failed[clip] = Time.GetTicksMsec(); return; }

		// the clip changed while this built: build again for what it is now
		if (!fingerprints.TryGetValue(clip, out int current) || current != fingerprint)
		{
			if (clip is AudioClip again && clip.Channel?.Timeline == timeline) Request(again);
			return;
		}

		stale.Remove(clip);
		envelopes[clip] = envelope;
		Updated?.Invoke(clip);
	}

	// after any entry, every clip with an envelope is fingerprinted again
	void OnHistoryChanged(object sender, HistoryEventArgs e)
	{
		if (disposed) return;

		foreach (Clip clip in fingerprints.Keys.ToList())
		{
			if (clip.Channel?.Timeline != timeline) continue;

			int now = ClipFingerprint.Of(clip);
			if (now == fingerprints[clip]) continue;

			fingerprints.Remove(clip);
			stale.Add(clip);
			Updated?.Invoke(clip);
		}
	}

	// a media's analysis landed: clips waiting on it can build now
	void OnAnalysis(string path) => Callable.From(() =>
	{
		if (disposed) return;

		foreach (Clip clip in timeline.Channels.SelectMany(c => c.Clips).OfType<AudioClip>())
		{
			if (envelopes.ContainsKey(clip)) continue;
			Updated?.Invoke(clip);
		}
	}).CallDeferred();

	bool disposed;

	public void Dispose()
	{
		if (disposed) return;
		disposed = true;

		history.Changed -= OnHistoryChanged;
		AudioAnalysisCache.Completed -= OnAnalysis;
		envelopes.Clear();
	}
}
