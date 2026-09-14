using EditSharp.Components;
using EditSharp.Components.Channels;
using EditSharp.Components.Clips;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class UITimeline : Control
{
	[ExportGroup("Options")]

	[Export] Slider widthSlider;
	[Export] Slider heightSlider;

	[ExportGroup("Channels")]

	[Export] ScrollContainer editsContainer;
	[Export] VBoxContainer edits;
	[Export] Control editsScrollSpacer;
	[Export] ScrollContainer clipsViewContainer;
	[Export] UIClipsView clipsView;
	[Export] ScrollContainer rulerContainer;
	[Export] UIRuler ruler;

	[ExportGroup("Packed Scenes")]

	[Export] PackedScene channelEditScene;
	[Export] PackedScene channelTimelineScene;

	public Timeline Timeline;

	public List<UIChannel> UIVideoChannels = [];
	// takes in global position
	// returns the video channel if any at position
	public UIChannel UIVideoChannelAtPoint(Vector2 p)
	{
		foreach (UIChannel u in UIVideoChannels)
		{
			if (u.ClipsView.GetGlobalRect().HasPoint(p))
			{
				return u;
			}
		}

		return null;
	}

	public List<UIChannel> UIAudioChannels = [];
	// takes in global position
	// returns the video channel if any at position
	public UIChannel UIAudioChannelAtPoint(Vector2 p)
	{
		foreach (UIChannel u in UIAudioChannels)
		{
			if (u.ClipsView.GetGlobalRect().HasPoint(p))
			{
				return u;
			}
		}

		return null;
	}

	public List<UIChannel> UIChannels => [.. UIVideoChannels, .. UIAudioChannels];
	public List<UIClip> UIClips => [.. UIVideoChannels.SelectMany(c => c.ClipsView.UIClips), .. UIAudioChannels.SelectMany(c => c.ClipsView.UIClips)];

	public double VerticalScale { 
		get; 
		set => field = value > 0d 
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Vertical scale must be greater than zero");
	} = 90d;

	public double PixelsPerSecond { 
		get; 
		set
		{
			if (value > 0d)
			{
				// update clips view
				clipsView.Refresh();

				// update ruler
				ruler.Update(value, ProjectManager.Singleton.CurrentProject.RenderSettings.Framerate);

				field = value;
			}
			else throw new ArgumentOutOfRangeException(nameof(value), "Pixels per second must be greater than zero");
		}
	} = 100d;

	public double TimeSpanToPixels(TimeSpan t) => t.TotalSeconds * PixelsPerSecond;
	public TimeSpan PixelsToTimeSpan(double p) => TimeSpan.FromSeconds(p / PixelsPerSecond);


	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// TEMPORARILY GET TIMELINE AUTOMATICALLY INSTEAD OF MANUAL ASSIGNMENT
		Timeline = ProjectManager.Singleton.CurrentProject.Timeline;

		// add event listeners
		heightSlider.ValueChanged += SetChannelHeight;
		widthSlider.ValueChanged += v => PixelsPerSecond = v;

		// add test channels
		foreach (var channel in ProjectManager.Singleton.CurrentProject.Timeline.Channels)
		{
			AddChannel(channel);
		}

		SetChannelHeight(heightSlider.Value);
		SetChannelWidth(widthSlider.Value);
	}

    public override void _Process(double delta)
    {
		// match scrolls to timeline
        editsContainer.ScrollVertical = clipsViewContainer.ScrollVertical;
		rulerContainer.ScrollHorizontal = clipsViewContainer.ScrollHorizontal;

		//stretch ruler to length of channels
		ruler.CustomMinimumSize = new(
			clipsView.Size.X + clipsViewContainer.GetVScrollBar().Size.X,
			ruler.CustomMinimumSize.Y
		);

		// show scrollbar spacer if timeline is scrollable
		editsScrollSpacer.Visible = clipsViewContainer.GetHScrollBar().Visible;
    }

	public class UIChannel
	{
		public required Channel Channel;

		public required UIChannelEdit Edit;

		public required UIChannelClipsView ClipsView;
	}

	public void AddChannel(Channel c)
	{
		UIChannel channel = CreateUIChannel(c);
		channel.ClipsView.UITimeline = this;
		UIChannels.Add(channel);

		if (c is VideoChannel v)
		{
			// add edit gui to tree
			edits.AddChild(channel.Edit);
			edits.MoveChild(channel.Edit, 0);

			// add clips view gui to tree
			clipsViews.AddChild(channel.ClipsView);
			clipsViews.MoveChild(channel.ClipsView, 0);

			UIVideoChannels.Add(channel);
		}
		else if (c is AudioChannel a)
		{
			// add edit gui to tree
			edits.AddChild(channel.Edit);
			edits.MoveChild(channel.Edit, edits.GetChildCount() - 1);

			// add clips view gui to tree
			clipsViews.AddChild(channel.ClipsView);
			clipsViews.MoveChild(channel.ClipsView, edits.GetChildCount() - 1);

			UIAudioChannels.Add(channel);
		}
	}

	UIChannel CreateUIChannel(Channel c)
	{
		UIChannelClipsView timeline = channelTimelineScene.Instantiate() as UIChannelClipsView;
		timeline.Channel = c;
		timeline.UITimeline = this;

		UIChannelEdit edit = channelEditScene.Instantiate() as UIChannelEdit;
		edit.channel = c;

		return new()
		{
			Channel = c,
			ClipsView = timeline,
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
		foreach (var child in clipsViews.GetChildren())
		{
			if (child is UIChannelClipsView timeline)
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
		PixelsPerSecond = w;

		
	}

	void UpdateSelection()
	{
		// highlight current selection, unhighlight any other clips
		foreach (UIClip c in UIClips)
		{
			c.Selected = CurrentSelection.Clips.Contains(c);
			c.SetOutlined(c.Selected);
		}
	}
}
