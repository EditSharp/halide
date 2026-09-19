using Godot;

namespace EditSharpGUI.Scripts.UI.Theming;

// a plain colour item of the theme - a font colour, one of the named
// Clip or Node colours - and the definition it takes its value from.
// colour items are bare colours, not resources, so unlike a stylebox
// they cannot carry their own pick; these live in a list on the theme
[Tool, GlobalClass]
public partial class ColorBinding : Resource
{
	[Export] public string ThemeType { get; set; } = "";
	[Export] public string Item { get; set; } = "";
	[Export] public ThemeDefinition Definition { get; set; } = ThemeDefinition.FontColor1;
	[Export] public ThemeShade Shade { get; set; }
	[Export(PropertyHint.Range, "0,1,0.01")] public float Alpha { get; set; } = 1f;

	public bool Apply(EditSharpTheme theme)
	{
		if (Definition == ThemeDefinition.None || string.IsNullOrEmpty(ThemeType) || string.IsNullOrEmpty(Item)) return false;

		Color colour = theme.Resolve(Definition, Shade, Alpha);

		if (theme.HasColor(Item, ThemeType) && theme.GetColor(Item, ThemeType) == colour) return false;

		theme.SetColor(Item, ThemeType, colour);
		return true;
	}
}
