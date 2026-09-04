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
	}

	public Project currentProject = new();
}

public class Project
{
	public Timeline Timeline = new();

	public RenderSettings RenderSettings = new();
}
