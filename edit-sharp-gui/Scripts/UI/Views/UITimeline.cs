using EditSharp.Components;
using Godot;
using System;
using System.Collections.Generic;

public partial class UITimeline : Control
{
	[ExportGroup("Controls")]

	[Export] ScrollContainer editsContainer;
	[Export] VBoxContainer edits;
	[Export] ScrollContainer timelinesContainer;
	[Export] VBoxContainer timelines;
	[Export] ScrollContainer rulerContainer;
	[Export] Control ruler;

	[ExportGroup("Packed Scenes")]

	[Export] PackedScene channelEdit;
	[Export] PackedScene channelTimeline;

	List<UIChannel> channels = [];

	float verticalScale = 90f;
	float horizontalScale = 1f;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// add test channels
		for (int i = 0; i < 10; i++)
		{
			VideoChannel channel = new()
			{
				Name = $"Channel {i}"
			};
			AddChannel(channel);
		}
	}

    public override void _Process(double delta)
    {
		// match scrolls to timeline
        editsContainer.ScrollVertical = timelinesContainer.ScrollVertical;
		rulerContainer.ScrollHorizontal = timelinesContainer.ScrollHorizontal;
    }

	UIChannel CreateUIChannel(Channel c)
	{
		UIChannelTimeline timeline = channelTimeline.Instantiate() as UIChannelTimeline;
		timeline.timeline = this;

		UIChannelEdit edit = channelEdit.Instantiate() as UIChannelEdit;
		edit.channel = c;

		return new()
		{
			Channel = c,
			ChannelTimeline = timeline,
			ChannelEdit = edit
		};
	}

	public void AddChannel(Channel c)
	{
		UIChannel channel = CreateUIChannel(c);

		edits.AddChild(channel.ChannelEdit);
		timelines.AddChild(channel.ChannelTimeline);
		channels.Add(channel);
	}

	

	public class UIChannel
	{
		public required Channel Channel;

		public required UIChannelEdit ChannelEdit;

		public required UIChannelTimeline ChannelTimeline;
	}
}
