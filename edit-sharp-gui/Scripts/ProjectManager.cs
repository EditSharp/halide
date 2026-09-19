using EditSharp;
using EditSharp.Components;
using EditSharp.History;
using EditSharp.Rendering;
using EditSharpGUI.Scripts.UI.Theming;
using Godot;
using System;

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
		if (ThemeDB.GetProjectTheme() is EditSharpTheme theme) theme.LoadUserSettings();
	}

	public Project CurrentProject = new()
	{
		Timeline = Tests.TestBlueprint.Timeline,
		RenderSettings = Tests.TestBlueprint.RenderSettings
	};
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
}