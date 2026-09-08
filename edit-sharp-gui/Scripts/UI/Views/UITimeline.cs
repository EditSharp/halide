using EditSharp.Components;
using Godot;
using System;
using System.Collections.Generic;

public partial class UITimeline : Control
{
	[ExportGroup("Options")]

	[Export] Slider widthSlider;
	[Export] Slider heightSlider;

	[ExportGroup("Channels")]

	[Export] ScrollContainer editsContainer;
	[Export] VBoxContainer edits;
	[Export] Control editsScrollSpacer;
	[Export] ScrollContainer timelinesContainer;
	[Export] VBoxContainer timelines;
	[Export] ScrollContainer rulerContainer;
	[Export] UIRuler ruler;

	[ExportGroup("Packed Scenes")]

	[Export] PackedScene channelEditScene;
	[Export] PackedScene channelTimelineScene;

	List<UIChannel> channels = [];

	public float VerticalScale = 90f;
	float pixelsPerSecond = 100f;
	public float HorizontalScale => pixelsPerSecond;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// add event listeners
		heightSlider.ValueChanged += (h) => SetChannelHeight(h);

		// add test channels
		foreach (var channel in ProjectManager.Singleton.currentProject.Timeline.Channels)
		{
			AddChannel(channel);
		}

		ruler.Update(pixelsPerSecond, ProjectManager.Singleton.currentProject.RenderSettings.Framerate);
	}

    public override void _Process(double delta)
    {
		// match scrolls to timeline
        editsContainer.ScrollVertical = timelinesContainer.ScrollVertical;
		rulerContainer.ScrollHorizontal = timelinesContainer.ScrollHorizontal;

		//stretch ruler to length of channels
		ruler.CustomMinimumSize = new(
			timelines.Size.X + timelinesContainer.GetVScrollBar().Size.X,
			ruler.CustomMinimumSize.Y
		);

		// show scrollbar spacer if timeline is scrollable
		editsScrollSpacer.Visible = timelinesContainer.GetHScrollBar().Visible;
    }

	public class UIChannel
	{
		public required Channel Channel;

		public required UIChannelEdit ChannelEdit;

		public required UIChannelTimeline ChannelTimeline;
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

	// set channel height in pixels
	public void SetChannelHeight(double h)
	{
		// update channel edits
		foreach (var child in edits.GetChildren())
		{
			if (child is UIChannelEdit edit)
			{
				edit.CustomMinimumSize = new(
					edit.CustomMinimumSize.X,
					(float)h
				);
			}
		}

		// update channel timelines
		foreach (var child in timelines.GetChildren())
		{
			if (child is UIChannelTimeline timeline)
			{
				timeline.CustomMinimumSize = new(
					timeline.CustomMinimumSize.X,
					(float)h
				);
			}
		}
	}

	public void SetChannelWidth(double pixelsPerSecond)
	{
		// update channel timelines


		// update ruler
	}
}
