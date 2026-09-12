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
	[Export] ScrollContainer clipsViewsContainer;
	[Export] VBoxContainer clipsViews;
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
        editsContainer.ScrollVertical = clipsViewsContainer.ScrollVertical;
		rulerContainer.ScrollHorizontal = clipsViewsContainer.ScrollHorizontal;

		//stretch ruler to length of channels
		ruler.CustomMinimumSize = new(
			clipsViews.Size.X + clipsViewsContainer.GetVScrollBar().Size.X,
			ruler.CustomMinimumSize.Y
		);

		// show scrollbar spacer if timeline is scrollable
		editsScrollSpacer.Visible = clipsViewsContainer.GetHScrollBar().Visible;
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

		public int ZIndex 
		{ 
			get
			{
				return Clips.Min(c => c.ZIndex);
			}
			set
			{
				int offset = value - ZIndex;

				foreach (UIClip clip in Clips) clip.ZIndex += offset;
			}
		}

		// the lowest channel the selection occupies
		public int LowestChannelIndex => Clips.Min(c => c.Clip.Channel.Index);
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
	public void DragSelection(UIClip uiClip, (Vector2 start, Vector2 delta) drag)
	{
		// make selection translucent
		foreach (UIClip c in UIClips)
		{
			c.SetTransparency(CurrentSelection.Clips.Contains(c) ? 0.5f : 1f);
		}

		// move selection z index above other clips
		while (UIClips.Where(c => !CurrentSelection.Clips.Contains(c)).Max(c => c.ZIndex) >= CurrentSelection.ZIndex) CurrentSelection.ZIndex++;

		// move clips visually
		// does not yet account for any scrolling

		int channelDragDelta = GetChannelDragDelta(uiClip, drag);
		foreach (UIClip s in CurrentSelection.Clips)
		{
			// VERTICAL

			if (channelDragDelta != 0)
			{
				if (s.Clip is VideoClip vi)
				{
					s.ClipsView.RemoveUIClip(s);
					UIVideoChannels[s.Clip.Channel.Index + channelDragDelta].ClipsView.AddUIClip(s);
				}
				else if (s.Clip is AudioClip a)
				{
					s.ClipsView.RemoveUIClip(s);
					UIAudioChannels[s.Clip.Channel.Index + channelDragDelta].ClipsView.AddUIClip(s);
				}
			}

			// HORIZONTAL

			float offset = drag.delta.X;

			// don't let offset move selection past zero
			if (!(CurrentSelection.EarliestPosition - PixelsToTimeSpan(offset) >= TimeSpan.Zero))
			{
				// reign offset back in
				offset = (float)TimeSpanToPixels(CurrentSelection.EarliestPosition);
			}

			s.Position = new(
				(float)TimeSpanToPixels(s.Clip.Start) - offset,
				s.Position.Y
			);
		}
	}

	// when the user lets go of the selection they were dragging
	public void FinishDrag(UIClip uiClip, Vector2 delta)
	{
		// set all clips back to opaque
		foreach (UIClip c in UIClips)
		{
			c.SetTransparency(1f);
		}

		// move selection z index back down to other clips
		while (UIClips.Where(c => !CurrentSelection.Clips.Contains(c)).Max(c => c.ZIndex) < CurrentSelection.ZIndex) CurrentSelection.ZIndex--;

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
			// do not delete clips in selection
			if (CurrentSelection.Clips.Any(s => ReferenceEquals(c, s))) continue;

			// delete this clip's gui if it intersects selection
			if (CurrentSelection.Clips.Any(s => c.GetGlobalRect().Intersects(s.GetGlobalRect())))
			{
				GD.Print($"{c.Clip.Name} ({c.GetGlobalRect()}) intersects selection. regenerating");
				c.ClipsView.RemoveUIClip(c);
				c.QueueFree();
			} 
		}

		// refresh all channel clip views
		int clips = 0;
		foreach (UIChannel ch in UIChannels) clips += ch.ClipsView.Refresh();
		GD.Print($"refreshed {clips} clips");
	}

	int GetChannelDragDelta(UIClip uiClip, (Vector2 start, Vector2 delta) drag)
	{
		UIChannel dragChannel = null;
		int channelDelta = 0;

		if (uiClip.Clip is VideoClip v)
		{
			dragChannel= UIVideoChannelAtPoint(drag.start - drag.delta);
		}
		else if (uiClip.Clip is AudioClip a)
		{
			dragChannel= UIAudioChannelAtPoint(drag.start - drag.delta);
		}

		if (dragChannel is not null)
		{
			channelDelta = dragChannel.Channel.Index - uiClip.Clip.Channel.Index;
			channelDelta = CurrentSelection.LowestChannelIndex + channelDelta >= 0 ? channelDelta : 0;
		}

		return channelDelta;
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
