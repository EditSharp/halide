using Godot;

namespace EditSharpGUI.Scripts.UI.Theming;

// a flat stylebox whose colours come from the theme's definitions. its
// shape - corners, borders, margins, shadow - is edited in godot's theme
// editor like any stylebox and is never touched by the theme; only the
// background and border colours are written, from whichever definitions
// are picked here, whenever the definitions change
[Tool, GlobalClass]
public partial class ThemedStyleBox : StyleBoxFlat
{
	ThemeDefinition _background = ThemeDefinition.BackgroundColor1;
	ThemeShade _backgroundShade;
	float _backgroundAlpha = 1f;
	ThemeDefinition _border = ThemeDefinition.None;
	ThemeShade _borderShade;
	float _borderAlpha = 1f;

	[ExportGroup("Definitions")]
	[Export] public ThemeDefinition Background { get => _background; set { _background = value; EmitChanged(); } }
	[Export] public ThemeShade BackgroundShade { get => _backgroundShade; set { _backgroundShade = value; EmitChanged(); } }
	[Export(PropertyHint.Range, "0,1,0.01")] public float BackgroundAlpha { get => _backgroundAlpha; set { _backgroundAlpha = value; EmitChanged(); } }
	[Export] public ThemeDefinition Border { get => _border; set { _border = value; EmitChanged(); } }
	[Export] public ThemeShade BorderShade { get => _borderShade; set { _borderShade = value; EmitChanged(); } }
	[Export(PropertyHint.Range, "0,1,0.01")] public float BorderAlpha { get => _borderAlpha; set { _borderAlpha = value; EmitChanged(); } }

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

		return changed;
	}
}
