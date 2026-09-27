using Godot;

namespace Halide.Scripts.UI.Theming;

// a plain colour item of the theme - a font colour, one of the named
// Clip or Node colours - and where it takes its value from: a definition,
// a swatch, or the swatch the palette picks for the item's kind. godot
// reads a colour item as a bare value, so unlike a stylebox it cannot
// resolve itself; the theme writes it from this when the palette or the
// binding changes
[Tool, GlobalClass]
public partial class ColorBinding : Resource
{
	[Export] public string ThemeType { get; set; } = "";
	[Export] public string Item { get; set; } = "";
	[Export] public ColorSource Source { get; set; } = ColorSource.Definition;
	[Export] public ThemeDefinition Definition { get; set; } = ThemeDefinition.FontColor1;
	[Export] public ClipSwatch ClipSwatch { get; set; }
	[Export] public NodeSwatch NodeSwatch { get; set; }
	[Export] public ThemeShade Shade { get; set; }
	[Export(PropertyHint.Range, "0,1,0.01")] public float Alpha { get; set; } = 1f;

	// the colour this binding resolves to, or null when it binds nothing
	public Color? Resolve(ThemePalette palette)
	{
		if (palette is null) return null;

		Color? colour = Source switch
		{
			ColorSource.ClipSwatch => palette.Resolve(ClipSwatch),
			ColorSource.NodeSwatch => palette.Resolve(NodeSwatch),
			ColorSource.ClipKind => palette.ClipKindSwatch(Item) is ClipSwatch clip ? palette.Resolve(clip) : null,
			ColorSource.NodeKind => palette.NodeKindSwatch(Item) is NodeSwatch node ? palette.Resolve(node) : null,
			_ => Definition == ThemeDefinition.None ? null : palette.Resolve(Definition)
		};

		return colour is Color c ? palette.Shade(c, Shade, Alpha) : null;
	}

	public bool Apply(Theme theme, ThemePalette palette)
	{
		if (string.IsNullOrEmpty(ThemeType) || string.IsNullOrEmpty(Item) || Resolve(palette) is not Color colour) return false;

		if (theme.HasColor(Item, ThemeType) && theme.GetColor(Item, ThemeType) == colour) return false;

		theme.SetColor(Item, ThemeType, colour);
		return true;
	}
}
