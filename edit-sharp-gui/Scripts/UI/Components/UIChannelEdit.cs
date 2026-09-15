using EditSharp.Components;
using EditSharp.Components.Channels;
using Godot;
using System;

public partial class UIChannelEdit : PanelContainer
{
	[ExportGroup("Controls")]

	[Export] LineEdit channelName;

	// reference to actual channel data under the hood
	public Channel Channel;
	public UITimeline UITimeline;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		channelName.Text = Channel.Name;

		Refresh();
	}

	// reset chanel edit ui to what is actually stored in data
	public void Refresh()
	{
		CustomMinimumSize = new(
			CustomMinimumSize.X,
			(float)UITimeline.VerticalScale
		);
	}
}
