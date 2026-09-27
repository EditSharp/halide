using Godot;

// the launchpad: an OS window holding Home. closing it only closes it; the
// app goes on while any project is open
public partial class HomeWindow : Window
{
	public static HomeWindow Create(PackedScene homeScene)
	{
		HomeWindow window = new()
		{
			Title = "EditSharp",
			Size = new Vector2I(1280, 800),
			MinSize = new Vector2I(720, 480),
			WrapControls = false,
			// popups stay inside the window, as they did in the one-window app
			GuiEmbedSubwindows = true,
		};
		Screens.Place(window);

		Control home = homeScene.Instantiate<Control>();
		home.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		window.AddChild(home);

		return window;
	}

	public override void _Ready()
	{
		CloseRequested += () =>
		{
			ProjectManager.Singleton.Closed(this);
			QueueFree();
		};
	}
}
