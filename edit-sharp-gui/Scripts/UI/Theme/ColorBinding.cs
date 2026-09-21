using Godot;

namespace EditSharpGUI.Scripts.UI.Theming;

// a plain colour item of the theme - a font colour, one of the named
// Clip or Node colours - and the definition it takes its value from.
// godot reads a colour item as a bare value, so unlike a stylebox it
// cannot resolve itself; the theme writes it from this when the palette
// or the binding changes
[Tool, GlobalClass]
public partial class ColorBinding : Resource
{
	[Export] public string ThemeType { get; set; } = "";
	[Export] public string Item { get; set; } = "";
	[Export] public ThemeDefinition Definition { get; set; } = ThemeDefinition.FontColor1;
	[Export] public ThemeShade Shade { get; set; }
	[Export(PropertyHint.Range, "0,1,0.01")] public float Alpha { get; set; } = 1f;

	public bool Apply(Theme theme, ThemePalette palette)
	{
		if (palette is null || Definition == ThemeDefinition.None || string.IsNullOrEmpty(ThemeType) || string.IsNullOrEmpty(Item)) return false;

		Color colour = palette.Resolve(Definition, Shade, Alpha);

		if (theme.HasColor(Item, ThemeType) && theme.GetColor(Item, ThemeType) == colour) return false;

		theme.SetColor(Item, ThemeType, colour);
		return true;
	}
}
