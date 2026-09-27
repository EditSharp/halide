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
			Size = new Vector2I(1600, 900),
			// small enough for every snap zone, thirds included
			MinSize = new Vector2I(640, 360),
			WrapControls = false,
			// popups stay inside the window, as they did in the one-window app
			GuiEmbedSubwindows = true,
		};
		Screens.Place(window);

		window.Session = ProjectSession.Attach(window, project, filePath);

		// finalizers left running while the scene is made can race Godot swapping script handles (a crash)
		System.GC.Collect();
		System.GC.WaitForPendingFinalizers();
		Control editor = editorScene.Instantiate<Control>();
		window.Frame = WindowFrame.Wrap(editor);
		window.AddChild(window.Frame);

		return window;
	}

	static string WindowTitle(string filePath) => filePath is null ? "EditSharp" : $"{System.IO.Path.GetFileNameWithoutExtension(filePath)} - EditSharp";

	// where the window last was while neither maximized nor fullscreen, which is what a project saves
	public Rect2I RestoredRect => Mode == ModeEnum.Windowed ? WindowChrome.ContentRect(this) : restored;

	// a windowed place only counts once it has held still: going fullscreen or maximized passes through
	// in-between sizes while the mode still reads windowed
	Rect2I restored, pending;
	ulong pendingAt;
	const ulong SettleMs = 300;

	// set once closing is confirmed, so a stray signal during teardown never asks the OS about a window it has already torn down
	bool closing;

	void RememberRestored()
	{
		if (closing || Mode != ModeEnum.Windowed) return;
		pending = WindowChrome.ContentRect(this);
		pendingAt = Godot.Time.GetTicksMsec();
	}

	public override void _Process(double delta)
	{
		if (Mode != ModeEnum.Windowed) pending = default;
		else if (pending.HasArea() && Godot.Time.GetTicksMsec() - pendingAt >= SettleMs) (restored, pending) = (pending, default);
	}

	// back where a project was left, fitted to a screen (monitors change, and older saves kept fullscreen sizes)
	public void Restore(Rect2I rect, bool maximized)
	{
		restored = Screens.Fit(rect);
		pending = default;
		InitialPosition = WindowInitialPosition.Absolute;
		WindowChrome.Place(this, restored);
		// after the chrome has taken the frame, so the OS keeps the restored size without a caption in it
		if (maximized) Callable.From(() => { if (IsInstanceValid(this)) Mode = ModeEnum.Maximized; }).CallDeferred();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationWMPositionChanged) RememberRestored();
	}

	public override void _Ready()
	{
		if (Mode == ModeEnum.Windowed && !restored.HasArea()) restored = WindowChrome.ContentRect(this);
		SizeChanged += RememberRestored;
		CloseRequested += () => _ = CloseAsync();
		FocusEntered += Session.Activate;
		Session.Activate();

		Session.Project.History.Changed += (_, _) => UpdateTitle();
		Frame.Bar.HomePressed += ProjectManager.Singleton.ShowHome;
		UpdateTitle();

		// the menus: in the bar, or macOS's own menu bar
		BarMenus barMenus = null;
		if (OS.GetName() == "macOS") EditSharpGUI.Scripts.App.Chrome.Platform.MacGlobalMenu.Install();
		else barMenus = BarMenus.Attach(this, Frame.Bar);

		// the layout switcher shows the active layout and lists them all
		if (Frame.Content.GetChild(0) is Editor editor)
		{
			Frame.Bar.LayoutName = editor.Layouts.Active;
			editor.Layouts.Changed += () => Frame.Bar.LayoutName = editor.Layouts.Active;
			// with bar menus, the switcher is one of them; macOS's bar has only the switcher
			if (barMenus is null) Frame.Bar.LayoutsPressed += () =>
			{
				EditSharpGUI.Scripts.UI.ContextMenu.ContextMenu menu = new();
				BarMenus.FillLayouts(menu.Elements, EditSharpGUI.Scripts.App.Commands.CommandContext.Of(this));
				EditSharpGUI.Scripts.UI.ContextMenu.ContextMenus.ShowContextMenuBelow(menu, Frame.Bar.LayoutsAnchor);
			};
		}
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

		closing = true;
		ProjectManager.Singleton.Closed(this);
		QueueFree();
		return true;
	}
}
