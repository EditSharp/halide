using EditSharp;
using EditSharp.Components;
using EditSharp.History;
using EditSharp.Rendering;
using EditSharpGUI.Scripts.UI.Theming;
using Godot;
using EditSharp.Components.Channels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EditSharpGUI.Scripts.UI.Dialogs;
using EditSharp.Components.Media;

public partial class ProjectManager : Node
{
	public static ProjectManager Singleton;

	public override void _Ready()
	{
		Singleton ??= this;

		if (Singleton != this) return;

		EditSharpConfig.Logger = new ConsoleLogger();

		// a test scene's own window goes where the tests' windows go
		Screens.Move(GetTree().Root);

		// the project theme is ours; the user's preset and accent go on top of
		// whatever the file says, and the whole tree restyles from it
		if (ThemeDB.GetProjectTheme() is EditSharpTheme theme)
		{
			theme.LoadUserSettings();
			themeFile = ProjectSettings.GlobalizePath("res://main_theme.tres");
			themeStamp = Godot.FileAccess.GetModifiedTime(themeFile);
		}
	}

	// ---- the theme file, watched while the app runs ----

	// a save from the editor's Theme tab restyles the running app: the file
	// is checked once a second and reloaded in place when it changed. debug
	// builds only - a release has no editor beside it
	string themeFile;
	ulong themeStamp;
	double themePoll;

	public override void _Process(double delta)
	{
		if (Singleton != this) return;

		Autosave(delta);

		if (themeFile is null || !OS.IsDebugBuild()) return;

		themePoll += delta;
		if (themePoll < 1d) return;
		themePoll = 0d;

		ulong stamp = Godot.FileAccess.GetModifiedTime(themeFile);
		if (stamp == themeStamp) return;
		themeStamp = stamp;

		if (ThemeDB.GetProjectTheme() is EditSharpTheme theme && theme.ReloadFrom("res://main_theme.tres"))
			GD.Print("Theme reloaded from disk.");
	}

	// ---- windows ----

	const string EditorScenePath = "res://Scenes/Views/Editor.tscn";
	const string HomeScenePath = "res://Scenes/Views/Home.tscn";

	readonly List<ProjectWindow> projects = [];
	HomeWindow home;

	public IReadOnlyList<ProjectWindow> OpenProjects => projects;

	public event Action OpenProjectsChanged;

	// what the app shows first, as the settings say
	public void Start()
	{
		// project files named on the command line - a double-click in the file manager - open instead
		string[] named = [.. OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).Where(a => a.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase) && File.Exists(a)).Distinct()];
		if (named.Length > 0)
		{
			foreach (string path in named) _ = OpenProjectAsync(path, null);
			return;
		}

		AppSettings settings = AppSettings.Current;
		string last = RecentProjects.All.FirstOrDefault(p => p.Exists)?.Path;

		switch (settings.OnStartup)
		{
			case StartupAction.ReopenLastSession when settings.LastSession.Any(File.Exists):
				foreach (string path in settings.LastSession.Where(File.Exists)) _ = OpenProjectAsync(path, null);
				break;

			case StartupAction.HomeAndLastProject when last is not null:
				ShowHome();
				_ = OpenProjectAsync(last, home);
				break;

			default:
				ShowHome();
				break;
		}
	}

	public void ShowHome()
	{
		if (home is null || !IsInstanceValid(home))
		{
			home = HomeWindow.Create(GD.Load<PackedScene>(HomeScenePath));
			GetTree().Root.AddChild(home);
		}

		home.Show();
		home.GrabFocus();
	}

	// opens a project the way the user asked for it: offering its autosave when
	// that's newer than the file, and afterwards to find any media gone missing
	public async Task<ProjectWindow> OpenProjectAsync(string path, Node asker)
	{
		string autosave = NewerAutosave(path);
		string from = null;

		if (autosave is not null && !IsOpen(path))
		{
			DialogResult answer = await Dialogs.Show(Dialogs.Question(
				"Autosave found",
				$"{Path.GetFileNameWithoutExtension(path)} has an autosave newer than the last save.",
				$"The autosave is from {File.GetLastWriteTime(autosave):g}; the project was last saved {File.GetLastWriteTime(path):g}.",
				("restore", "Restore Autosave", DialogButtonRole.Default),
				("saved", "Open Last Save", DialogButtonRole.Cancel)), asker);

			if (answer.Is("restore")) from = autosave;
		}

		ProjectWindow window = OpenProject(path, from);
		if (window is not null) await OfferRelinkAsync(window.Session);
		return window;
	}

	bool IsOpen(string path) => projects.Any(w => w.Session.FilePath is { } open && string.Equals(Path.GetFullPath(open), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));

	// the newest file in the project's Autosave folder, when it's newer than the project file
	static string NewerAutosave(string path)
	{
		string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "Autosave");
		if (!Directory.Exists(folder) || !File.Exists(path)) return null;

		string newest = Directory.GetFiles(folder, $"*{ProjectFile.Extension}").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
		return newest is not null && File.GetLastWriteTimeUtc(newest) > File.GetLastWriteTimeUtc(path) ? newest : null;
	}

	// the media a project couldn't find, listed so each can be found again:
	// one at a time, or every one of them under a folder
	public async Task OfferRelinkAsync(ProjectSession session)
	{
		List<IMedia> missing = [.. session.Project.Media.Where(m => !string.IsNullOrEmpty(m.Path) && !File.Exists(m.Path))];
		if (missing.Count == 0) return;

		Dialog dialog = new() { Title = "Missing media" };
		dialog.Elements.Add(new DialogText { Id = "message", Text = $"{missing.Count} media file{(missing.Count == 1 ? " is" : "s are")} missing.", Style = DialogText.TextStyle.Heading });
		dialog.Elements.Add(new DialogText { Id = "detail", Text = "Their clips show as offline until they're found. Point to each file, or search a folder for all of them." });

		DialogList list = new() { Id = "missing" };
		list.Buttons.Add(new DialogButton { Id = "search", Text = "Search Folder..." });
		dialog.Elements.Add(list);
		dialog.Buttons.Add(new DialogButton { Id = "done", Text = "Done", Role = DialogButtonRole.Default });
		dialog.Buttons.Add(new DialogButton { Id = "later", Text = "Later", Role = DialogButtonRole.Cancel });

		void Fill()
		{
			list.Rows.Clear();
			foreach (IMedia media in missing)
			{
				bool found = File.Exists(media.Path);
				list.Rows.Add(new DialogListRow
				{
					Id = media.Id.ToString(),
					Text = Path.GetFileName(media.Path),
					Detail = found ? $"Found: {media.Path}" : media.Path,
					ButtonText = found ? "Found" : "Locate...",
					ButtonEnabled = !found,
				});
			}
			list.Refresh();
		}

		void Relink(IMedia media, string path)
		{
			using (EditSharp.History.Transaction.Scope change = session.Project.History.Begin("Relink media"))
			{
				media.Path = path;
				change.Commit();
			}
		}

		list.Pressed += (row, button) =>
		{
			if (row is not null && missing.FirstOrDefault(m => m.Id.ToString() == row.Id) is { } media)
			{
				DisplayServer.FileDialogShow($"Locate {Path.GetFileName(media.Path)}", Path.GetDirectoryName(session.FilePath) ?? "", Path.GetFileName(media.Path), false,
					DisplayServer.FileDialogMode.OpenFile, [], Callable.From((bool ok, string[] paths, long _) =>
					{
						if (ok && paths.Length > 0) { Relink(media, paths[0]); Fill(); }
					}));
			}
			else if (button?.Id == "search")
			{
				DisplayServer.FileDialogShow("Search a folder for the missing media", Path.GetDirectoryName(session.FilePath) ?? "", "", false,
					DisplayServer.FileDialogMode.OpenDir, [], Callable.From((bool ok, string[] paths, long _) =>
					{
						if (!ok || paths.Length == 0) return;
						RelinkFromFolder(paths[0]);
					}));
			}
		};

		void RelinkFromFolder(string folder)
		{
			Dictionary<string, string> byName = new(StringComparer.OrdinalIgnoreCase);
			foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) byName.TryAdd(Path.GetFileName(file), file);

			foreach (IMedia media in missing.Where(m => !File.Exists(m.Path)))
				if (byName.TryGetValue(Path.GetFileName(media.Path), out string found)) Relink(media, found);
			Fill();
		}

		Fill();
		await Dialogs.Show(dialog, session);
	}

	// opens a project in a window of its own; one already open comes to the front.
	// `from` loads another file in its place - an autosave - leaving it unsaved
	public ProjectWindow OpenProject(string path, string from = null)
	{
		path = Path.GetFullPath(path);

		if (projects.FirstOrDefault(w => w.Session.FilePath is { } open && string.Equals(Path.GetFullPath(open), path, StringComparison.OrdinalIgnoreCase)) is { } already)
		{
			already.GrabFocus();
			return already;
		}

		LoadedProject loaded;
		try
		{
			loaded = from is null ? ProjectFile.Load(path) : ProjectFile.Load(from, Path.GetDirectoryName(path));
		}
		catch (Exception e) when (e is IOException or JsonException or NotSupportedException or UnauthorizedAccessException)
		{
			GD.PushError($"Could not open '{path}': {e.Message}");
			return null;
		}

		foreach (string warning in loaded.Warnings) GD.PushWarning($"{Path.GetFileName(path)}: {warning}");

		ProjectWindow window = ProjectWindow.Create(loaded.Project, path, GD.Load<PackedScene>(EditorScenePath));
		window.Session.Gui = loaded.Gui;
		if (from is not null) window.Session.MarkUnsaved();
		Add(window);
		RecentProjects.Touch(path);
		return window;
	}

	// a new project in <folder>/<name>/<name>.esproj: an empty main timeline
	// with three video and three audio channels, saved straight away
	public ProjectWindow CreateProject(string parentFolder, string name, RenderSettings renderSettings)
	{
		string folder = Path.Combine(parentFolder, name);
		string path = Path.Combine(folder, name + ProjectFile.Extension);

		Project project = new();
		using (Transaction.Suppress())
		{
			project.RenderSettings = renderSettings;
			Timeline timeline = new();
			for (int i = 0; i < 3; i++) timeline.AddChannel(new VideoChannel());
			for (int i = 0; i < 3; i++) timeline.AddChannel(new AudioChannel());
			project.Timeline = timeline;
			project.AddTimeline(timeline);
		}

		ProjectFile.Save(project, path);
		CaptureThumbnails(project, path, null);

		ProjectWindow window = ProjectWindow.Create(project, path, GD.Load<PackedScene>(EditorScenePath));
		Add(window);
		RecentProjects.Touch(path);
		return window;
	}

	void Add(ProjectWindow window)
	{
		projects.Add(window);
		GetTree().Root.AddChild(window);
		window.Show();
		OpenProjectsChanged?.Invoke();

		// Home has done its job once a project is open
		if (home is not null && IsInstanceValid(home))
		{
			home.QueueFree();
			home = null;
		}
	}

	// a window went: the app quits with the last one, unless the settings keep it in the tray
	public void Closed(Window window)
	{
		if (window is ProjectWindow project) projects.Remove(project);
		if (window == home) home = null;
		OpenProjectsChanged?.Invoke();

		if (projects.Count == 0 && home is null && AppSettings.Current.OnLastWindowClosed == LastWindowAction.Quit)
			Callable.From(() => GetTree().Quit()).CallDeferred();
	}

	// App Settings: comes with its own window later in this step
	public void ShowSettings() => GD.Print("App Settings: not built yet.");

	// ---- saving ----

	// whether a project may close: straight away when everything is saved,
	// otherwise once the user has saved, discarded or kept it open
	public async Task<bool> ConfirmCloseAsync(ProjectSession session)
	{
		if (!session.Dirty) return true;

		string name = Path.GetFileNameWithoutExtension(session.FilePath ?? "Untitled");
		DialogResult answer = await Dialogs.Show(Dialogs.Question(
			"Unsaved changes",
			$"Save the changes to {name}?",
			"Your changes will be lost if you don't save them.",
			("save", "Save", DialogButtonRole.Default),
			("discard", "Don't Save", DialogButtonRole.Destructive),
			("cancel", "Cancel", DialogButtonRole.Cancel)), session);

		return answer.Button switch
		{
			"save" => Save(session),
			"discard" => true,
			_ => false,
		};
	}

	public bool Save(ProjectSession session)
	{
		if (session.FilePath is null) return false;

		System.Text.Json.Nodes.JsonObject gui = session.CaptureGui();

		try
		{
			ProjectFile.Save(session.Project, session.FilePath, gui);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			GD.PushError($"Could not save '{session.FilePath}': {e.Message}");
			return false;
		}

		session.MarkSaved();
		RecentProjects.Touch(session.FilePath);
		CaptureThumbnails(session.Project, session.FilePath, gui);
		return true;
	}

	// the project written to a file the user picks; the window follows it there
	public void SaveAs(ProjectSession session)
	{
		string start = session.FilePath is { } current ? Path.GetDirectoryName(current)! : AppSettings.Current.ProjectsFolder;

		DisplayServer.FileDialogShow("Save project as", start, Path.GetFileName(session.FilePath ?? "Untitled" + ProjectFile.Extension), false,
			DisplayServer.FileDialogMode.SaveFile, [$"*{ProjectFile.Extension};EditSharp projects"],
			Callable.From((bool ok, string[] paths, long _) =>
			{
				if (!ok || paths.Length == 0) return;

				string path = paths[0].EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase) ? paths[0] : paths[0] + ProjectFile.Extension;
				string previous = session.FilePath;
				session.FilePath = path;

				if (!Save(session)) session.FilePath = previous;
				else if (session.GetParent() is ProjectWindow window) window.UpdateTitle();
			}));
	}

	// the Home tile's pictures, rendered in the background after a save: the
	// preview at the playhead and frames across the timeline that was open
	static void CaptureThumbnails(Project project, string path, System.Text.Json.Nodes.JsonObject gui)
	{
		Time playhead = new(gui?["playhead"]?.GetValue<long>() ?? 0);
		Guid timelineId = Guid.TryParse(gui?["timelineId"]?.GetValue<string>(), out Guid id) ? id : Guid.Empty;
		Timeline timeline = project.Timelines.FirstOrDefault(t => t.Id == timelineId) ?? project.Timeline;

		_ = ProjectThumbnails.CaptureAsync(project, timeline, playhead, Path.GetDirectoryName(path));
	}

	// every project closed, each asking about unsaved changes; the app goes when they all have
	public async void QuitAll()
	{
		AppSettings.Current.LastSession = [.. projects.Select(p => p.Session.FilePath).Where(p => p is not null)];
		AppSettings.Current.Save();

		foreach (ProjectWindow window in projects.ToList())
			if (!await window.CloseAsync()) return;

		GetTree().Quit();
	}

	// ---- autosave ----

	double autosaveClock;

	void Autosave(double delta)
	{
		autosaveClock += delta;
		if (autosaveClock < 5d) return;
		autosaveClock = 0d;

		foreach (ProjectWindow window in projects)
		{
			ProjectSession session = window.Session;
			if (session.FilePath is null || !session.NeedsAutosave(AppSettings.Current.AutosaveSeconds)) continue;

			string folder = Path.Combine(Path.GetDirectoryName(session.FilePath)!, "Autosave");
			string name = Path.GetFileNameWithoutExtension(session.FilePath);
			string backup = Path.Combine(folder, $"{name} {DateTime.Now:yyyy-MM-dd HH-mm-ss}{ProjectFile.Extension}");

			try
			{
				ProjectFile.Save(session.Project, backup, session.CaptureGui(), folder: Path.GetDirectoryName(session.FilePath));
				session.MarkAutosaved();

				foreach (string old in Directory.GetFiles(folder, $"{name} *{ProjectFile.Extension}").OrderByDescending(f => f).Skip(AppSettings.Current.AutosaveBackups))
					File.Delete(old);
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException)
			{
				GD.PushWarning($"Autosave of '{session.FilePath}' failed: {e.Message}");
			}
		}
	}
}

public class ConsoleLogger : IEditSharpLogger
{
    public void Log(string message)
    {
        GD.Print(message, Colors.Yellow);
    }

    public void LogError(string message)
    {
        GD.PushError(message);
    }

    public void LogVerbose(string message)
    {
        //throw new NotImplementedException();
    }

    public void LogWarning(string message)
    {
        GD.PushWarning(message);
    }
}

public class Project
{
	// everything the user has done to this project, for undo and redo. the
	// timeline records its own edits into whichever history is active - see
	// EditSharp.History.Transaction - and Editor makes this one active
	public History History { get; } = new();

	Timeline _timeline = new();
	public Timeline Timeline { get => _timeline; set => Transaction.Set(this, ref _timeline, value, static (o, v) => o._timeline = v); }

	RenderSettings _renderSettings = new();
	public RenderSettings RenderSettings { get => _renderSettings; set => Transaction.Set(this, ref _renderSettings, value, static (o, v) => o._renderSettings = v); }

	// the media the project has brought in; nodes share them
	public MediaLibrary Media { get; } = new();

	// every timeline in the project, the edited one first. the list has
	// history like the media
	readonly List<Timeline> timelines = [];
	public IReadOnlyList<Timeline> Timelines => timelines;

	public event Action TimelinesChanged;

	public void AddTimeline(Timeline timeline)
	{
		if (timeline is null || timelines.Contains(timeline)) return;

		Transaction.Apply(
			() => { timelines.Add(timeline); TimelinesChanged?.Invoke(); },
			() => { timelines.Remove(timeline); TimelinesChanged?.Invoke(); },
			"add timeline");
	}

	public void RemoveTimeline(Timeline timeline)
	{
		int index = timelines.IndexOf(timeline);
		if (index < 0 || ReferenceEquals(timeline, Timeline)) return;

		Transaction.Apply(
			() => { timelines.Remove(timeline); TimelinesChanged?.Invoke(); },
			() => { timelines.Insert(Math.Min(index, timelines.Count), timeline); TimelinesChanged?.Invoke(); },
			"remove timeline");
	}

	// a project around a timeline built in code: its media are whatever the clips read
	public static Project FromBlueprint(Blueprint blueprint)
	{
		Project project = new() { Timeline = blueprint.Timeline, RenderSettings = blueprint.RenderSettings };
		project.Media.AdoptFrom(blueprint.Timeline);

		using (Transaction.Suppress()) project.AddTimeline(blueprint.Timeline);

		return project;
	}
}