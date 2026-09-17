using EditSharp.Components;
using EditSharp.Components.Channels;
using EditSharp.Components.Clips;
using EditSharpGUI.Scripts.Input;
using EditSharpGUI.Scripts.UI;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class UIClipsView : PanelContainer, IDragCancellable
{
	[ExportGroup("Controls")]

	[Export] Control clipsControl;

	[ExportGroup("Packed Scenes")]

	[Export] PackedScene clipScene;

	public UITimeline UITimeline;
	public List<UIClip> UIClips { get; private set; } = [];


	// a captured drag can end without a release - the window lost focus, or the
	// os swallowed the button. the pan cursor would otherwise stay stuck on
	public void CancelDrag(MouseButtonState button) => MouseDefaultCursorShape = CursorShape.Arrow;

    public override void _GuiInput(InputEvent _)
	{
		MouseButtonState left = InputManager.Singleton.Mouse.LeftButton;
		MouseButtonState middle = InputManager.Singleton.Mouse.MiddleButton;

        switch (left.Action)
        {
            case MouseAction.Press:
            case MouseAction.DoubleClick:
                if (!left.Capture(this)) break;

                if (left.PressModifiers.Shift) SelectAll();
                // ctrl on empty space deliberately does nothing - it is the
                // "remove from selection" modifier and there is nothing here
                else if (!left.PressModifiers.Control) DeselectAll();
                break;
        }

		switch (middle.Action)
		{
			case MouseAction.Press:
				middle.Capture(this);
				break;

			case MouseAction.DragStart:
				if (!middle.HasCapture(this)) break;
				UITimeline.BeginViewScroll();
				goto case MouseAction.DragMove;

			case MouseAction.DragMove:
				if (!middle.HasCapture(this)) break;
				MouseDefaultCursorShape = CursorShape.Drag;
				// the step delta, not the whole drag: DragView accumulates into
				// ScrollHorizontal, so the total would re-apply the distance every
				// event and the view would fly off
				UITimeline.DragView(InputManager.Singleton.Mouse.GetDragStepDelta(middle));
				break;

			case MouseAction.DragEnd:
				MouseDefaultCursorShape = CursorShape.Arrow;
				break;
		}

		if (InputManager.Singleton.Mouse.IsScrolling)
		{
			Modifiers mods = InputManager.Singleton.Modifiers;
			float steps = InputManager.Singleton.Mouse.Scroll.Y;

			// AcceptEvent is what stops the wheel here. without it the parent
			// ScrollContainer scrolls as well, because Control defaults to
			// mouse_force_pass_scroll_events - wheel events are sent past a Stop
			// filter on purpose so nested views still scroll. an unmodified wheel
			// is deliberately left alone so it still reaches the container
			Vector2 cursor = InputManager.Singleton.Mouse.CurrentPosition;

			if (mods.Control)
			{
				UITimeline.ZoomTime(steps, cursor.X);
				AcceptEvent();
			}
			else if (mods.Alt)
			{
				UITimeline.ZoomChannelHeight(steps, cursor.Y);
				AcceptEvent();
			}
		}

		// rubber-band selection goes on left DragStart/DragMove here
	}

	

	enum ChannelType { Video, Audio }
	(ChannelType type, int index, bool exists) GetChannelAtPoint(Vector2 globalPosition)
	{
		// convert global position to local position.
		// deliberately not measured against GlobalPosition - creating a channel
		// moves the scroll, and this node does not follow until the container
		// lays out, so for that frame it would read a whole channel out
		Vector2 localPosition = UITimeline.ToViewContent(globalPosition);

		// floor, not truncate: above the top row this goes negative, and (int)
		// rounds -0.5 to 0, so the drag never noticed it had left the timeline
		// the offset takes off the phantom rows, which are not channels yet
		int channelsDown = Mathf.FloorToInt((float)(localPosition.Y / UITimeline.VerticalScale)) - ChannelOffset;

		// if channels down is negative 
		// channel is a new video channel
		if (channelsDown < 0)
		{
			return (ChannelType.Video, UITimeline.Timeline.VideoChannels.Count - 1 - channelsDown, false);
		}
		// if channels down is greater than highest channel index
		// channel is a new audio channel
		else if (channelsDown > UITimeline.Timeline.Channels.Count - 1)
		{
			return (ChannelType.Audio, channelsDown - UITimeline.Timeline.VideoChannels.Count, false);
		}
		// if channels down is greater than the highest video channel index
		// channel is an existing audio channel
		else if (channelsDown > UITimeline.Timeline.VideoChannels.Count - 1)
		{
			return (ChannelType.Audio, channelsDown - UITimeline.Timeline.VideoChannels.Count, true);
		}
		// otherwise, channel is an existing a video channel
		else
		{
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
	public class ClipsSelection : Selection<UIClip>
	{
		public TimeSpan EarliestPosition => this.Min(c => c.Clip.Start);
		public TimeSpan LatestPosition => this.Max(c => c.Clip.End);

		public int ZIndex 
		{ 
			get
			{
				return this.Min(c => c.ZIndex);
			}
			set
			{
				int offset = value - ZIndex;

				foreach (UIClip clip in this) clip.ZIndex += offset;
			}
		}

		// the lowest channel the selection occupies
		public int LowestChannelIndex => this.Min(c => c.Clip.Channel.Index);

		// the lowest channel the selection occupies
		public int HighestChannelIndex => this.Max(c => c.Clip.Channel.Index);
	}

	ClipsSelection Selection = new();

	public void SelectClip(UIClip uiClip, SelectionMode mode = SelectionMode.ExclusiveIfUnselected, bool invert = false)
	{
		// select clip and all clips linked to it
		if (uiClip.Clip.LinkGroupId is not null)
			Selection.Select(UIClips.Where(u => u.Clip.LinkGroupId == uiClip.Clip.LinkGroupId), mode);
		else Selection.Select(uiClip, mode);
		
		
		UpdateSelection();
	}

	public void SelectAll()
	{
		// add any clips not already in selection
		Selection.Select(UIClips, SelectionMode.Inclusive);

		UpdateSelection();
	}

	// when a clip gets control clicked on
	public void DeselectClip(UIClip uiClip)
	{
		if (uiClip.Clip.LinkGroupId is not null)
			Selection.Deselect(UIClips.Where(u => u.Clip.LinkGroupId == uiClip.Clip.LinkGroupId));
		else Selection.Deselect(uiClip);
		
		UpdateSelection();
	}

	public void DeselectAll()
	{
		Selection.Clear();

		UpdateSelection();
	}

	void UpdateSelection()
	{
		// highlight current selection, unhighlight any other clips
		foreach (UIClip c in UIClips)
		{
			c.Selected = Selection.Contains(c);
		}
	}

	// abandon a drag that will never get a release of its own - the window lost
	// focus, or the os swallowed the button up. put back everything BeginDrag
	// changed and leave the clip data alone
	public void CancelDrag()
	{
		ClearDragState();

		// still settle on the way out: SettleAfterDrag is what releases the
		// content, and skipping it here would hold the timeline open for good
		if (Selection.Count == 0) { SettleAfterDrag(); return; }

		// set all clips back to opaque
		foreach (UIClip c in UIClips) c.SetTransparency(1f);

		// move selection z index back down to other clips
		while (UIClips.Where(c => !Selection.Contains(c)).Select(c => c.ZIndex).DefaultIfEmpty(int.MaxValue).Max() < Selection.ZIndex) Selection.ZIndex--;

		// the drag only ever moved the gui, so the data is still right and
		// Refresh puts every clip back on top of where it actually belongs
		Refresh();

		SettleAfterDrag();
	}

	// one-time setup when a drag begins: lift the selection clear of the other
	// clips and make it translucent so the user can see what is underneath
	// the clip drag in progress, if any. held so the view can keep scrolling and
	// keep the selection under the cursor on frames where the mouse never moved
	UIClip dragClip;
	Vector2 dragScrollAtStart;

	public override void _Process(double delta)
	{
		if (dragClip is null) return;

		MouseButtonState left = InputManager.Singleton.Mouse.LeftButton;

		// belt and braces: if the gesture ended by any route that did not come
		// back through FinishDrag or CancelDrag, stop driving it
		if (!left.HasCapture(dragClip))
		{
			CancelDrag();
			return;
		}

		// scroll while the cursor is pushing against an edge of the visible area
		Vector2 push = UITimeline.GetEdgePush(InputManager.Singleton.Mouse.CurrentPosition);

		// vertically, getting the selection into sight comes first - whether or
		// not the cursor is asking for it, a clip left off the edge has to be
		// reachable. and it has to win outright while it lasts: revealing moves
		// the view, which moves what the cursor is pointing at, which can unpin
		// the drag and hand the cursor its old push straight back. the two then
		// alternate frame by frame and the whole thing crawls to a halt just
		// short of arriving.
		//
		// once there is nothing left to reveal, a pinned selection gets no
		// vertical scroll at all: there is nothing further for it to be dragged
		// onto, so chasing the cursor only carries the view away from the clip.
		// horizontal is untouched either way - a pinned selection still slides
		// along in time
		float reveal = GetDragClipRevealDistance(push.Y);

		if (reveal != 0f) push.Y = Mathf.Clamp(reveal / (float)delta, -REVEAL_SCROLL_SPEED, REVEAL_SCROLL_SPEED);
		else if (verticalDragPinned) push.Y = 0f;

		if (push != Vector2.Zero) UITimeline.ScrollView(push * (float)delta);

		// re-apply the move every frame, not just on motion. the view can slide
		// out from under a stationary cursor - from the edge scroll above, or
		// from the user scrolling mid-drag - and the selection has to follow it
		DragSelection(dragClip, (left.ClickStartPosition, InputManager.Singleton.Mouse.GetDragDelta(left)));
	}

	// the selection follows the cursor in content space, so any scrolling that
	// happened since the drag began counts as extra travel. only the horizontal
	// half: GetChannelAtPoint works off this control GlobalPosition, which the
	// scroll container has already moved, so vertical is accounted for there
	(Vector2 start, Vector2 delta) WithViewScroll((Vector2 start, Vector2 delta) drag)
		=> (drag.start, drag.delta + new Vector2(UITimeline.ViewScroll.X - dragScrollAtStart.X, 0f));

	// the content sizes itself to the clips inside it, so a clip being dragged
	// changes it every frame
	FitToChildren ClipsBounds => clipsControl as FitToChildren;

	// channels a drag is reaching for that do not exist yet. these are ui only -
	// placeholder rows in the channel list, plus the space they take up here -
	// so a drag never touches the project until it is actually dropped
	int phantomVideoChannels;
	int phantomAudioChannels;

	// how far every clip is laid out below its real row, to leave the phantom
	// video channels their room above it
	public int ChannelOffset => phantomVideoChannels;

	// grow the phantom rows to cover wherever this move is reaching. grow only
	// for the life of the drag, so crossing the boundary back and forth does not
	// make the timeline shuffle
	void EnsurePhantomChannels(int channelDelta)
	{
		int video = phantomVideoChannels;
		int audio = phantomAudioChannels;

		// a video clip aiming above the top channel lands on a negative row
		int topRow = Selection
			.Where(c => c.Clip is VideoClip)
			.Select(c => c.GetChannelsDownAfter(channelDelta))
			.DefaultIfEmpty(0)
			.Min();

		if (topRow < 0) video = Mathf.Max(video, -topRow);

		// and an audio clip aiming below the bottom one lands past the last row
		int bottomRow = Selection
			.Where(c => c.Clip is AudioClip)
			.Select(c => c.GetChannelsDownAfter(channelDelta))
			.DefaultIfEmpty(0)
			.Max();

		int lastRow = UITimeline.Timeline.Channels.Count - 1;
		if (bottomRow > lastRow) audio = Mathf.Max(audio, bottomRow - lastRow);

		if (video == phantomVideoChannels && audio == phantomAudioChannels) return;

		int added = video - phantomVideoChannels;

		phantomVideoChannels = video;
		phantomAudioChannels = audio;

		// the offset moved, so every clip placed from data moves with it. the
		// selection is about to be placed by the MoveGUI calls that follow
		foreach (UIClip c in UIClips.Where(c => !Selection.Contains(c))) c.Refresh();

		UITimeline.SetPhantomChannels(phantomVideoChannels, phantomAudioChannels);

		// phantom video rows sit above everything else, so they push it all down
		ShiftViewForNewChannels(added);
	}

	// hand the phantom rows back, and report how many rows of video went with
	// them - every one of those lifts the whole content by a row
	int ClearPhantomChannels()
	{
		int video = phantomVideoChannels;

		phantomVideoChannels = 0;
		phantomAudioChannels = 0;

		UITimeline.SetPhantomChannels(0, 0);

		return video;
	}

	// the clips have just been re-placed without the phantom rows under them, so
	// they all sit that many rows higher. the view comes with them, in this same
	// frame - easing it instead means the content jumps first and the ease spends
	// its time undoing that, which is not a rebound, just a lurch and a recovery
	void ReleaseViewRows(int rows)
	{
		if (rows == 0) return;

		UITimeline.ScrollViewNow(new Vector2(0f, (float)(-rows * UITimeline.VerticalScale)));
	}

	// every route out of a drag goes through here. miss one and _Process keeps
	// edge-scrolling a finished drag, or the content stays held open forever.
	// GrowOnly is deliberately left on - SettleAfterDrag releases it once the
	// view has eased to wherever the shrinking content is going to put it
	void ClearDragState()
	{
		dragClip = null;
		verticalDragPinned = false;
	}

	// video channels stack upwards but content coordinates only grow downwards,
	// so a row opened above pushes every existing one down. the view goes with
	// them, or the timeline appears to lurch and - worse - the cursor ends up
	// over a different channel than it was, which asks for another row, and
	// another, and never stops
	void ShiftViewForNewChannels(int rows)
	{
		if (rows <= 0) return;

		float height = (float)(rows * UITimeline.VerticalScale);

		// grow the content by hand, in the same breath. the container works its
		// scroll maximum out from the content minimum size on its next sort, and
		// moving the scroll queues one, so a maximum raised on its own is undone
		// before anything gets to use it - which is what made the view jolt by
		// exactly the amount it was supposed to be compensating for
		if (ClipsBounds is not null) ClipsBounds.CustomMinimumSize += new Vector2(0f, height);

		UITimeline.ReserveViewTop(height);
	}


	// the content has been held open for the whole drag. dropping that now would
	// snap the scroll to the new maximum in a single frame and read as the view
	// teleporting. work out where it is going to land, slide there, and only
	// then let it shrink
	// the phantom rows are still standing at this point, and so is the content
	// the drag held open. the view slides to where it will sit once both are
	// given back, and only then are they actually given back - killing them up
	// front is what made the channel list jump a row ahead of the clips
	void SettleAfterDrag()
	{
		if (ClipsBounds is null) return;

		int releaseRows = phantomVideoChannels;

		// what the content actually needs now, against what it is being held at
		Vector2 natural = Vector2.Zero;
		foreach (UIClip c in UIClips)
		{
			natural = new(Mathf.Max(natural.X, c.GetRect().End.X), Mathf.Max(natural.Y, c.GetRect().End.Y));
		}

		Vector2 held = ClipsBounds.CustomMinimumSize;
		Vector2 shrink = new(Mathf.Max(0f, held.X - natural.X), Mathf.Max(0f, held.Y - natural.Y));

		Vector2 target = UITimeline.GetSettledScroll(shrink, (float)(releaseRows * UITimeline.VerticalScale));

		UITimeline.EaseScrollTo(target, () => ReleasePhantomsAndContent(releaseRows));
	}

	// run once the view has finished sliding. the phantom rows come out, the
	// clips re-place without them, and the scroll drops the same distance in the
	// same frame - so none of the three shows on its own
	void ReleasePhantomsAndContent(int releaseRows)
	{
		ClearPhantomChannels();
		Refresh();
		ReleaseViewRows(releaseRows);

		if (ClipsBounds is not null) ClipsBounds.GrowOnly = false;
	}

	// the drag is committing, so the phantom rows it actually used become real
	// channels and their placeholders step aside. the total number of rows above
	// the timeline is unchanged, which is why nothing moves as it happens
	void RealisePhantomChannels(int video, int audio)
	{
		if (video <= 0 && audio <= 0) return;

		phantomVideoChannels = Mathf.Max(0, phantomVideoChannels - video);
		phantomAudioChannels = Mathf.Max(0, phantomAudioChannels - audio);

		UITimeline.SetPhantomChannels(phantomVideoChannels, phantomAudioChannels);
	}

	public void BeginDrag(UIClip uiClip)
	{
		if (Selection.Count == 0) return;

		dragClip = uiClip;
		UITimeline.BeginViewScroll();
		UITimeline.FlushScrollEase();

		// hold the content open for the duration. letting it shrink while the
		// selection moves feeds the clamped scroll back into the clip positions
		// and the view bolts to the start - see FitToChildren.GrowOnly
		if (ClipsBounds is not null) ClipsBounds.GrowOnly = true;

		dragScrollAtStart = UITimeline.ViewScroll;

		// make selection translucent
		foreach (UIClip c in UIClips)
		{
			c.SetTransparency(Selection.Contains(c) ? 0.5f : 1f);
		}

		// move selection z index above other clips.
		// everything may be selected, in which case there is nothing to clear
		while (UIClips.Where(c => !Selection.Contains(c)).Select(c => c.ZIndex).DefaultIfEmpty(int.MinValue).Max() >= Selection.ZIndex) Selection.ZIndex++;
	}

	// drag selection with context provided from the dragged clip
	public void DragSelection(UIClip uiClip, (Vector2 start, Vector2 delta) drag)
	{
		if (Selection.Count == 0) return;

		var move = GetClipMove(uiClip, WithViewScroll(drag));

		// open placeholder rows for wherever this move is reaching, so the
		// preview has somewhere to sit and a header to line up against
		EnsurePhantomChannels(move.channelDelta);

		// move clips visually
		foreach (UIClip s in Selection) s.MoveGUI(move.timeDelta, move.channelDelta);
	}

	// when the user lets go of the selection they were dragging
	public void FinishDrag(UIClip uiClip, (Vector2 start, Vector2 delta) drag)
	{
		// clear before the guard below - leaving these set would keep _Process
		// edge-scrolling for a drag that is already over, and hold the content
		// open at whatever width the drag stretched it to
		drag = WithViewScroll(drag);

		// still settle on the way out: SettleAfterDrag is what releases the
		// content, and skipping it here would hold the timeline open for good
		if (Selection.Count == 0) { ClearDragState(); SettleAfterDrag(); return; }

		// resolve the move while the phantom rows are still in place, since
		// GetChannelAtPoint measures against them
		var move = GetClipMove(uiClip, drag);

		int videoBefore = UITimeline.Timeline.VideoChannels.Count;
		int audioBefore = UITimeline.Timeline.AudioChannels.Count;

		ClearDragState();

		// set all clips back to opaque
		foreach (UIClip c in UIClips)
		{
			c.SetTransparency(1f);
		}

		// move selection z index back down to other clips.
		// everything may be selected, in which case there is nothing to drop below
		while (UIClips.Where(c => !Selection.Contains(c)).Select(c => c.ZIndex).DefaultIfEmpty(int.MaxValue).Max() < Selection.ZIndex) Selection.ZIndex--;

		// create any new channels needed so every clip in the selection has a valid target channel
		EnsureChannelsExist(move.channelDelta);

		// the placeholders for the ones that just became real step aside, so the
		// rows above the timeline come to the same total as before
		RealisePhantomChannels(
			UITimeline.Timeline.VideoChannels.Count - videoBefore,
			UITimeline.Timeline.AudioChannels.Count - audioBefore
		);

		// a clip landing on a channel overwrites whatever is already there, so walk the
		// selection front-first along the direction of travel - that way each clip only
		// ever lands on space another selected clip has already vacated
		int channelOrder = move.channelDelta >= 0 ? -1 : 1;
		long timeOrder = move.timeDelta >= TimeSpan.Zero ? -1 : 1;

		List<UIClip> ordered = [.. Selection
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
			if (Selection.Any(s => ReferenceEquals(c, s))) continue;

			// delete this clip's gui if it intersects selection
			if (Selection.Any(s => c.GetGlobalRect().Intersects(s.GetGlobalRect())))
			{
				GD.Print($"{c.Clip.Name} ({c.GetGlobalRect()}) intersects selection. regenerating");
				remove.Add(c);
			} 
		}

		for (int i = 0; i < remove.Count; i++) RemoveUIClip(remove[i]);

		GD.Print($"refreshed {Refresh()} clips");

		SettleAfterDrag();
	}

	// create as many new video/audio channels as needed so every clip in the
	// current selection has a valid target channel at (its own channel index + channelDelta)
	void EnsureChannelsExist(int channelDelta)
	{
		if (channelDelta <= 0) return;

		if (Selection.Any(c => c.Clip is VideoClip))
		{
			int highestVideoTarget = Selection
				.Where(c => c.Clip is VideoClip)
				.Max(c => c.Clip.Channel.Index) + channelDelta;

			while (highestVideoTarget > UITimeline.Timeline.VideoChannels.Count - 1)
				UITimeline.Timeline.AddChannel(new VideoChannel());
		}

		if (Selection.Any(c => c.Clip is AudioClip))
		{
			int highestAudioTarget = Selection
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
		if (!(Selection.EarliestPosition + UITimeline.PixelsToTimeSpan(offset) >= TimeSpan.Zero))
		{
			// reign offset back in
			offset = -(float)UITimeline.TimeSpanToPixels(Selection.EarliestPosition);
		}

		return (UITimeline.PixelsToTimeSpan(offset), channelDragDelta);
	}

	// how fast the view catches up to a pinned selection that is off the edge of
	// it, in pixels per second
	const float REVEAL_SCROLL_SPEED = 900f;

	// how far the view has to move, in pixels, to bring the clip being dragged
	// back into sight. zero once there is nothing left to show. heading is which
	// way the drag is pushing, which only matters for a clip too tall to fit.
	//
	// deliberately the one clip under the cursor rather than the whole
	// selection. a selection holding both video and audio pulls apart as it
	// moves - the delta sends video up its channels and audio down its own - so
	// its bounds grow every frame and say nothing about where the user is
	// steering. the clip in hand is the thing that has to stay in sight
	float GetDragClipRevealDistance(float heading)
	{
		if (dragClip is null) return 0f;

		Vector2 range = UITimeline.ViewVerticalRange;

		// measured in content space, from positions the drag sets directly.
		// the on-screen rects would do too, but they trail a scroll by a frame
		float top = dragClip.Position.Y;
		float bottom = top + dragClip.Size.Y;

		// no scroll position shows all of a clip taller than the view, so show
		// the end the drag is heading for rather than stopping somewhere in the
		// middle of it. each direction stops once that end is reached
		if (bottom - top >= range.Y - range.X)
		{
			if (heading > 0f) return Mathf.Max(0f, bottom - range.Y);
			if (heading < 0f) return Mathf.Min(0f, top - range.X);

			return 0f;
		}

		if (top < range.X) return top - range.X;
		if (bottom > range.Y) return bottom - range.Y;

		return 0f;
	}

	// set when the cursor is reaching for a channel the selection cannot follow
	// it to: a video clip pushed down past the bottom video channel, or an audio
	// clip pushed up past the top audio one. those directions are walls - unlike
	// video upward or audio downward, where new channels can always be made - so
	// the edge scroll reads this and stops chasing a cursor it cannot serve
	bool verticalDragPinned;

	int GetChannelDragDelta(UIClip uiClip, (Vector2 start, Vector2 delta) drag)
	{
		var dragChannel = GetChannelAtPoint(drag.start + drag.delta);

		verticalDragPinned = false;

		// dragChannel.index is a meaningful target even when dragChannel.exists is
		// false (it points one past the last channel of its kind) - that's how a
		// drag into the empty space above the top channel produces a positive
		// delta, which EnsureChannelsExist then uses to create new channels
		if (uiClip.Clip is VideoClip v)
		{
			// the cursor has left the video channels behind, so the furthest this
			// can go is the bottom one
			if (dragChannel.type != ChannelType.Video) return Pin();

			return Limit(dragChannel.index - v.Channel.Index);
		}

		if (uiClip.Clip is AudioClip a)
		{
			if (dragChannel.type != ChannelType.Audio) return Pin();

			return Limit(dragChannel.index - a.Channel.Index);
		}

		return 0;

		// no channel of either kind has an index below zero, so the selection
		// cannot go past the first one of its own kind
		int Limit(int channelDelta)
			=> Selection.LowestChannelIndex + channelDelta >= 0 ? channelDelta : Pin();

		int Pin()
		{
			verticalDragPinned = true;

			// as far as it will go, rather than refusing to move at all
			return -Selection.LowestChannelIndex;
		}
	}

}
