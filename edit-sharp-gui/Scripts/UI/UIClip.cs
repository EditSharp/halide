using Godot;
using System;
using EditSharp;
using EditSharp.Components.Clips;

public partial class UIClip : Control
{
	[Export] Label clipName;

	[Export] Panel thumbnail;

	Clip Clip;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
