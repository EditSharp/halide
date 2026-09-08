using Godot;
using System;
using EditSharp;
using EditSharp.Components.Clips;

public partial class UIClip : Control
{
	[ExportGroup("Controls")]

	[Export] Control content;
	[Export] Control gap;

	[ExportGroup("Details")]

	[Export] Label clipName;
	[Export] Panel thumbnail;

	public required Clip Clip;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	public void SetLength(float length, float gap = 0)
	{
		
	}
}
