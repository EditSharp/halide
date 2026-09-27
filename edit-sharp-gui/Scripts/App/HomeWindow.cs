using Godot;

// the OS window holding Home; closing it leaves open projects running
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

		window.home = homeScene.Instantiate<Home>();
		window.frame = EditSharpGUI.Scripts.App.Chrome.WindowFrame.Wrap(window.home);
		window.AddChild(window.frame);

		return window;
	}

	EditSharpGUI.Scripts.App.Chrome.WindowFrame frame;
	Home home;

	public override void _Ready()
	{
		frame.Bar.Title = "EditSharp";
		frame.Bar.LogoInert = true;
		frame.Bar.Hold(home.Sources);

		CloseRequested += () =>
		{
			ProjectManager.Singleton.Closed(this);
			QueueFree();
		};
	}
}
