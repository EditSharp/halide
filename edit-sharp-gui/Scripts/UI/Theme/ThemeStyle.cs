using Godot;
using System.Collections.Generic;

namespace EditSharpGUI.Scripts.UI.Theming;

// one look, authored once: which palette definitions colour its
// background, border and shadow, and its shape - corners, borders,
// margins, shadow, skew. a type's state styleboxes are thin wrappers that
// share one of these and add only the state; the palette's rules make the
// hover, pressed, disabled and focus looks from it at draw time. edited
// in godot's inspector like any resource
[Tool, GlobalClass]
public partial class ThemeStyle : Resource
{
	public int Version { get; private set; }

	// the change signal goes out at the end of the frame, by object and
	// method name: a setter runs while a file loads and while the editor
	// restores every script instance after a build, when the resources
	// listening may not exist yet - a synchronous emission would reach a
	// released instance
	bool emitQueued;

	void Bump()
	{
		Version++;
		if (emitQueued) return;
		emitQueued = true;
		new Callable(this, MethodName.EmitPending).CallDeferred();
	}

	void EmitPending()
	{
		emitQueued = false;
		EmitChanged();
	}

	void Set<T>(ref T field, T value)
	{
		if (EqualityComparer<T>.Default.Equals(field, value)) return;
		field = value;
		Bump();
	}

	// ---- the palette this look resolves against ----

	ThemePalette _palette;

	[Export] public ThemePalette Palette
	{
		get => _palette;
		set
		{
			if (_palette == value) return;
			Callable on = new(this, MethodName.Bump);
			if (_palette is not null && _palette.IsConnected(Resource.SignalName.Changed, on)) _palette.Disconnect(Resource.SignalName.Changed, on);
			_palette = value;
			if (_palette is not null && !_palette.IsConnected(Resource.SignalName.Changed, on)) _palette.Connect(Resource.SignalName.Changed, on);
			Bump();
		}
	}

	// ---- picks ----

	ThemeDefinition _background = ThemeDefinition.BackgroundColor1;
	ThemeShade _backgroundShade;
	float _backgroundAlpha = 1f;
	ThemeDefinition _border = ThemeDefinition.None;
	ThemeShade _borderShade;
	float _borderAlpha = 1f;
	ThemeDefinition _shadow = ThemeDefinition.None;
	ThemeShade _shadowShade;
	float _shadowAlpha = 1f;
	bool _focusRing = true;

	[ExportGroup("Background")]
	[Export] public ThemeDefinition Background { get => _background; set => Set(ref _background, value); }
	[Export] public ThemeShade BackgroundShade { get => _backgroundShade; set => Set(ref _backgroundShade, value); }
	[Export(PropertyHint.Range, "0,1,0.01")] public float BackgroundAlpha { get => _backgroundAlpha; set => Set(ref _backgroundAlpha, value); }

	[ExportGroup("Border")]
	[Export] public ThemeDefinition Border { get => _border; set => Set(ref _border, value); }
	[Export] public ThemeShade BorderShade { get => _borderShade; set => Set(ref _borderShade, value); }
	[Export(PropertyHint.Range, "0,1,0.01")] public float BorderAlpha { get => _borderAlpha; set => Set(ref _borderAlpha, value); }
	[Export] public bool BorderBlend { get => _borderBlend; set => Set(ref _borderBlend, value); }
	[Export] public int BorderWidthLeft { get => _borderWidthLeft; set => Set(ref _borderWidthLeft, value); }
	[Export] public int BorderWidthTop { get => _borderWidthTop; set => Set(ref _borderWidthTop, value); }
	[Export] public int BorderWidthRight { get => _borderWidthRight; set => Set(ref _borderWidthRight, value); }
	[Export] public int BorderWidthBottom { get => _borderWidthBottom; set => Set(ref _borderWidthBottom, value); }

	// the focus state gets the palette's accent ring, unless this says not to
	[ExportGroup("Focus")]
	[Export] public bool FocusRing { get => _focusRing; set => Set(ref _focusRing, value); }

	[ExportGroup("Shadow")]
	[Export] public ThemeDefinition Shadow { get => _shadow; set => Set(ref _shadow, value); }
	[Export] public ThemeShade ShadowShade { get => _shadowShade; set => Set(ref _shadowShade, value); }
	[Export(PropertyHint.Range, "0,1,0.01")] public float ShadowAlpha { get => _shadowAlpha; set => Set(ref _shadowAlpha, value); }
	[Export] public int ShadowSize { get => _shadowSize; set => Set(ref _shadowSize, value); }
	[Export] public Vector2 ShadowOffset { get => _shadowOffset; set => Set(ref _shadowOffset, value); }

	// ---- shape ----

	bool _drawCenter = true;
	bool _borderBlend;
	int _cornerRadiusTopLeft, _cornerRadiusTopRight, _cornerRadiusBottomRight, _cornerRadiusBottomLeft;
	int _cornerDetail = 8;
	int _borderWidthLeft, _borderWidthTop, _borderWidthRight, _borderWidthBottom;
	float _contentMarginLeft = -1f, _contentMarginTop = -1f, _contentMarginRight = -1f, _contentMarginBottom = -1f;
	float _expandMarginLeft, _expandMarginTop, _expandMarginRight, _expandMarginBottom;
	int _shadowSize;
	Vector2 _shadowOffset;
	Vector2 _skew;
	bool _antiAliasing = true;
	float _antiAliasingSize = 1f;

	[ExportGroup("Shape")]
	[Export] public bool DrawCenter { get => _drawCenter; set => Set(ref _drawCenter, value); }
	[Export] public Vector2 Skew { get => _skew; set => Set(ref _skew, value); }

	[ExportGroup("Corner Radius")]
	[Export] public int CornerRadiusTopLeft { get => _cornerRadiusTopLeft; set => Set(ref _cornerRadiusTopLeft, value); }
	[Export] public int CornerRadiusTopRight { get => _cornerRadiusTopRight; set => Set(ref _cornerRadiusTopRight, value); }
	[Export] public int CornerRadiusBottomRight { get => _cornerRadiusBottomRight; set => Set(ref _cornerRadiusBottomRight, value); }
	[Export] public int CornerRadiusBottomLeft { get => _cornerRadiusBottomLeft; set => Set(ref _cornerRadiusBottomLeft, value); }
	[Export] public int CornerDetail { get => _cornerDetail; set => Set(ref _cornerDetail, value); }

	[ExportGroup("Content Margins")]
	[Export] public float ContentMarginLeft { get => _contentMarginLeft; set => Set(ref _contentMarginLeft, value); }
	[Export] public float ContentMarginTop { get => _contentMarginTop; set => Set(ref _contentMarginTop, value); }
	[Export] public float ContentMarginRight { get => _contentMarginRight; set => Set(ref _contentMarginRight, value); }
	[Export] public float ContentMarginBottom { get => _contentMarginBottom; set => Set(ref _contentMarginBottom, value); }

	[ExportGroup("Expand Margins")]
	[Export] public float ExpandMarginLeft { get => _expandMarginLeft; set => Set(ref _expandMarginLeft, value); }
	[Export] public float ExpandMarginTop { get => _expandMarginTop; set => Set(ref _expandMarginTop, value); }
	[Export] public float ExpandMarginRight { get => _expandMarginRight; set => Set(ref _expandMarginRight, value); }
	[Export] public float ExpandMarginBottom { get => _expandMarginBottom; set => Set(ref _expandMarginBottom, value); }

	[ExportGroup("Anti-aliasing")]
	[Export] public bool AntiAliasing { get => _antiAliasing; set => Set(ref _antiAliasing, value); }
	[Export] public float AntiAliasingSize { get => _antiAliasingSize; set => Set(ref _antiAliasingSize, value); }

	public int MinCornerRadius => Mathf.Min(Mathf.Min(_cornerRadiusTopLeft, _cornerRadiusTopRight), Mathf.Min(_cornerRadiusBottomRight, _cornerRadiusBottomLeft));

	// ---- resolving: a flat stylebox that looks like this style in a state ----

	// writes this look, in the state, into a flat stylebox - only what
	// differs, since every write on a box in use re-themes the controls
	// wearing it
	public void Resolve(StyleBoxFlat into, StyleState state)
	{
		ThemePalette p = _palette;
		ThemeShade shade = ThemePalette.ShadeOf(state);
		bool focus = state == StyleState.Focus && _focusRing;

		Put(v => into.DrawCenter != v, v => into.DrawCenter = v, _drawCenter);
		Put(v => into.BorderBlend != v, v => into.BorderBlend = v, focus ? false : _borderBlend);
		Put(v => into.CornerRadiusTopLeft != v, v => into.CornerRadiusTopLeft = v, _cornerRadiusTopLeft);
		Put(v => into.CornerRadiusTopRight != v, v => into.CornerRadiusTopRight = v, _cornerRadiusTopRight);
		Put(v => into.CornerRadiusBottomRight != v, v => into.CornerRadiusBottomRight = v, _cornerRadiusBottomRight);
		Put(v => into.CornerRadiusBottomLeft != v, v => into.CornerRadiusBottomLeft = v, _cornerRadiusBottomLeft);
		Put(v => into.CornerDetail != v, v => into.CornerDetail = v, _cornerDetail);

		int widest = Mathf.Max(Mathf.Max(_borderWidthLeft, _borderWidthRight), Mathf.Max(_borderWidthTop, _borderWidthBottom));
		int ring = focus ? Mathf.Max(p?.FocusWidth ?? 1, widest) : 0;
		Put(v => into.BorderWidthLeft != v, v => into.BorderWidthLeft = v, focus ? ring : _borderWidthLeft);
		Put(v => into.BorderWidthTop != v, v => into.BorderWidthTop = v, focus ? ring : _borderWidthTop);
		Put(v => into.BorderWidthRight != v, v => into.BorderWidthRight = v, focus ? ring : _borderWidthRight);
		Put(v => into.BorderWidthBottom != v, v => into.BorderWidthBottom = v, focus ? ring : _borderWidthBottom);

		Put(v => into.ContentMarginLeft != v, v => into.ContentMarginLeft = v, _contentMarginLeft);
		Put(v => into.ContentMarginTop != v, v => into.ContentMarginTop = v, _contentMarginTop);
		Put(v => into.ContentMarginRight != v, v => into.ContentMarginRight = v, _contentMarginRight);
		Put(v => into.ContentMarginBottom != v, v => into.ContentMarginBottom = v, _contentMarginBottom);
		Put(v => into.ExpandMarginLeft != v, v => into.ExpandMarginLeft = v, _expandMarginLeft);
		Put(v => into.ExpandMarginTop != v, v => into.ExpandMarginTop = v, _expandMarginTop);
		Put(v => into.ExpandMarginRight != v, v => into.ExpandMarginRight = v, _expandMarginRight);
		Put(v => into.ExpandMarginBottom != v, v => into.ExpandMarginBottom = v, _expandMarginBottom);
		Put(v => into.ShadowSize != v, v => into.ShadowSize = v, _shadowSize);
		Put(v => into.ShadowOffset != v, v => into.ShadowOffset = v, _shadowOffset);
		Put(v => into.Skew != v, v => into.Skew = v, _skew);
		Put(v => into.AntiAliasing != v, v => into.AntiAliasing = v, _antiAliasing);
		Put(v => into.AntiAliasingSize != v, v => into.AntiAliasingSize = v, _antiAliasingSize);

		if (p is null) return;

		if (_background != ThemeDefinition.None)
		{
			Color background = p.Resolve(_background, state == StyleState.Focus ? _backgroundShade : shade, _backgroundAlpha);
			if (into.BgColor != background) into.BgColor = background;
		}

		Color border = focus ? p.Resolve(ThemeDefinition.AccentColor, ThemeShade.None, p.FocusAlpha)
			: _border != ThemeDefinition.None ? p.Resolve(_border, shade, _borderAlpha)
			: into.BorderColor;
		if (into.BorderColor != border) into.BorderColor = border;

		if (_shadow != ThemeDefinition.None)
		{
			Color shadow = p.Resolve(_shadow, _shadowShade, _shadowAlpha);
			if (into.ShadowColor != shadow) into.ShadowColor = shadow;
		}
	}

	static void Put<T>(System.Func<T, bool> differs, System.Action<T> write, T value)
	{
		if (differs(value)) write(value);
	}
}
