using Godot;

namespace Halide.Scripts.UI.Theming;

// a theme type's font and size: the family (UI or mono), the weight of the
// UI font, and the size role. godot reads font items directly, so the
// theme writes them from this when the palette or the binding changes.
// None for the family leaves the font item alone, None for the size the
// size item
[Tool, GlobalClass]
public partial class FontBinding : Resource
{
	[Export] public string ThemeType { get; set; } = "";
	[Export] public ThemeFontFamily Family { get; set; }
	[Export] public ThemeFontWeight Weight { get; set; } = ThemeFontWeight.Regular;
	[Export] public ThemeFontSizeRole Size { get; set; }

	public bool Apply(Theme theme, ThemePalette palette)
	{
		if (palette is null || string.IsNullOrEmpty(ThemeType)) return false;

		bool changed = false;

		if (Family != ThemeFontFamily.None && palette.ResolveFont(Family, Weight) is Font font)
		{
			if (!theme.HasFont("font", ThemeType) || theme.GetFont("font", ThemeType) != font)
			{
				theme.SetFont("font", ThemeType, font);
				changed = true;
			}
		}

		if (Size != ThemeFontSizeRole.None)
		{
			int size = palette.ResolveFontSize(Size);

			if (!theme.HasFontSize("font_size", ThemeType) || theme.GetFontSize("font_size", ThemeType) != size)
			{
				theme.SetFontSize("font_size", ThemeType, size);
				changed = true;
			}
		}

		return changed;
	}
}
