using Godot;
using System.Threading.Tasks;

// one open project: an OS window holding its session and an editor. its
// history is the one writes go to while it has focus. closing it asks about
// unsaved changes first
public partial class ProjectWindow : Window
{
	public ProjectSession Session { get; private set; }

	public static ProjectWindow Create(Project project, string filePath, PackedScene editorScene)
	{
		ProjectWindow window = new()
		{
			Title = WindowTitle(filePath),
			Size = new Vector2I(1600, 900),
			MinSize = new Vector2I(960, 540),
			WrapControls = false,
			// popups stay inside the window, as they did in the one-window app
			GuiEmbedSubwindows = true,
		};
		Screens.Place(window);

		window.Session = ProjectSession.Attach(window, project, filePath);

		Control editor = editorScene.Instantiate<Control>();
		editor.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		window.AddChild(editor);

		return window;
	}

	static string WindowTitle(string filePath) => filePath is null ? "EditSharp" : $"{System.IO.Path.GetFileNameWithoutExtension(filePath)} - EditSharp";

	public override void _Ready()
	{
		CloseRequested += () => _ = CloseAsync();
		FocusEntered += Session.Activate;
		Session.Activate();

		Session.Project.History.Changed += (_, _) => UpdateTitle();
	}

	// the name, with a dot while there are unsaved changes
	public void UpdateTitle() => Title = (Session.Dirty ? "• " : "") + WindowTitle(Session.FilePath);

	// true once the window is gone; false when the user kept it open
	public async Task<bool> CloseAsync()
	{
		if (!await ProjectManager.Singleton.ConfirmCloseAsync(Session)) return false;

		ProjectManager.Singleton.Closed(this);
		QueueFree();
		return true;
	}
}
