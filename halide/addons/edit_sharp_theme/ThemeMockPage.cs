using Halide.Addons.Theme;
using Halide.Scripts.UI.Theming;
using Godot;
using System.Collections.Generic;

// the mock page as a scene you can open: every inspector component at its
// real size and in every state, the base controls, the view variations,
// under the project theme. built when the scene opens in the editor, and
// again when it runs, so the components that only lay out under a parent
// can be seen without running the app
[Tool]
public partial class ThemeMockPage : Control
{
	[Export] VBoxContainer content;
	[Export] PackedScene inspectorScene;

	readonly List<(ColorRect Tile, ThemeDefinition Definition)> palette = [];
	EditSharpTheme theme;

	public override void _Ready()
	{
		string path = ProjectSettings.GetSetting("gui/theme/custom").AsString();
		theme = string.IsNullOrEmpty(path) ? null : ResourceLoader.Load<Godot.Theme>(path) as EditSharpTheme;
		if (theme is null || content is null) return;

		// no theme of its own: as a preview in godot's theme editor it wears
		// the theme being edited, and run on its own it wears the project's
		Callable on = new(this, MethodName.OnThemeChanged);
		if (!theme.IsConnected(Resource.SignalName.Changed, on)) theme.Connect(Resource.SignalName.Changed, on);

		foreach (Node child in content.GetChildren()) child.QueueFree();
		ThemeMock.Build(content, theme, inspectorScene, palette);
	}

	public override void _ExitTree()
	{
		if (theme is not null && theme.IsConnected(Resource.SignalName.Changed, new Callable(this, MethodName.OnThemeChanged))) theme.Disconnect(Resource.SignalName.Changed, new Callable(this, MethodName.OnThemeChanged));
	}

	void OnThemeChanged()
	{
		foreach ((ColorRect tile, ThemeDefinition definition) in palette)
		{
			if (GodotObject.IsInstanceValid(tile)) tile.Color = theme.Resolve(definition);
		}
	}
}
