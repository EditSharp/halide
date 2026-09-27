using EditSharpGUI.Scripts.Input;
using EditSharpGUI.Scripts.UI.ContextMenu;
using EditSharpGUI.Scripts.UI.Dialogs;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// the launchpad: project tiles with search, sort and an Add tile; laid out in Home.tscn
public partial class Home : Control
{
	[ExportGroup("Controls")]

	[Export] TabBar tabs;
	[Export] LineEdit search;
	[Export] Button sortButton;
	[Export] HFlowContainer flow;
	[Export] Control addTile;
	[Export] Label empty;

	[ExportGroup("Scenes")]

	[Export] PackedScene tileScene;

	readonly Dictionary<string, UIProjectItem> tiles = new(StringComparer.OrdinalIgnoreCase);
	readonly HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
	string anchor;

	public override void _Ready()
	{
		// cloud projects come much later; the tab is there to say so
		if (tabs is not null && tabs.TabCount > 1)
		{
			tabs.SetTabDisabled(1, true);
			tabs.SetTabTooltip(1, "Cloud projects are coming in a later version.");
		}

		if (search is not null) search.TextChanged += _ => Rebuild();
		if (sortButton is not null) sortButton.Pressed += ShowSortMenu;
		if (addTile is not null) addTile.GuiInput += e => { if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) ShowAddProject(); };

		RecentProjects.Changed += Rebuild;
		GetWindow().FilesDropped += OnFilesDropped;
		InputManager.Singleton.Keyboard.Register(this, OnShortcut);

		UpdateSortLabel();
		Rebuild();
	}

	public override void _ExitTree()
	{
		RecentProjects.Changed -= Rebuild;
		InputManager.Singleton.Keyboard.Unregister(this);
	}

	// refreshed whenever Home comes back into view, for "edited 5 minutes ago"
	public override void _Notification(int what)
	{
		if (what == NotificationWMWindowFocusIn) foreach (UIProjectItem tile in tiles.Values) tile.Refresh();
	}

	// ---- the tiles ----

	IEnumerable<RecentProject> Shown()
	{
		string text = search?.Text.Trim() ?? "";
		IEnumerable<RecentProject> shown = RecentProjects.All.Where(p => text.Length == 0 || p.Name.Contains(text, StringComparison.OrdinalIgnoreCase));

		return AppSettings.Current.HomeSort switch
		{
			HomeSort.Name => shown.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
			HomeSort.Created => shown.OrderByDescending(p => p.Exists ? File.GetCreationTimeUtc(p.Path) : DateTime.MinValue),
			_ => shown.OrderByDescending(p => p.LastOpenedUtc),
		};
	}

	// the tiles for what's shown now, in order, reusing the ones already made
	public void Rebuild()
	{
		if (flow is null || tileScene is null) return;

		List<RecentProject> shown = [.. Shown()];
		HashSet<string> keep = new(shown.Select(p => p.Path), StringComparer.OrdinalIgnoreCase);

		foreach (string gone in tiles.Keys.Where(k => !keep.Contains(k)).ToList())
		{
			tiles[gone].QueueFree();
			tiles.Remove(gone);
		}

		selected.IntersectWith(keep);

		for (int i = 0; i < shown.Count; i++)
		{
			if (!tiles.TryGetValue(shown[i].Path, out UIProjectItem tile))
			{
				tile = tileScene.Instantiate<UIProjectItem>();
				tiles[shown[i].Path] = tile;
				flow.AddChild(tile);
			}

			tile.Setup(this, shown[i]);
			tile.Selected = selected.Contains(shown[i].Path);

			// after the add tile, which stays first
			flow.MoveChild(tile, i + 1);
		}

		if (empty is not null)
		{
			empty.Visible = shown.Count == 0;
			empty.Text = RecentProjects.All.Count == 0 ? "No projects yet. Add one to get started." : "No projects match the search.";
		}
	}

	// ---- selecting and opening ----

	public void TileInput(UIProjectItem tile, InputEvent @event)
	{
		if (@event is not InputEventMouseButton { Pressed: true } press) return;

		InputManager.Singleton.Keyboard.Capture(this);

		if (press.ButtonIndex == MouseButton.Left)
		{
			if (press.DoubleClick) { Open(tile.Project); return; }
			Select(tile, press.IsCommandOrControlPressed(), press.ShiftPressed);
		}
		else if (press.ButtonIndex == MouseButton.Right)
		{
			if (!tile.Selected) Select(tile, add: false, range: false);
			ShowTileMenu(tile, null);
		}
	}

	// plain: just this one; ctrl: toggled; shift: everything from the last one clicked
	void Select(UIProjectItem tile, bool add, bool range)
	{
		string path = tile.Project.Path;
		List<string> order = [.. Shown().Select(p => p.Path)];

		if (range && anchor is not null && order.Contains(anchor))
		{
			int from = order.IndexOf(anchor), to = order.IndexOf(path);
			if (!add) selected.Clear();
			for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++) selected.Add(order[i]);
		}
		else if (add)
		{
			if (!selected.Remove(path)) selected.Add(path);
			anchor = path;
		}
		else
		{
			selected.Clear();
			selected.Add(path);
			anchor = path;
		}

		foreach ((string key, UIProjectItem t) in tiles) t.Selected = selected.Contains(key);
	}

	List<RecentProject> SelectedProjects() => [.. Shown().Where(p => selected.Contains(p.Path))];

	void Open(RecentProject project)
	{
		if (project.Exists) _ = ProjectManager.Singleton.OpenProjectAsync(project.Path, this);
		else Locate(project);
	}

	// a click on the background lets go of the selection
	public override void _GuiInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) return;

		InputManager.Singleton.Keyboard.Capture(this);
		selected.Clear();
		foreach (UIProjectItem t in tiles.Values) t.Selected = false;
	}

	// ---- the menus ----

	public void ShowTileMenu(UIProjectItem tile, Control button)
	{
		if (!tile.Selected) Select(tile, add: false, range: false);

		List<RecentProject> targets = SelectedProjects();
		if (targets.Count == 0) return;

		RecentProject first = targets[0];
		bool single = targets.Count == 1;
		bool open = ProjectManager.Singleton.OpenProjects.Any(w => w.Session.FilePath is { } p && string.Equals(Path.GetFullPath(p), first.Path, StringComparison.OrdinalIgnoreCase));

		ContextMenu menu = GD.Load<ContextMenu>("res://Menus/ProjectTile.tres").Clone();

		Wire(menu, "project.open", targets.All(p => p.Exists), () => { foreach (RecentProject p in targets) Open(p); });
		Wire(menu, "project.rename", single && first.Exists && !open, tile.BeginRename);
		Wire(menu, "project.reveal", single && first.Exists, () => OS.ShellShowInFileManager(first.Path));
		Wire(menu, "project.locate", single && !first.Exists, () => Locate(first));
		Wire(menu, "project.remove", true, () => { foreach (RecentProject p in targets) RecentProjects.Remove(p.Path); });

		Wire(menu, "project.delete", targets.All(p => p.Exists) && !targets.Any(IsOpen), () => _ = DeleteAsync(targets));

		if (menu.Find<ContextButton>("project.locate") is ContextButton locate) locate.Visible = single && !first.Exists;

		if (button is not null) ContextMenus.ShowContextMenuBelow(menu, button);
		else ContextMenus.ShowContextMenu(menu, this);
	}

	static void Wire(ContextMenu menu, string id, bool enabled, Action action)
	{
		if (menu.Find<ContextButton>(id) is not ContextButton button) return;
		button.Enabled = enabled;
		button.Pressed += () => action();
	}

	void ShowSortMenu()
	{
		ContextMenu menu = GD.Load<ContextMenu>("res://Menus/HomeSort.tres").Clone();

		if (menu.Find<ContextRadioList>("home.sort") is ContextRadioList sorts)
		{
			sorts.SelectedButton = (int)AppSettings.Current.HomeSort;
			sorts.Selected += button =>
			{
				AppSettings.Current.HomeSort = sorts.Buttons.IndexOf(button) switch { 1 => HomeSort.Name, 2 => HomeSort.Created, _ => HomeSort.LastOpened };
				AppSettings.Current.Save();
				UpdateSortLabel();
				Rebuild();
			};
		}

		ContextMenus.ShowContextMenuBelow(menu, sortButton);
	}

	void UpdateSortLabel()
	{
		if (sortButton is not null) sortButton.Text = AppSettings.Current.HomeSort switch
		{
			HomeSort.Name => "Sort: Name",
			HomeSort.Created => "Sort: Date Created",
			_ => "Sort: Last Opened",
		};
	}

	// ---- keys ----

	void OnShortcut(ShortcutEventArgs e)
	{
		switch (e.Action)
		{
			case Shortcuts.SelectAll:
				selected.UnionWith(tiles.Keys);
				foreach (UIProjectItem t in tiles.Values) t.Selected = true;
				e.Handled = true;
				break;

			case Shortcuts.Delete or Shortcuts.RippleDelete:
				foreach (RecentProject p in SelectedProjects()) RecentProjects.Remove(p.Path);
				e.Handled = true;
				break;

			case Shortcuts.MediaRename:
				if (SelectedProjects() is [RecentProject only] && tiles.TryGetValue(only.Path, out UIProjectItem tile)) tile.BeginRename();
				e.Handled = true;
				break;
		}
	}

	// ---- changes on disk ----

	// renames the project file, and its folder when it has the project's name
	public void Rename(UIProjectItem tile, string name)
	{
		RecentProject project = tile.Project;

		if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
		{
			GD.PushWarning($"'{name}' can't be a file name.");
			tile.Refresh();
			return;
		}

		try
		{
			string folder = project.Folder;
			string renamedFolder = folder;

			if (string.Equals(Path.GetFileName(folder), project.Name, StringComparison.OrdinalIgnoreCase))
			{
				renamedFolder = Path.Combine(Path.GetDirectoryName(folder)!, name);
				if (Directory.Exists(renamedFolder)) throw new IOException($"A folder named '{name}' already exists there.");
				Directory.Move(folder, renamedFolder);
			}

			string oldFile = Path.Combine(renamedFolder, Path.GetFileName(project.Path));
			string newFile = Path.Combine(renamedFolder, name + ProjectFile.Extension);
			File.Move(oldFile, newFile);

			RecentProjects.Replace(project.Path, newFile);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			GD.PushWarning($"Could not rename '{project.Name}': {e.Message}");
			tile.Refresh();
		}
	}

	bool IsOpen(RecentProject project) => ProjectManager.Singleton.OpenProjects.Any(w => w.Session.FilePath is { } p && string.Equals(Path.GetFullPath(p), project.Path, StringComparison.OrdinalIgnoreCase));

	// moves the projects to the trash after asking; other files in their folders stay
	async System.Threading.Tasks.Task DeleteAsync(List<RecentProject> targets)
	{
		string what = targets.Count == 1 ? targets[0].Name : $"{targets.Count} projects";
		DialogResult answer = await Dialogs.Show(Dialogs.Question(
			"Delete project",
			$"Delete {what}?",
			"The project files go to the trash. Media files the projects use are left where they are.",
			("delete", "Delete", DialogButtonRole.Destructive),
			("cancel", "Cancel", DialogButtonRole.Cancel)), this);

		if (!answer.Is("delete")) return;

		foreach (RecentProject project in targets)
		{
			string folder = project.Folder;
			bool ownFolder = string.Equals(Path.GetFileName(folder), project.Name, StringComparison.OrdinalIgnoreCase);

			Error error = ownFolder
				? OS.MoveToTrash(folder)
				: new[] { project.Path, Path.Combine(folder, "Autosave"), Path.Combine(folder, "Thumbnails") }.Where(p => File.Exists(p) || Directory.Exists(p)).Select(OS.MoveToTrash).FirstOrDefault(e => e != Error.Ok);

			if (error == Error.Ok) RecentProjects.Remove(project.Path);
			else GD.PushWarning($"Could not delete '{project.Name}': {error}");
		}
	}

	// a missing project, found again where it went
	void Locate(RecentProject project)
	{
		DisplayServer.FileDialogShow($"Locate {project.Name}", Path.GetDirectoryName(project.Folder) ?? "", "", false,
			DisplayServer.FileDialogMode.OpenFile, [$"*{ProjectFile.Extension};EditSharp projects"],
			Callable.From((bool ok, string[] paths, long _) =>
			{
				if (ok && paths.Length > 0) RecentProjects.Replace(project.Path, paths[0]);
			}));
	}

	void OnFilesDropped(string[] files)
	{
		foreach (string file in files.Where(f => f.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase)))
			_ = ProjectManager.Singleton.OpenProjectAsync(file, this);
	}

	// ---- adding ----

	void ShowAddProject() => _ = NewProjectDialog.ShowAsync(this);
}
