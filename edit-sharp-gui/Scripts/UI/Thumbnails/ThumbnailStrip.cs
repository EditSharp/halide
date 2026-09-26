using EditSharp.Components.Clips;
using EditSharp.Editing;
using EditSharpGUI.Scripts.UI.Theming;
using Godot;
using System;

namespace EditSharpGUI.Scripts.UI.Thumbnails;

// the filmstrip along one clip: frames from the cache laid side by side, each
// as wide as the strip is tall times the project's aspect, the first at the
// clip's head. it draws only the part of itself in view, and asks the cache
// for what it draws - the cache renders what is missing and says when it is
// there. the strip's size is its parent's, so it needs nothing but the
// scale, the window, and a word when a head edit is being previewed
public partial class ThumbnailStrip : Control
{
	ThumbnailCache cache;
	VideoClip clip;

	double pixelsPerSecond;

	// the part of the strip on screen, in its own x
	float viewLeft = float.NegativeInfinity;
	float viewRight = float.PositiveInfinity;

	// a head edit in progress moves the clip's head without moving the
	// content under it. this is how far the head has gone, in content time,
	// so the frames stay put while the edge slides over them
	Time previewShift;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		ClipContents = true;
		UpdateMask();
	}

	// the strip's material, when it has one, rounds its corners to match
	// the panel it sits in - see Shaders/RoundedMask.gdshader. it needs the
	// strip's size and the panel's corner radius, from the theme
	void UpdateMask()
	{
		if (Material is not ShaderMaterial mask) return;

		float radius = 0f;

		if (GetParent() is Control panel)
		{
			radius = panel.GetThemeStylebox("panel") switch
			{
				ThemedStyleBox themed => themed.MinCornerRadius,
				StyleBoxFlat box => Mathf.Min(Mathf.Min(box.CornerRadiusTopLeft, box.CornerRadiusTopRight), Mathf.Min(box.CornerRadiusBottomLeft, box.CornerRadiusBottomRight)),
				_ => 0
			};
		}

		mask.SetShaderParameter("size", Size);
		mask.SetShaderParameter("radius", radius);
	}

	public void Setup(ThumbnailCache cache, VideoClip clip)
	{
		if (this.cache is not null) this.cache.Updated -= OnUpdated;

		this.cache = cache;
		this.clip = clip;

		if (cache is not null) cache.Updated += OnUpdated;

		QueueRedraw();
	}

	public void SetScale(double pixelsPerSecond)
	{
		if (this.pixelsPerSecond == pixelsPerSecond) return;

		this.pixelsPerSecond = pixelsPerSecond;
		QueueRedraw();
	}

	public void SetVisibleRange(float left, float right)
	{
		if (Mathf.IsEqualApprox(left, viewLeft) && Mathf.IsEqualApprox(right, viewRight)) return;

		viewLeft = left;
		viewRight = right;
		QueueRedraw();
	}

	public void SetPreviewShift(Time contentShift)
	{
		if (previewShift == contentShift) return;

		previewShift = contentShift;
		QueueRedraw();
	}

	void OnUpdated(Clip updated)
	{
		if (updated == clip) QueueRedraw();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationResized) { UpdateMask(); QueueRedraw(); }
		else if (what == NotificationThemeChanged) UpdateMask();
	}

	public override void _ExitTree()
	{
		if (cache is not null) cache.Updated -= OnUpdated;
	}

	public override void _Draw()
	{
		if (cache is null || clip is null || pixelsPerSecond <= 0d) return;

		float height = Size.Y;
		if (height < 4f) return;

		int tier = ThumbnailCache.TierFor(height);
		float slotWidth = Mathf.Max(8f, height * cache.Aspect);

		// content seconds one slot spans, and the coarsest power-of-two grid
		// that still puts a frame of its own in every slot
		double magnitude = Math.Abs(clip.Speed.Value);
		double slotSeconds = slotWidth / pixelsPerSecond * magnitude;
		int grid = magnitude > 0d ? Math.Clamp((int)Math.Floor(Math.Log2(slotSeconds)), ThumbnailCache.MinGrid, ThumbnailCache.MaxGrid) : ThumbnailCache.MinGrid;
		double gridSeconds = ThumbnailCache.GridSeconds(grid);

		double inPoint = ClipFingerprint.Anchor(clip).Seconds;
		double shift = previewShift.Seconds;

		// the file time shown at x: forwards from the in-point, backwards from the
		// end of the covered content (just inside it), or the one held frame
		double FileTimeAt(float x) => clip.Frozen ? inPoint + clip.FreezeAt.Seconds
			: clip.IsReversed ? inPoint + clip.ContentDuration.Seconds - shift - x / pixelsPerSecond * magnitude - 1e-6
			: inPoint + shift + x / pixelsPerSecond * magnitude;

		float left = Mathf.Max(0f, viewLeft);
		float right = Mathf.Min(Size.X, viewRight);
		if (right - left < 1f) return;

		int first = (int)Math.Floor(left / slotWidth);
		int last = (int)Math.Floor((right - 0.001f) / slotWidth);

		for (int i = first; i <= last; i++)
		{
			float x = i * slotWidth;
			float width = Mathf.Min(slotWidth, Size.X - x);
			if (width <= 0f) continue;

			// the file time at the slot's left edge
			double anchored = FileTimeAt(x);

			// before the media starts: a head extend in preview past what
			// the content has. nothing to show there yet
			if (anchored < 0d) continue;

			long index = (long)Math.Floor(anchored / gridSeconds + 1e-9);

			Texture2D texture = cache.Get(clip, tier, grid, index);
			if (texture is null) continue;

			// the last slot is cut off at the clip's end: show that much of
			// the frame, not the whole frame squeezed into it
			Rect2 region = new(0f, 0f, texture.GetWidth() * (width / slotWidth), texture.GetHeight());
			DrawTextureRectRegion(texture, new Rect2(x, 0f, width, height), region);
		}
	}
}
