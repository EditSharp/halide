using EditSharpGUI.Scripts.App.Chrome;
using Godot;
using System.Threading.Tasks;

// the OS window for one open project; closing it asks about unsaved changes
public partial class ProjectWindow : Window
{
	public ProjectSession Session { get; private set; }

	// the drawn title bar over the editor
	public WindowFrame Frame { get; private set; }

	public static ProjectWindow Create(Project project, string filePath, PackedScene editorScene)
	{
		ProjectWindow window = new()
		{
			Title = WindowTitle(filePath),
			Size = Screens.Sized(new Vector2I(1600, 900)),
			// small enough for every snap zone, thirds included
			MinSize = Screens.Sized(new Vector2I(640, 360)),
			WrapControls = false,
			// popups stay inside the window, as they did in the one-window app
			GuiEmbedSubwindows = true,
		};
		Screens.Place(window);

		window.Session = ProjectSession.Attach(window, project, filePath);

		Control editor = editorScene.Instantiate<Control>();
		window.Frame = WindowFrame.Wrap(editor);
		window.AddChild(window.Frame);

		return window;
	}

	static string WindowTitle(string filePath) => filePath is null ? "EditSharp" : $"{System.IO.Path.GetFileNameWithoutExtension(filePath)} - EditSharp";

	public override void _Ready()
	{
		CloseRequested += () => _ = CloseAsync();
		FocusEntered += Session.Activate;
		Session.Activate();

		Session.Project.History.Changed += (_, _) => UpdateTitle();
		Frame.Bar.HomePressed += ProjectManager.Singleton.ShowHome;
		UpdateTitle();
	}

	// the name, with a dot while there are unsaved changes
	public void UpdateTitle()
	{
		Title = (Session.Dirty ? "• " : "") + WindowTitle(Session.FilePath);
		Frame.Bar.Title = (Session.Dirty ? "• " : "") + (Session.FilePath is null ? "Untitled" : System.IO.Path.GetFileNameWithoutExtension(Session.FilePath));
	}

	// true once the window is gone; false when the user kept it open
	public async Task<bool> CloseAsync()
	{
		if (!await ProjectManager.Singleton.ConfirmCloseAsync(Session)) return false;

		ProjectManager.Singleton.Closed(this);
		QueueFree();
		return true;
	}
}
