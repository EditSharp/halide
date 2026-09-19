using EditSharpGUI.Scripts.UI.Theming;
using Godot;

// writes every default item into main_theme.tres, so the theme file holds
// the whole look and can be edited in godot's theme editor. items that
// already exist in the file are left exactly as they are; only what is
// missing is added. run headless:
//
//   godot --headless --path . res://Scenes/Tests/ThemeBake.tscn
public partial class ThemeBake : Node
{
	const string Path = "res://main_theme.tres";

	public override void _Ready()
	{
		if (GD.Load<Theme>(Path) is not EditSharpTheme theme)
		{
			GD.PrintErr("BAKE FAIL: main_theme.tres is not an EditSharpTheme");
			GetTree().Quit(1);
			return;
		}

		// `++ --reset-type=X` drops X's items first, so they come back as the
		// current defaults - for a type whose defaults changed in code
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (!arg.StartsWith("--reset-type=")) continue;

			string type = arg["--reset-type=".Length..];
			foreach (string item in theme.GetStyleboxList(type)) theme.ClearStylebox(item, type);
			foreach (string item in theme.GetColorList(type)) theme.ClearColor(item, type);
			foreach (string item in theme.GetConstantList(type)) theme.ClearConstant(item, type);
			for (int i = theme.ColorBindings.Count - 1; i >= 0; i--) if (theme.ColorBindings[i]?.ThemeType == type) theme.ColorBindings.RemoveAt(i);
			GD.Print($"BAKE reset {type}");
		}

		theme.EnsureDefaults();
		theme.ApplyDefinitions();

		Error error = ResourceSaver.Save(theme, Path);

		int styleboxes = 0;
		foreach (string type in theme.GetTypeList()) styleboxes += theme.GetStyleboxList(type).Length;

		GD.Print($"BAKE {error}: {theme.GetTypeList().Length} types, {styleboxes} styleboxes, {theme.ColorBindings.Count} colour bindings, {theme.FontBindings.Count} font bindings");
		GetTree().Quit(error == Error.Ok ? 0 : 1);
	}
}
