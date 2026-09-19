using Godot;

namespace EditSharpGUI.Scripts.UI.Theming;

// a flat stylebox whose colours come from the theme's definitions. its
// shape - corners, borders, margins, shadow size - is edited in godot's
// theme editor or the theme tool like any stylebox and is never touched by
// the theme; only the background, border and shadow colours are written,
// from whichever definitions are picked here, whenever the definitions
// change. a pick of None leaves that colour alone
[Tool, GlobalClass]
public partial class ThemedStyleBox : StyleBoxFlat
{
	ThemeDefinition _background = ThemeDefinition.BackgroundColor1;
	ThemeShade _backgroundShade;
	float _backgroundAlpha = 1f;
	ThemeDefinition _border = ThemeDefinition.None;
	ThemeShade _borderShade;
	float _borderAlpha = 1f;
	ThemeDefinition _shadow = ThemeDefinition.None;
	ThemeShade _shadowShade;
	float _shadowAlpha = 1f;

	[ExportGroup("Definitions")]
	[Export] public ThemeDefinition Background { get => _background; set { _background = value; EmitChanged(); } }
	[Export] public ThemeShade BackgroundShade { get => _backgroundShade; set { _backgroundShade = value; EmitChanged(); } }
	[Export(PropertyHint.Range, "0,1,0.01")] public float BackgroundAlpha { get => _backgroundAlpha; set { _backgroundAlpha = value; EmitChanged(); } }
	[Export] public ThemeDefinition Border { get => _border; set { _border = value; EmitChanged(); } }
	[Export] public ThemeShade BorderShade { get => _borderShade; set { _borderShade = value; EmitChanged(); } }
	[Export(PropertyHint.Range, "0,1,0.01")] public float BorderAlpha { get => _borderAlpha; set { _borderAlpha = value; EmitChanged(); } }
	[Export] public ThemeDefinition Shadow { get => _shadow; set { _shadow = value; EmitChanged(); } }
	[Export] public ThemeShade ShadowShade { get => _shadowShade; set { _shadowShade = value; EmitChanged(); } }
	[Export(PropertyHint.Range, "0,1,0.01")] public float ShadowAlpha { get => _shadowAlpha; set { _shadowAlpha = value; EmitChanged(); } }

	// writes the colours the definitions resolve to. returns whether
	// anything changed, so a theme re-applying itself can stop when
	// nothing does
	public bool Apply(EditSharpTheme theme)
	{
		bool changed = false;

		if (_background != ThemeDefinition.None)
		{
			Color background = theme.Resolve(_background, _backgroundShade, _backgroundAlpha);
			if (BgColor != background) { BgColor = background; changed = true; }
		}

		if (_border != ThemeDefinition.None)
		{
			Color border = theme.Resolve(_border, _borderShade, _borderAlpha);
			if (BorderColor != border) { BorderColor = border; changed = true; }
		}

		if (_shadow != ThemeDefinition.None)
		{
			Color shadow = theme.Resolve(_shadow, _shadowShade, _shadowAlpha);
			if (ShadowColor != shadow) { ShadowColor = shadow; changed = true; }
		}

		return changed;
	}

	// takes the shape of another flat stylebox - a plain one being made
	// themed keeps everything it had, colours included, until picks are made
	public void CopyShapeFrom(StyleBoxFlat other)
	{
		BgColor = other.BgColor;
		BorderColor = other.BorderColor;
		ShadowColor = other.ShadowColor;
		DrawCenter = other.DrawCenter;
		BorderBlend = other.BorderBlend;
		CornerRadiusTopLeft = other.CornerRadiusTopLeft;
		CornerRadiusTopRight = other.CornerRadiusTopRight;
		CornerRadiusBottomRight = other.CornerRadiusBottomRight;
		CornerRadiusBottomLeft = other.CornerRadiusBottomLeft;
		CornerDetail = other.CornerDetail;
		BorderWidthLeft = other.BorderWidthLeft;
		BorderWidthTop = other.BorderWidthTop;
		BorderWidthRight = other.BorderWidthRight;
		BorderWidthBottom = other.BorderWidthBottom;
		ContentMarginLeft = other.ContentMarginLeft;
		ContentMarginTop = other.ContentMarginTop;
		ContentMarginRight = other.ContentMarginRight;
		ContentMarginBottom = other.ContentMarginBottom;
		ExpandMarginLeft = other.ExpandMarginLeft;
		ExpandMarginTop = other.ExpandMarginTop;
		ExpandMarginRight = other.ExpandMarginRight;
		ExpandMarginBottom = other.ExpandMarginBottom;
		ShadowSize = other.ShadowSize;
		ShadowOffset = other.ShadowOffset;
		Skew = other.Skew;
		AntiAliasing = other.AntiAliasing;
		AntiAliasingSize = other.AntiAliasingSize;
	}
}
