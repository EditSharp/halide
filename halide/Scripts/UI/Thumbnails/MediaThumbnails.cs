using EditSharp.Audio.Analysis;
using EditSharp.Components;
using EditSharp.Components.Clips;
using EditSharp.Components.Media;
using EditSharp.Playback;
using EditSharp.Rendering;
using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Halide.Scripts.UI.Thumbnails;

// the pictures on the media viewer's tiles: one frame per video or still,
// the first frame of a timeline, and the whole-length peaks of an audio
// media. each is made once, on a background task, and kept until the media
// changes its file. Updated says which media has something new
public sealed class MediaThumbnails : IDisposable
{
	// a video's frame is taken this far into it, past any black lead-in
	public const double FrameFraction = 0.1;

	public event Action<object> Updated;

	readonly RenderSettings settings;

	public MediaThumbnails(RenderSettings settings)
	{
		this.settings = settings;
	}

	readonly Dictionary<object, Texture2D> frames = [];
	readonly Dictionary<object, EnvelopeTexture> envelopes = [];
	readonly Dictionary<object, string> paths = [];
	readonly HashSet<object> pending = [];
	readonly Dictionary<object, ulong> failed = [];
	const ulong RetryMs = 5000;

	// the frame for a video, still or timeline, if made; otherwise a
	// request for it and null. width and height bound the picture
	public Texture2D GetFrame(object subject, int width, int height)
	{
		if (disposed || subject is null) return null;

		if (subject is IMedia media && paths.TryGetValue(subject, out string path) && path != media.Path) Forget(subject);

		if (frames.TryGetValue(subject, out Texture2D texture)) return texture;

		if (subject is IMedia m) paths[subject] = m.Path;
		Request(subject, () => RenderFrameAsync(subject, width, height));
		return null;
	}

	// the envelope of an audio media's whole length, from its analysis;
	// otherwise a request for the analysis and null
	public EnvelopeTexture GetEnvelope(AudioMedia media)
	{
		if (disposed || media is null) return null;

		if (paths.TryGetValue(media, out string path) && path != media.Path) Forget(media);

		if (envelopes.TryGetValue(media, out EnvelopeTexture made)) return made;

		paths[media] = media.Path;

		if (AudioAnalysisCache.TryGet(media.Path, out AudioAnalysis analysis))
		{
			made = EnvelopeTexture.Of(analysis);
			envelopes[media] = made;
			return made;
		}

		Request(media, () => AnalyseAsync(media));
		return null;
	}

	public void Forget(object subject)
	{
		frames.Remove(subject);
		envelopes.Remove(subject);
		paths.Remove(subject);
		failed.Remove(subject);
	}

	void Request(object subject, Func<Task> render)
	{
		if (pending.Contains(subject)) return;

		if (failed.TryGetValue(subject, out ulong at))
		{
			if (Godot.Time.GetTicksMsec() - at < RetryMs) return;
			failed.Remove(subject);
		}

		pending.Add(subject);
		_ = render();
	}

	async Task RenderFrameAsync(object subject, int width, int height)
	{
		try
		{
			Texture2D texture = subject switch
			{
				VideoMedia media => await VideoFrameAsync(media, width, height),
				Timeline timeline => await TimelineFrameAsync(timeline, width, height),
				_ => null
			};

			Callable.From(() => Deliver(subject, texture, null)).CallDeferred();
		}
		catch (Exception e)
		{
			GD.PushWarning($"Thumbnail for '{subject}' failed: {e.Message}");
			Callable.From(() => Deliver(subject, null, null)).CallDeferred();
		}
	}

	async Task AnalyseAsync(AudioMedia media)
	{
		try
		{
			AudioAnalysis analysis = await AudioAnalysisCache.GetAsync(media.Path, cancel.Token);
			Callable.From(() => Deliver(media, null, EnvelopeTexture.Of(analysis))).CallDeferred();
		}
		catch (Exception e)
		{
			GD.PushWarning($"Waveform for '{media.Name}' failed: {e.Message}");
			Callable.From(() => Deliver(media, null, null)).CallDeferred();
		}
	}

	async Task<Texture2D> VideoFrameAsync(VideoMedia media, int width, int height)
	{
		Time? length = await media.GetNaturalLengthAsync(cancel.Token);
		Time at = length is Time l ? Time.FromSeconds(l.Seconds * FrameFraction) : Time.Zero;

		using SkiaSharp.SKImage image = await media.GetFrameAtAsync(at, SourceMode.ProxiesAndSource, width, height, cancel.Token);
		return ToTexture(image);
	}

	// a timeline's first frame: a scratch clip that embeds it, rendered on
	// its own the way clip thumbnails are
	async Task<Texture2D> TimelineFrameAsync(Timeline timeline, int width, int height)
	{
		VideoClip clip = VideoClip.CreateTimelineEmbed(timeline, Time.Zero, Time.FromSeconds(1));

		using Playback playback = new() { Timeline = timeline, RenderSettings = settings with { SourceMode = SourceMode.ProxiesAndSource } };
		ClipFrame frame = await playback.RenderClipFrameAsync(clip, Time.Zero, width, height, cancel.Token);

		Image image = Image.CreateFromData(frame.Width, frame.Height, false, Image.Format.Rgba8, frame.Pixels);
		return ImageTexture.CreateFromImage(image);
	}

	static Texture2D ToTexture(SkiaSharp.SKImage image)
	{
		if (image is null) return null;

		using SkiaSharp.SKBitmap bitmap = SkiaSharp.SKBitmap.FromImage(image);
		using SkiaSharp.SKBitmap rgba = bitmap.ColorType == SkiaSharp.SKColorType.Rgba8888 ? bitmap.Copy() : bitmap.Copy(SkiaSharp.SKColorType.Rgba8888);

		Image made = Image.CreateFromData(rgba.Width, rgba.Height, false, Image.Format.Rgba8, rgba.Bytes);
		return ImageTexture.CreateFromImage(made);
	}

	// main thread
	void Deliver(object subject, Texture2D texture, EnvelopeTexture made)
	{
		pending.Remove(subject);
		if (disposed) return;

		if (texture is not null) frames[subject] = texture;
		else if (made is not null) envelopes[subject] = made;
		else { failed[subject] = Godot.Time.GetTicksMsec(); return; }

		Updated?.Invoke(subject);
	}

	readonly CancellationTokenSource cancel = new();
	bool disposed;

	public void Dispose()
	{
		if (disposed) return;
		disposed = true;
		cancel.Cancel();
		frames.Clear();
		envelopes.Clear();
	}
}
