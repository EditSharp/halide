using EditSharp.Editing;
using EditSharpGUI.Scripts.UI.Inspecting;
using EditSharpGUI.Scripts.UI.Theming;
using Godot;
using SkiaSharp;
using System.Collections.Generic;

// App Settings' Appearance page: theme, accent and interface scale, applied and saved as they change
public sealed class AppearancePage : IChoiceProvider, IPropertyDefaults
{
	static EditSharpTheme Theme => ThemeDB.GetProjectTheme() as EditSharpTheme;

	[Editable("Theme", Group = "Theme", Order = 0, Default = EditSharpTheme.SystemPalette)]
	public string Palette
	{
		get => Theme?.PaletteChoice is { Length: > 0 } choice ? choice : EditSharpTheme.SystemPalette;
		set => Theme?.ChoosePalette(value);
	}

	[Editable("Accent", Group = "Theme", Order = 1, Tooltip = "The highlight colour; reset returns to the theme's own")]
	public SKColor Accent
	{
		get => ColorEditor.ToSk(Theme?.UserAccent ?? Theme?.PaletteAccent ?? Colors.White);
		set
		{
			Color picked = ColorEditor.ToGodot(value);
			Theme?.ChooseAccent(Theme is { } theme && picked.IsEqualApprox(theme.PaletteAccent) ? null : picked);
		}
	}

	[Editable("Interface scale", Group = "Interface", Order = 2, Default = InterfaceScale.System)]
	public InterfaceScale Scale
	{
		get => AppSettings.Current.InterfaceScale;
		set
		{
			AppSettings.Current.InterfaceScale = value;
			AppSettings.Current.Save();
		}
	}

	public IReadOnlyList<Choice> ChoicesFor(string property) => property switch
	{
		nameof(Palette) => [new(EditSharpTheme.SystemPalette, "Follow system"), new(EditSharpTheme.DarkPalette, "Dark"), new(EditSharpTheme.LightPalette, "Light")],
		nameof(Scale) =>
		[
			new(InterfaceScale.System, "Follow system"), new(InterfaceScale.Percent75, "75%"), new(InterfaceScale.Percent100, "100%"),
			new(InterfaceScale.Percent125, "125%"), new(InterfaceScale.Percent150, "150%"), new(InterfaceScale.Percent175, "175%"), new(InterfaceScale.Percent200, "200%"),
		],
		_ => null,
	};

	public bool TryGetDefault(string propertyName, out object value)
	{
		value = propertyName == nameof(Accent) ? ColorEditor.ToSk(Theme?.PaletteAccent ?? Colors.White) : null;
		return value is not null;
	}
}
