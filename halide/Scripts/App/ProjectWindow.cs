using Halide.Scripts.App.Chrome;
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
		// clamped to whatever screen is actually available: a fixed 1600x900 is comfortably smaller than
		// any real monitor, but not necessarily smaller than a CI runner's virtual display -- found via a
		// real (much smaller) screen in GitHub Actions, where the unclamped size opened partly off-screen
		Vector2I size = Screens.Fit(new Rect2I(Vector2I.Zero, new Vector2I(1600, 900))).Size;
		ProjectWindow window = new()
		{
			Title = WindowTitle(filePath),
			Size = size,
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

	static string WindowTitle(string filePath) => filePath is null ? "Halide" : $"{System.IO.Path.GetFileNameWithoutExtension(filePath)} - Halide";

	// where the window last was while neither maximized nor fullscreen, which is what a project saves
	public Rect2I RestoredRect =>
		Mode == ModeEnum.Windowed && Godot.Time.GetTicksMsec() >= suppressRememberedUntil
			? WindowChrome.ContentRect(this)
			: restored;

	// a windowed place only counts once it has held still: going fullscreen or maximized passes through
	// in-between sizes while the mode still reads windowed -- on macOS specifically, Zoom/fullscreen is an
	// animated OS transition (~300-400ms) taking Godot's own Mode property along for the ride, so a
	// position/size notification fired mid-animation can still read Mode as Windowed. 500ms comfortably
	// outlasts that while still being well under what a person dragging/resizing by hand would notice
	Rect2I restored, pending;
	ulong pendingAt;
	const ulong SettleMs = 500;

	// sets an authoritative `restored` itself (see Restore below); RememberRestored must not clobber that
	// with a mid-transition read from the deferred Maximize/Fullscreen that follows it, so it's suppressed
	// for a bit longer than one animated transition should ever take
	ulong suppressRememberedUntil;
	const ulong SuppressMs = 800;

	// set once closing is confirmed, so a stray signal during teardown never asks the OS about a window it has already torn down
	bool closing;
	Window.ModeEnum lastMode;

	void RememberRestored()
	{
		if (closing || Mode != ModeEnum.Windowed) return;
		if (Godot.Time.GetTicksMsec() < suppressRememberedUntil)
		{
			GD.Print($"[WindowState] RememberRestored suppressed ({WindowChrome.ContentRect(this)} ignored, {suppressRememberedUntil - Godot.Time.GetTicksMsec()}ms left)");
			return;
		}
		pending = WindowChrome.ContentRect(this);
		pendingAt = Godot.Time.GetTicksMsec();
	}

	public override void _Process(double delta)
	{
		if (Mode != lastMode)
		{
			bool restored = Mode == ModeEnum.Windowed && lastMode != ModeEnum.Windowed;
			lastMode = Mode;
			if (restored && OS.GetName() == "macOS")
				_ = ReapplyRestoredRectAsync(this.restored);
		}

		if (Godot.Time.GetTicksMsec() < suppressRememberedUntil) { pending = default; return; }
		if (Mode != ModeEnum.Windowed) { pending = default; return; }
		if (!pending.HasArea() || Godot.Time.GetTicksMsec() - pendingAt < SettleMs) return;

		if (pending != restored) GD.Print($"[WindowState] settled restored {restored} -> {pending}");
		(restored, pending) = (pending, default);
	}

	// back where a project was left, fitted to a screen (monitors change, and older saves kept fullscreen sizes)
	public void Restore(Rect2I rect, bool maximized)
	{
		restored = Screens.Fit(rect);
		pending = default;
		ulong suppression = OS.GetName() == "macOS" ? 2000 : SuppressMs;
		suppressRememberedUntil = Godot.Time.GetTicksMsec() + suppression;
		InitialPosition = WindowInitialPosition.Absolute;
		WindowChrome.Place(this, restored);
		GD.Print($"[WindowState] Restore({rect}, maximized={maximized}) -> restored={restored}, ModeNow={Mode}");
		// after the chrome has taken the frame, so the OS keeps the restored size without a caption in it.
		// diagnostic-only for the non-maximized case for now: an earlier version of this also set Mode =
		// Windowed explicitly there (on a hunch that macOS might carry a new window into an already-
		// fullscreen Space), but that runs on every ordinary reopen, not just the fullscreen scenario it
		// was meant for, and the very next CI run broke on all three platforms -- reverted without real
		// evidence it was the cause, since a hypothesis this expensive to get wrong needs proof first
		Callable.From(() =>
		{
			if (!IsInstanceValid(this)) return;
			if (maximized) Mode = ModeEnum.Maximized;
			else if (OS.GetName() == "macOS" && Mode != ModeEnum.Windowed) Mode = ModeEnum.Windowed;
			GD.Print($"[WindowState] Restore's deferred callback: maximized={maximized}, Mode={Mode}");
			if (OS.GetName() == "macOS" && !maximized) _ = ReapplyRestoredRectAsync(restored);
		}).CallDeferred();
	}

	// macOS completes title-bar and Space transitions asynchronously. Reapply the saved content
	// rectangle after the transition so Cocoa's intermediate frame notifications cannot resize it.
	bool reapplyingRestoredRect;
	async Task ReapplyRestoredRectAsync(Rect2I rect)
	{
		if (reapplyingRestoredRect) return;
		reapplyingRestoredRect = true;
		try
		{
			await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
			if (!GodotObject.IsInstanceValid(this)) return;
			if (Mode != ModeEnum.Windowed) Mode = ModeEnum.Windowed;
			await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
			if (GodotObject.IsInstanceValid(this) && Mode == ModeEnum.Windowed) WindowChrome.Place(this, rect);
		}
		finally { reapplyingRestoredRect = false; }
	}

	public override void _Notification(int what)
	{
		if (what == NotificationWMPositionChanged) RememberRestored();
	}

	public override void _Ready()
	{
		lastMode = Mode;
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
		if (OS.GetName() == "macOS") Halide.Scripts.App.Chrome.Platform.MacGlobalMenu.Install();
		else barMenus = BarMenus.Attach(this, Frame.Bar);

		// the layout switcher shows the active layout and lists them all
		if (Frame.Content.GetChild(0) is Editor editor)
		{
			Frame.Bar.LayoutName = editor.Layouts.Active;
			editor.Layouts.Changed += () => Frame.Bar.LayoutName = editor.Layouts.Active;
			// with bar menus, the switcher is one of them; macOS's bar has only the switcher
			if (barMenus is null) Frame.Bar.LayoutsPressed += () =>
			{
				Halide.Scripts.UI.ContextMenu.ContextMenu menu = new();
				BarMenus.FillLayouts(menu.Elements, Halide.Scripts.App.Commands.CommandContext.Of(this));
				Halide.Scripts.UI.ContextMenu.ContextMenus.ShowContextMenuBelow(menu, Frame.Bar.LayoutsAnchor);
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
