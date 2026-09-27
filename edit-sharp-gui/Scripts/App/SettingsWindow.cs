using Godot;

// the one App Settings window; opening it again brings it forward
public partial class SettingsWindow : Window
{
	const string Scene = "res://Scenes/Settings/AppSettings.tscn";

	static SettingsWindow open;

	public static void Open()
	{
		if (IsInstanceValid(open) && !open.IsQueuedForDeletion())
		{
			open.MoveToForeground();
			open.GrabFocus();
			return;
		}

		open = new SettingsWindow
		{
			Title = "App Settings",
			Size = Screens.Sized(new Vector2I(960, 660)),
			MinSize = Screens.Sized(new Vector2I(720, 480)),
			WrapControls = false,
			// popups stay inside the window
			GuiEmbedSubwindows = true,
		};
		Screens.Place(open);

		Control page = GD.Load<PackedScene>(Scene).Instantiate<Control>();
		EditSharpGUI.Scripts.App.Chrome.WindowFrame frame = EditSharpGUI.Scripts.App.Chrome.WindowFrame.Wrap(page);
		open.AddChild(frame);
		frame.Bar.ShowsLogo = false;
		frame.Bar.Title = "Settings";

		((SceneTree)Engine.GetMainLoop()).Root.AddChild(open);
		Screens.Scale(open);
		open.Show();
	}

	public override void _Ready() => CloseRequested += QueueFree;
}
