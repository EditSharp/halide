using Godot;
using System;

public partial class Editor : Control
{
	[ExportGroup("Views")]

	[Export] UITimeline UITimeline;
	[Export] UIPlayback UIPlayback;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		UITimeline.SetTimeline(ProjectManager.Singleton.CurrentProject.Timeline);
		UIPlayback.SetPlayback(new()
		{
			Timeline = ProjectManager.Singleton.CurrentProject.Timeline,
			RenderSettings = ProjectManager.Singleton.CurrentProject.RenderSettings with { Resolution = new(1280, 720), Framerate = 60 }
		});
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
