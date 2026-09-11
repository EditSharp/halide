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
	[Export] ScrollContainer timelinesContainer;
	[Export] VBoxContainer timelines;
	[Export] ScrollContainer rulerContainer;
	[Export] UIRuler ruler;

	[ExportGroup("Packed Scenes")]

	[Export] PackedScene channelEditScene;
	[Export] PackedScene channelTimelineScene;

	public Timeline Timeline;

	public List<UIChannel> UIChannels = [];
	public List<UIClip> UIClips => [.. UIChannels.SelectMany(c => c.ClipsView.UIClips)];

	public double VerticalScale { 
		get; 
		set => field = value > 0d 
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Vertical scale must be greater than zero");
	} = 90d;

	public double PixelsPerSecond { 
		get; 
		set => field = value > 0d 
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), "Pixels per second must be greater than zero");
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
		widthSlider.ValueChanged += SetChannelWidth;

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

		public required UIChannelClipsView ClipsView;
	}

	public void AddChannel(Channel c)
	{
		UIChannel channel = CreateUIChannel(c);
		channel.ClipsView.UITimeline = this;

		edits.AddChild(channel.Edit);
		timelines.AddChild(channel.ClipsView);
		UIChannels.Add(channel);
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
		foreach (var child in timelines.GetChildren())
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

		// update channel timelines
		foreach (var channel in UIChannels) channel.ClipsView.SetWidth(PixelsPerSecond);


		// update ruler
		ruler.Update(PixelsPerSecond, ProjectManager.Singleton.CurrentProject.RenderSettings.Framerate);
	}

	// all selected clips
	public class Selection
	{
		public List<UIClip> Clips = [];

		public TimeSpan EarliestPosition => Clips.Min(c => c.Clip.Start);
		public TimeSpan LatestPosition => Clips.Max(c => c.Clip.End);
	}

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

	Selection CurrentSelection = new();

	public void SelectClip(UIClip uiClip, SelectionMode mode = SelectionMode.ExclusiveIfUnselected, bool invert = false)
	{
		if (mode == SelectionMode.ExclusiveIfUnselected)
		{
			if (!CurrentSelection.Clips.Contains(uiClip)) SelectClip(uiClip, SelectionMode.Exclusive);
			return;
		}
		else if (mode == SelectionMode.Exclusive)
		{
			// clear current selection
			CurrentSelection.Clips.Clear();
		}
		
        // select clip and all clips linked to it
		if (uiClip.Clip.LinkGroupId.HasValue)
		{
			foreach (UIClip c in UIClips.Where(u => u.Clip.LinkGroupId == uiClip.Clip.LinkGroupId))
			{
				CurrentSelection.Clips.Add(c);
			}
		}
		else CurrentSelection.Clips.Add(uiClip);
		
		UpdateSelection();
	}

	// when a clip gets control clicked on
	public void DeselectClip(UIClip uiClip)
	{
		if (!CurrentSelection.Clips.Contains(uiClip)) return;

		 // deselect clip and all clips linked to it
		if (uiClip.Clip.LinkGroupId.HasValue)
		{
			foreach (UIClip c in UIClips.Where(u => u.Clip.LinkGroupId == uiClip.Clip.LinkGroupId))
			{
				CurrentSelection.Clips.Remove(c);
			}
		}
		else CurrentSelection.Clips.Remove(uiClip);
		
		UpdateSelection();
	}

	// drag selection with context provided from the dragged clip
	public void DragSelection(Vector2 delta)
	{
		// make selection translucent
		foreach (UIClip c in UIClips)
		{
			c.SetTransparency(CurrentSelection.Clips.Contains(c) ? 0.5f : 1f);
		}

		// move clips visually
		// account for any scrolling
		foreach (UIClip s in CurrentSelection.Clips)
		{
			Vector2 offset = delta;

			// don't let offset move selection past zero
			if (!(CurrentSelection.EarliestPosition - PixelsToTimeSpan(delta.X) >= TimeSpan.Zero))
			{
				// reign offset back in
				offset = new((float)TimeSpanToPixels(CurrentSelection.EarliestPosition), delta.Y);
			}

			s.Position = new(
				(float)TimeSpanToPixels(s.Clip.Start) - offset.X,
				s.Position.Y
			);
		}
	}

	// when the user lets go of the selection they were dragging
	public void FinishDrag(Vector2 delta)
	{
		// set all clips back to opaque
		foreach (UIClip c in UIClips) c.SetTransparency(1f);

		Vector2 offset = delta;

		// don't let offset move selection past zero
		if (!(CurrentSelection.EarliestPosition - PixelsToTimeSpan(delta.X) >= TimeSpan.Zero))
		{
			// reign offset back in
			offset = new((float)TimeSpanToPixels(CurrentSelection.EarliestPosition), delta.Y);
		}
			

		// edit underlying clip data
		foreach (UIClip s in CurrentSelection.Clips)
		{
			s.Clip.Move(PixelsToTimeSpan(TimeSpanToPixels(s.Clip.Start) - offset.X));
		}

		
		foreach (UIClip c in UIClips)
		{
			foreach (UIClip s in CurrentSelection.Clips)
			{
				// do not delete clips in selection
				if (ReferenceEquals(s, c)) continue;

				// delete this clip's gui if it intersects selection
				if (c.GetRect().Intersects(s.GetRect())) c.ClipsView.RemoveUIClip(c);
			}
		}

		// refresh all channel clip views
		foreach (UIChannel ch in UIChannels) ch.ClipsView.Refresh();
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
