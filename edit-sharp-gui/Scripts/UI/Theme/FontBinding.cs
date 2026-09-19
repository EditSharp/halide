using Godot;

namespace EditSharpGUI.Scripts.UI.Theming;

// a theme type's font and font size, by role: the UI or mono font, and
// the small, normal or title size. None leaves that item as it is
[Tool, GlobalClass]
public partial class FontBinding : Resource
{
	[Export] public string ThemeType { get; set; } = "";
	[Export] public ThemeFontRole Font { get; set; }
	[Export] public ThemeFontSizeRole Size { get; set; }

	public bool Apply(EditSharpTheme theme)
	{
		if (string.IsNullOrEmpty(ThemeType)) return false;

		bool changed = false;

		if (Font != ThemeFontRole.None && theme.ResolveFont(Font) is Godot.Font font)
		{
			if (!theme.HasFont("font", ThemeType) || theme.GetFont("font", ThemeType) != font)
			{
				theme.SetFont("font", ThemeType, font);
				changed = true;
			}
		}

		if (Size != ThemeFontSizeRole.None)
		{
			int size = theme.ResolveFontSize(Size);

			if (!theme.HasFontSize("font_size", ThemeType) || theme.GetFontSize("font_size", ThemeType) != size)
			{
				theme.SetFontSize("font_size", ThemeType, size);
				changed = true;
			}
		}

		return changed;
	}
}
