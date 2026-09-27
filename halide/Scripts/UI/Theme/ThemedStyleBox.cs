using Godot;

namespace Halide.Scripts.UI.Theming;

// a stylebox that draws a ThemeStyle in one state, resolving its colours
// through the style's palette when it draws. nothing is written into the
// theme: a type's six state items are six of these over the same style,
// and a change to the style or the palette shows the next time they draw.
// the drawing itself is done by a private flat stylebox kept in step with
// the style, so it looks exactly as a StyleBoxFlat would
[Tool, GlobalClass]
public partial class ThemedStyleBox : StyleBox
{
	ThemeStyle _style;
	StyleState _state;

	[Export] public ThemeStyle Style
	{
		get => _style;
		set
		{
			if (_style == value) return;
			Callable on = new(this, MethodName.OnStyleChanged);
			if (_style is not null && _style.IsConnected(Resource.SignalName.Changed, on)) _style.Disconnect(Resource.SignalName.Changed, on);
			_style = value;
			if (_style is not null && !_style.IsConnected(Resource.SignalName.Changed, on)) _style.Connect(Resource.SignalName.Changed, on);
			synced = -1;
			QueueEmit();
		}
	}

	[Export] public StyleState State
	{
		get => _state;
		set
		{
			if (_state == value) return;
			_state = value;
			synced = -1;
			QueueEmit();
		}
	}

	readonly StyleBoxFlat flat = new();
	int synced = -1;
	int syncedPalette = -1;

	void OnStyleChanged()
	{
		synced = -1;
		QueueEmit();
	}

	// the change goes to the theme at the end of the frame, by object and
	// method name: a setter runs while a file loads and while the editor
	// restores every script instance after a build, when the theme listening
	// may not exist yet
	bool emitQueued;

	void QueueEmit()
	{
		if (emitQueued) return;
		emitQueued = true;
		new Callable(this, MethodName.EmitPending).CallDeferred();
	}

	void EmitPending()
	{
		emitQueued = false;
		EmitChanged();
	}

	// brings the flat box up to the style and palette, when either moved
	void Sync()
	{
		if (_style is null) return;

		int palette = _style.Palette?.Version ?? -1;
		if (synced == _style.Version && syncedPalette == palette) return;

		_style.Resolve(flat, _state);

		// content margins are read off this box by every control, not off
		// what it draws with, so they are mirrored here
		if (ContentMarginLeft != _style.ContentMarginLeft) ContentMarginLeft = _style.ContentMarginLeft;
		if (ContentMarginTop != _style.ContentMarginTop) ContentMarginTop = _style.ContentMarginTop;
		if (ContentMarginRight != _style.ContentMarginRight) ContentMarginRight = _style.ContentMarginRight;
		if (ContentMarginBottom != _style.ContentMarginBottom) ContentMarginBottom = _style.ContentMarginBottom;

		synced = _style.Version;
		syncedPalette = palette;
	}

	public override void _Draw(Rid toCanvasItem, Rect2 rect)
	{
		if (_style is null) return;
		Sync();
		flat.Draw(toCanvasItem, rect);
	}

	public override Vector2 _GetMinimumSize()
	{
		if (_style is null) return Vector2.Zero;
		Sync();
		return flat.GetMinimumSize();
	}

	public override Rect2 _GetDrawRect(Rect2 rect)
	{
		if (_style is null) return rect;
		Sync();
		Rect2 drawn = rect.GrowIndividual(flat.ExpandMarginLeft, flat.ExpandMarginTop, flat.ExpandMarginRight, flat.ExpandMarginBottom);
		if (flat.ShadowSize > 0) drawn = drawn.Merge(new Rect2(drawn.Position + flat.ShadowOffset, drawn.Size).Grow(flat.ShadowSize));
		return drawn;
	}

	public override bool _TestMask(Vector2 point, Rect2 rect)
	{
		if (_style is null) return rect.HasPoint(point);
		Sync();
		return flat.TestMask(point, rect);
	}

	// ---- for code that needs a plain box ----

	// the colour this box draws its background with right now
	public Color ResolvedBackground
	{
		get { Sync(); return flat.BgColor; }
	}

	// a flat copy of what this box draws, for code that wants to tint one
	// instance - a clip in its own colour
	public StyleBoxFlat MakeFlat()
	{
		Sync();
		return (StyleBoxFlat)flat.Duplicate();
	}

	public int MinCornerRadius => _style?.MinCornerRadius ?? 0;
}
