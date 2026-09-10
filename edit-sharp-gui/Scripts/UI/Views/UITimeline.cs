using EditSharp.Components;
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
		channel.Timeline.Timeline = this;

		edits.AddChild(channel.Edit);
		timelines.AddChild(channel.Timeline);
		channels.Add(channel);
	}

	UIChannel CreateUIChannel(Channel c)
	{
		UIChannelTimeline timeline = channelTimelineScene.Instantiate() as UIChannelTimeline;
		timeline.Channel = c;
		timeline.Timeline = this;

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

	// all selected clips
	List<UIClip> selection = [];

	// when a clip gets clicked on
	public enum SelectionMode
	{
		// add this item to existing selection
		// if it is not already part of it
		Inclusive,
		// if this item is part of the current selection, do nothing
		// otherwise act exclusive
		ExclusiveIfUnselected,
		// make this item the only one in the selection
		Exclusive
	}

	public void SelectClip(UIClip uiClip, SelectionMode mode = SelectionMode.ExclusiveIfUnselected, bool invert = false)
	{
		if (mode == SelectionMode.ExclusiveIfUnselected)
		{
			if (!selection.Contains(uiClip)) SelectClip(uiClip, SelectionMode.Exclusive);
			return;
		}
		else if (mode == SelectionMode.Exclusive)
		{
			// clear current selection
			selection.Clear();
		}
		
        // select clip and all clips linked to it
		if (uiClip.Clip.LinkGroupId.HasValue)
		{
			foreach (UIClip c in channels.SelectMany(ch => ch.Timeline.UIClips.Where(u => u.Clip.LinkGroupId == uiClip.Clip.LinkGroupId)))
			{
				selection.Add(c);
			}
		}
		else selection.Add(uiClip);
		
		UpdateSelection();
	}

	// when a clip gets control clicked on
	public void DeselectClip(UIClip uiClip)
	{
		if (!selection.Contains(uiClip)) return;

		 // deselect clip and all clips linked to it
		if (uiClip.Clip.LinkGroupId.HasValue)
		{
			foreach (UIClip c in channels.SelectMany(ch => ch.Timeline.UIClips.Where(u => u.Clip.LinkGroupId == uiClip.Clip.LinkGroupId)))
			{
				selection.Remove(c);
			}
		}
		else selection.Remove(uiClip);
		
		UpdateSelection();
	}

	void UpdateSelection()
	{
		// highlight current selection, unhighlight any other clips
		foreach (UIClip c in channels.SelectMany(ch => ch.Timeline.UIClips))
		{
			c.Selected = selection.Contains(c);
			c.SetOutlined(c.Selected);
		}
	}

	// dr
	public void DragClip()
	{
		
	}
}
