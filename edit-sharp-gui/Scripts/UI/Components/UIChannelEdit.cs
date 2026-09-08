using EditSharp.Components;
using Godot;
using System;

public partial class UIChannelEdit : PanelContainer
{
	[ExportGroup("Controls")]

	[Export] LineEdit channelName;

	public Channel channel;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		channelName.Text = channel.Name;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
