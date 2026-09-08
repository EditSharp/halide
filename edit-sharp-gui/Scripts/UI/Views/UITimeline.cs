using EditSharp.Components;
using Godot;
using System;
using System.Collections.Generic;

public partial class UITimeline : Control
{
	[ExportGroup("Controls")]

	[Export] ScrollContainer editsContainer;
	[Export] VBoxContainer edits;
	[Export] Control editsScrollSpacer;
	[Export] ScrollContainer timelinesContainer;
	[Export] VBoxContainer timelines;
	[Export] ScrollContainer rulerContainer;
	[Export] Control ruler;

	[ExportGroup("Packed Scenes")]

	[Export] PackedScene channelEditScene;
	[Export] PackedScene channelTimelineScene;

	List<UIChannel> channels = [];

	float verticalScale = 90f;
	float horizontalScale = 1f;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// add test channels
		foreach (var channel in Tests.TestBlueprint.Timeline.Channels)
		{
			AddChannel(channel);
		}
	}

    public override void _Process(double delta)
    {
		// match scrolls to timeline
        editsContainer.ScrollVertical = timelinesContainer.ScrollVertical;
		rulerContainer.ScrollHorizontal = timelinesContainer.ScrollHorizontal;

		//show scrollbar spacer if timeline is scrollable
		editsScrollSpacer.Visible = timelinesContainer.GetHScrollBar().Visible;
    }

	public void AddChannel(Channel c)
	{
		UIChannel channel = CreateUIChannel(c);

		edits.AddChild(channel.ChannelEdit);
		timelines.AddChild(channel.ChannelTimeline);
		channels.Add(channel);
	}

	UIChannel CreateUIChannel(Channel c)
	{
		UIChannelTimeline timeline = channelTimelineScene.Instantiate() as UIChannelTimeline;
		timeline.channel = c;
		timeline.timeline = this;

		UIChannelEdit edit = channelEditScene.Instantiate() as UIChannelEdit;
		edit.channel = c;

		return new()
		{
			Channel = c,
			ChannelTimeline = timeline,
			ChannelEdit = edit
		};
	}

	public class UIChannel
	{
		public required Channel Channel;

		public required UIChannelEdit ChannelEdit;

		public required UIChannelTimeline ChannelTimeline;
	}
}
