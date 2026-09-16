using EditSharp.Components;
using EditSharp.Components.Channels;
using EditSharp.Components.Clips;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class UIClipsView : PanelContainer
{
	[ExportGroup("Controls")]

	[Export] Control clipsControl;

	[ExportGroup("Packed Scenes")]

	[Export] PackedScene clipScene;

	public UITimeline UITimeline;
	public List<UIClip> UIClips { get; private set; } = [];

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		/*
		var channel = GetChannelAtPoint(GetGlobalMousePosition());
		string name = string.Empty;
		if (channel.exists)
		{
			if (channel.type == ChannelType.Video)
			{
				name = UITimeline.Timeline.VideoChannels[channel.index].Name;
			}
			else
			{
				name = UITimeline.Timeline.AudioChannels[channel.index].Name;
			}
		}
		*/
		
		//GD.Print($"mouse ({GetLocalMousePosition()}) is over {channel.type} channel {(name == string.Empty ? $"New {channel.index}" : name)}");
	}

	enum ChannelType { Video, Audio }
	(ChannelType type, int index, bool exists) GetChannelAtPoint(Vector2 globalPosition)
	{
		// convert global position to local position
		Vector2 localPosition = globalPosition - GlobalPosition;
		//GD.Print($"local position is {localPosition}");

		int channelsDown = (int)(localPosition.Y / UITimeline.VerticalScale);

		// if channels down is negative 
		// channel is a new video channel
		if (channelsDown < 0)
		{
			//GD.Print("new video channel");
			return (ChannelType.Video, UITimeline.Timeline.VideoChannels.Count - 1 - channelsDown, false);
		}
		// if channels down is greater than highest channel index
		// channel is a new audio channel
		else if (channelsDown > UITimeline.Timeline.Channels.Count - 1)
		{
			//GD.Print("new audio channel");
			return (ChannelType.Audio, channelsDown - UITimeline.Timeline.VideoChannels.Count, false);
		}
		// if channels down is greater than the highest video channel index
		// channel is an existing audio channel
		else if (channelsDown > UITimeline.Timeline.VideoChannels.Count - 1)
		{
			//GD.Print("existing audio channel");
			return (ChannelType.Audio, channelsDown - UITimeline.Timeline.VideoChannels.Count, true);
		}
		// otherwise, channel is an existing a video channel
		else
		{
			//GD.Print("existing video channel");
			return (ChannelType.Video, UITimeline.Timeline.VideoChannels.Count - 1 - channelsDown, true);
		}
	}

	public int Refresh(bool all = false)
	{
		if (all)
		{
			//delete all clips
			foreach (UIClip c in UIClips) c.QueueFree();
			UIClips.Clear();
		}
		
		// find all missing clips and generate their guis
		int clips = 0;
		foreach (Clip c in UITimeline.Timeline.Channels.SelectMany(ch => ch.Clips))
		{
			// existing clips
			if (UIClips.Any(u => ReferenceEquals(u.Clip, c)))
			{
				UIClips.First(u => ReferenceEquals(u.Clip, c)).Refresh();
				continue;
			}

			// new clips
			AddClip(c);
			clips++;
		}

		return clips;
	}

	public UIClip AddClip(Clip c)
	{
		UIClip clip = clipScene.Instantiate() as UIClip;

		clip.Clip = c;
		clip.ClipsView = this;

		UIClips.Add(clip);

		clipsControl.AddChild(clip);

		return clip;
	}

	public void RemoveUIClip(UIClip c)
	{
		UIClips.Remove(c);
		c.QueueFree();
	}

	// FUTURE: add a clip and create its clip data from a dragged in source
	// public void AddClip(Source s, int channelIndex)

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

		// the lowest channel the selection occupies
		public int HighestChannelIndex => Clips.Max(c => c.Clip.Channel.Index);
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
				if (!CurrentSelection.Clips.Contains(c)) CurrentSelection.Clips.Add(c);
			}
		}
		else if (!CurrentSelection.Clips.Contains(uiClip)) CurrentSelection.Clips.Add(uiClip);
		
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

	void UpdateSelection()
	{
		// highlight current selection, unhighlight any other clips
		foreach (UIClip c in UIClips)
		{
			c.Selected = CurrentSelection.Clips.Contains(c);
			c.SetOutlined(c.Selected);
		}
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

		var move = GetClipMove(uiClip, drag);

		// move clips visually
		// does not yet account for any scrolling
		foreach (UIClip s in CurrentSelection.Clips) s.MoveGUI(move.timeDelta, move.channelDelta);
	}

	// when the user lets go of the selection they were dragging
	public void FinishDrag(UIClip uiClip, (Vector2 start, Vector2 delta) drag)
	{
		// set all clips back to opaque
		foreach (UIClip c in UIClips)
		{
			c.SetTransparency(1f);
		}

		// move selection z index back down to other clips
		while (UIClips.Where(c => !CurrentSelection.Clips.Contains(c)).Max(c => c.ZIndex) < CurrentSelection.ZIndex) CurrentSelection.ZIndex--;

		var move = GetClipMove(uiClip, drag);

		// create any new channels needed so every clip in the selection has a valid target channel
		EnsureChannelsExist(move.channelDelta);

		// a clip landing on a channel overwrites whatever is already there, so walk the
		// selection front-first along the direction of travel - that way each clip only
		// ever lands on space another selected clip has already vacated
		int channelOrder = move.channelDelta >= 0 ? -1 : 1;
		long timeOrder = move.timeDelta >= TimeSpan.Zero ? -1 : 1;

		List<UIClip> ordered = [.. CurrentSelection.Clips
			.OrderBy(c => channelOrder * c.Clip.Channel.Index)
			.ThenBy(c => timeOrder * c.Clip.Start.Ticks)];

		// resolve every target before moving anything, so relocating one clip
		// can never perturb another clip's own target
		List<(Clip clip, TimeSpan start, Channel channel)> moves = [];
		foreach (UIClip s in ordered)
		{
			int targetIndex = s.Clip.Channel.Index + move.channelDelta;

			Channel targetChannel = s.Clip is VideoClip
				? UITimeline.Timeline.VideoChannels[targetIndex]
				: UITimeline.Timeline.AudioChannels[targetIndex];

			moves.Add((s.Clip, s.Clip.Start + move.timeDelta, targetChannel));
		}

		// edit underlying clip data
		foreach ((Clip clip, TimeSpan start, Channel channel) in moves) clip.Move(start, channel);

		List<UIClip> remove = [];
		foreach (UIClip c in UIClips)
		{
			// do not delete clips in selection
			if (CurrentSelection.Clips.Any(s => ReferenceEquals(c, s))) continue;

			// delete this clip's gui if it intersects selection
			if (CurrentSelection.Clips.Any(s => c.GetGlobalRect().Intersects(s.GetGlobalRect())))
			{
				GD.Print($"{c.Clip.Name} ({c.GetGlobalRect()}) intersects selection. regenerating");
				remove.Add(c);
			} 
		}

		for (int i = 0; i < remove.Count; i++) RemoveUIClip(remove[i]);

		// refresh all clips
		GD.Print($"refreshed {Refresh()} clips");
	}

	// create as many new video/audio channels as needed so every clip in the
	// current selection has a valid target channel at (its own channel index + channelDelta)
	void EnsureChannelsExist(int channelDelta)
	{
		if (channelDelta <= 0) return;

		if (CurrentSelection.Clips.Any(c => c.Clip is VideoClip))
		{
			int highestVideoTarget = CurrentSelection.Clips
				.Where(c => c.Clip is VideoClip)
				.Max(c => c.Clip.Channel.Index) + channelDelta;

			while (highestVideoTarget > UITimeline.Timeline.VideoChannels.Count - 1)
				UITimeline.Timeline.AddChannel(new VideoChannel());
		}

		if (CurrentSelection.Clips.Any(c => c.Clip is AudioClip))
		{
			int highestAudioTarget = CurrentSelection.Clips
				.Where(c => c.Clip is AudioClip)
				.Max(c => c.Clip.Channel.Index) + channelDelta;

			while (highestAudioTarget > UITimeline.Timeline.AudioChannels.Count - 1)
				UITimeline.Timeline.AddChannel(new AudioChannel());
		}

		// let the channel headers pick up any newly created channels
		UITimeline.RefreshChannelEdits();
	}

	(TimeSpan timeDelta, int channelDelta) GetClipMove(UIClip uiClip, (Vector2 start, Vector2 delta) drag)
	{
		int channelDragDelta = GetChannelDragDelta(uiClip, drag);

		float offset = drag.delta.X;
		// don't let offset move selection past zero
		if (!(CurrentSelection.EarliestPosition + UITimeline.PixelsToTimeSpan(offset) >= TimeSpan.Zero))
		{
			// reign offset back in
			offset = -(float)UITimeline.TimeSpanToPixels(CurrentSelection.EarliestPosition);
		}

		return (UITimeline.PixelsToTimeSpan(offset), channelDragDelta);
	}

	int GetChannelDragDelta(UIClip uiClip, (Vector2 start, Vector2 delta) drag)
	{
		var dragChannel = GetChannelAtPoint(drag.start + drag.delta);
		int channelDelta = 0;

		// dragChannel.index is a meaningful target even when dragChannel.exists is
		// false (it points one past the last channel of its kind) - that's how a
		// drag into the empty space above the top channel produces a positive
		// delta, which EnsureChannelsExist then uses to create new channels
		if (uiClip.Clip is VideoClip v)
		{
			if (dragChannel.type == ChannelType.Video)
			{
				channelDelta = dragChannel.index - v.Channel.Index;
				channelDelta = CurrentSelection.LowestChannelIndex + channelDelta >= 0 ? channelDelta : 0;
			}
			else return -CurrentSelection.LowestChannelIndex;
		}
		else if (uiClip.Clip is AudioClip a)
		{
			if (dragChannel.type == ChannelType.Audio)
			{
				channelDelta = dragChannel.index - a.Channel.Index;
				channelDelta = CurrentSelection.LowestChannelIndex + channelDelta >= 0 ? channelDelta : 0;
			}
			else return -CurrentSelection.LowestChannelIndex;
		}

		return channelDelta;
	}
}
