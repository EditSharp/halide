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
	double pixelsPerSecond = 100d;
	public float HorizontalScale => (float)pixelsPerSecond;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// add event listeners
		heightSlider.ValueChanged += (h) => SetChannelHeight(h);
		widthSlider.ValueChanged += (w) => SetChannelWidth(w);

		// add test channels
		foreach (var channel in ProjectManager.Singleton.currentProject.Timeline.Channels)
		{
			AddChannel(channel);
		}

		SetChannelHeight(heightSlider.Value);
		SetChannelWidth(widthSlider.Value);
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

		public required UIChannelEdit Edit;

		public required UIChannelTimeline Timeline;
	}

	public void AddChannel(Channel c)
	{
		UIChannel channel = CreateUIChannel(c);

		edits.AddChild(channel.Edit);
		timelines.AddChild(channel.Timeline);
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
			Timeline = timeline,
			Edit = edit
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

	public void SetChannelWidth(double w)
	{
		pixelsPerSecond = w;

		// update channel timelines
		foreach (var channel in channels) channel.Timeline.SetWidth(pixelsPerSecond);


		// update ruler
		ruler.Update(pixelsPerSecond, ProjectManager.Singleton.currentProject.RenderSettings.Framerate);
	}
}
