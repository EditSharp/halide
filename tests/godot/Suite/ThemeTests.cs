using Halide.Scripts.UI.Theming;
using Godot;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Halide.Tests;

// the theme holds what the scenes and code ask of it, and both palettes define every colour
[TestFixture]
public sealed class ThemeTests
{
	static Theme Theme => ThemeDB.GetProjectTheme();

	// every theme_type_variation named in a scene under res://Scenes
	static IEnumerable<(string Scene, string Variation)> SceneVariations()
	{
		string root = ProjectSettings.GlobalizePath("res://Scenes");
		foreach (string file in Directory.GetFiles(root, "*.tscn", SearchOption.AllDirectories))
			foreach (Match m in Regex.Matches(File.ReadAllText(file), "theme_type_variation = &\"(\\w+)\""))
				yield return (Path.GetFileName(file), m.Groups[1].Value);
	}

	[Test]
	public void TheProjectThemeIsOurs() => Assert.True(Theme is EditSharpTheme);

	[Test]
	public void EveryVariationTheScenesUseExists()
	{
		foreach ((string scene, string variation) in SceneVariations())
			Assert.NotEqual("", Theme.GetTypeVariationBase(variation).ToString(), $"{scene} uses '{variation}'");
	}

	[Test]
	public void EveryVariationTheCodeSetsExists()
	{
		string root = ProjectSettings.GlobalizePath("res://Scripts");
		foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("Tests")))
			foreach (Match m in Regex.Matches(File.ReadAllText(file), "ThemeTypeVariation = (?:[^\"]*?)\"(\\w+)\""))
				Assert.NotEqual("", Theme.GetTypeVariationBase(m.Groups[1].Value).ToString(), $"{Path.GetFileName(file)} sets '{m.Groups[1].Value}'");
	}

	[Test]
	public void TheDockLooksHaveTheirPanels()
	{
		foreach (string look in new[] { "DockCompassTile", "DockCompassTileHot", "DockCompassPane", "DockCompassFill", "DockPreview", "DockInsertMarker", "DockTabGhost" })
			Assert.True(Theme.HasStylebox("panel", look), look);
	}

	[Test]
	public void TheBarUsesOneOsFont()
	{
		Font button = Theme.GetFont("font", "TopBarButton");
		Assert.True(button is SystemFont, "the bar's buttons use a system font");
		Assert.Equal(button, Theme.GetFont("font", "TopBarTitle"));
		Assert.Equal(button, Theme.GetFont("font", "TopBarTabs"));
		Assert.Equal(12, Theme.GetFontSize("font_size", "TopBarTitle"));
	}

	[Test]
	public void BothPalettesDefineEveryColour()
	{
		foreach (string path in new[] { "res://Themes/Dark.tres", "res://Themes/Light.tres" })
		{
			ThemePalette palette = GD.Load<ThemePalette>(path);
			foreach (var property in typeof(ThemePalette).GetProperties().Where(p => p.PropertyType == typeof(Color) && p.CanRead))
			{
				Color c = (Color)property.GetValue(palette);
				Assert.True(c.A > 0 || property.Name.Contains("Transparent"), $"{Path.GetFileName(path)} defines {property.Name}");
			}
		}
	}

	[Test]
	public void SwitchingPalettesRecolours()
	{
		EditSharpTheme theme = (EditSharpTheme)Theme;
		string original = theme.PaletteChoice;
		try
		{
			theme.ChoosePalette("res://Themes/Light.tres");
			Color light = theme.Palette.BackgroundColor1;
			theme.ChoosePalette("res://Themes/Dark.tres");
			Assert.NotEqual(light, theme.Palette.BackgroundColor1);
		}
		finally { theme.ChoosePalette(original); }
	}
}
