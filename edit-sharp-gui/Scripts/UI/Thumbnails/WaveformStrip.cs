using EditSharp.Components.Clips;
using EditSharp.Editing;
using Godot;
using System;

namespace EditSharpGUI.Scripts.UI.Thumbnails;

// the waveform along one audio clip: the clip's envelope from the cache,
// painted by the shader with the clip's head at the strip's left edge.
// the strip is told the scale, the colour and any head or speed edit
// being previewed, and moves the window rather than the data
public partial class WaveformStrip : WaveformView
{
	WaveformCache cache;
	AudioClip clip;

	double pixelsPerSecond;
	double? previewSpeed;
	Time previewShift;

	public void Setup(WaveformCache cache, AudioClip clip)
	{
		if (this.cache is not null) this.cache.Updated -= OnUpdated;

		this.cache = cache;
		this.clip = clip;

		if (cache is not null) cache.Updated += OnUpdated;

		Refresh();
	}

	public void SetScale(double pixelsPerSecond)
	{
		if (this.pixelsPerSecond == pixelsPerSecond) return;

		this.pixelsPerSecond = pixelsPerSecond;
		Refresh();
	}

	public void SetColor(Color color)
	{
		SetColors(color, color.Lightened(0.35f));
	}

	// a head edit in progress moves the clip's head over the content
	public void SetPreviewShift(Time contentShift)
	{
		if (previewShift == contentShift) return;

		previewShift = contentShift;
		Refresh();
	}

	// a timeshift in progress plays the content at another speed
	public void SetPreviewSpeed(double? speed)
	{
		if (previewSpeed == speed) return;

		previewSpeed = speed;
		Refresh();
	}

	// the strip is the whole clip, so nothing depends on the window
	public void SetVisibleRange(float left, float right) { }

	void OnUpdated(Clip updated)
	{
		if (updated == clip) Refresh();
	}

	void Refresh()
	{
		if (cache is null || clip is null || pixelsPerSecond <= 0d) return;

		double speed = previewSpeed ?? clip.Speed.Value;

		// a frozen clip is silent
		EnvelopeTexture envelope = speed == 0d ? null : cache.Get(clip);
		SetEnvelope(envelope);
		if (envelope is null) return;

		// backwards the left edge is the end of the covered content, and the content runs down from there
		double left = speed < 0d ? clip.ContentDuration.Seconds - previewShift.Seconds : previewShift.Seconds;

		SetAnchor(ClipFingerprint.Anchor(clip));
		SetWindow(left, speed / pixelsPerSecond);
	}

	// back in the tree after its view moved between panes or windows: listening again, and caught up
	public override void _EnterTree()
	{
		if (cache is null) return;
		cache.Updated -= OnUpdated;
		cache.Updated += OnUpdated;
		Refresh();
	}

	public override void _ExitTree()
	{
		if (cache is not null) cache.Updated -= OnUpdated;
	}
}
