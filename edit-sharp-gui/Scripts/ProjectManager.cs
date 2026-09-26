using EditSharp;
using EditSharp.Components;
using EditSharp.History;
using EditSharp.Rendering;
using EditSharpGUI.Scripts.UI.Theming;
using Godot;
using System;
using System.Collections.Generic;

public partial class ProjectManager : Node
{
	public static ProjectManager Singleton;

	public override void _Ready()
	{
		Singleton ??= this;

		if (Singleton != this) return;

		EditSharpConfig.Logger = new ConsoleLogger();

		// the project theme is ours; the user's preset and accent go on top of
		// whatever the file says, and the whole tree restyles from it
		if (ThemeDB.GetProjectTheme() is EditSharpTheme theme)
		{
			theme.LoadUserSettings();
			themeFile = ProjectSettings.GlobalizePath("res://main_theme.tres");
			themeStamp = FileAccess.GetModifiedTime(themeFile);
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
		if (Singleton != this || themeFile is null || !OS.IsDebugBuild()) return;

		themePoll += delta;
		if (themePoll < 1d) return;
		themePoll = 0d;

		ulong stamp = FileAccess.GetModifiedTime(themeFile);
		if (stamp == themeStamp) return;
		themeStamp = stamp;

		if (ThemeDB.GetProjectTheme() is EditSharpTheme theme && theme.ReloadFrom("res://main_theme.tres"))
			GD.Print("Theme reloaded from disk.");
	}

	public Project CurrentProject = Project.FromBlueprint(Tests.TestBlueprint);
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