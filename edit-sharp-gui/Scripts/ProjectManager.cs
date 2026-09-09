using EditSharp;
using EditSharp.Components;
using EditSharp.Composite;
using EditSharp.Playback;
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
	}

	public Project currentProject = new()
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
	public Timeline Timeline = new();

	public RenderSettings RenderSettings = new();
}