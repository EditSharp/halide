using EditSharpGUI.Scripts.App.Platform;
using EditSharpGUI.Scripts.UI.ContextMenu;
using Godot;
using System.Linq;

// the main scene: the app's root window, never shown, with the tray icon in
// it. Home and every project are windows of their own, opened from here
public partial class Host : Node
{
	[Export] Texture2D trayIcon;

	StatusIndicator tray;

	public override void _Ready()
	{
		Window root = GetTree().Root;

		// home and projects are OS windows of their own
		root.GuiEmbedSubwindows = false;
		Callable.From(() => HostWindow.Hide(root)).CallDeferred();

		tray = new StatusIndicator { Icon = trayIcon, Tooltip = "EditSharp" };
		tray.Pressed += OnTrayPressed;
		AddChild(tray);

		// once the root has finished setting up, so windows can join it
		Callable.From(ProjectManager.Singleton.Start).CallDeferred();
	}

	// left: Home. right: the tray menu
	void OnTrayPressed(long button, Vector2I position)
	{
		if (button == (long)MouseButton.Left) ProjectManager.Singleton.ShowHome();
		else if (button == (long)MouseButton.Right) ShowTrayMenu();
	}

	// the tray reports the press; the menu opens on the release, as tray menus do
	async void ShowTrayMenu()
	{
		while (HostWindow.RightButtonHeld) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		ContextMenus.ShowContextMenu(TrayMenu(), from: null);
	}

	// Home, each open project, App Settings and Quit
	static ContextMenu TrayMenu()
	{
		ProjectManager manager = ProjectManager.Singleton;

		ContextButton home = new() { Id = "tray.home", Text = new("Home") };
		home.Pressed += manager.ShowHome;

		ContextButton settings = new() { Id = "tray.settings", Text = new("App Settings") };
		settings.Pressed += manager.ShowSettings;

		ContextButton quit = new() { Id = "tray.quit", Text = new("Quit") };
		quit.Pressed += manager.QuitAll;

		Godot.Collections.Array<ContextElement> elements = [home];

		if (manager.OpenProjects.Count > 0)
		{
			elements.Add(new ContextDivider());

			foreach (ProjectWindow window in manager.OpenProjects.ToList())
			{
				ContextButton project = new() { Text = new(System.IO.Path.GetFileNameWithoutExtension(window.Session.FilePath ?? "Untitled")) };
				project.Pressed += () => window.GrabFocus();
				elements.Add(project);
			}
		}

		elements.Add(new ContextDivider());
		elements.Add(settings);
		elements.Add(quit);

		return new ContextMenu { Elements = elements };
	}
}
