using EditSharp.Components;
using EditSharp.Components.Channels;
using EditSharp.Components.Clips;
using EditSharp.History;
using EditSharpGUI.Scripts;
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

	// ---- keyboard ----

	// any press in here, on empty space or on a clip, makes this the view
	// shortcuts are meant for until something else is clicked. the timeline
	// above answers them - it is on the way up from here
	public void ClaimKeyboard() => InputManager.Singleton.Keyboard.Capture(this);

	// ---- editing the selection: cut, copy, paste, split, delete ----

	// the channel the user last clicked, on a clip or on empty space. a
	// paste re-bases the copied clips of that kind onto it. lastClicked is
	// the clip itself when it was one - the copy remembers it as the clip in
	// hand, so the paste lines that clip up with the target rather than the
	// bottom of the set
	(bool video, int index)? pasteTarget;
	UIClip lastClicked;

	public void MarkTarget(UIClip clip)
	{
		lastClicked = clip;

		if (clip.Clip.Channel is Channel channel) pasteTarget = (channel is VideoChannel, channel.Index);
	}

	void MarkTarget((ChannelType type, int index, bool exists) at)
	{
		lastClicked = null;

		if (at.exists) pasteTarget = (at.type == ChannelType.Video, at.index);
	}

	public void CopySelection()
	{
		if (Selection.Count == 0) return;

		Clip anchor = lastClicked is not null && Selection.Contains(lastClicked) ? lastClicked.Clip : null;

		Clipboard.Shared.Copy(ClipsItem.From(Selection.Select(s => s.Clip), anchor));
	}

	// copy, then a plain delete - the gap stays
	public void CutSelection()
	{
		if (Selection.Count == 0) return;

		CopySelection();
		DeleteSelection(RippleScope.None, "Cut");
	}

	// the copied clips land with the earliest at the playhead, keeping their
	// spacing. the ones of the kind the user last clicked are re-based onto
	// that channel: the clip that was in hand when copying goes there and
	// the rest keep their places around it (the lowest goes there when no
	// clip was in hand). the other kind stays on the channels it came from.
	// whatever is under them is overwritten, like a drop, and channels are
	// made where the paste reaches past the top - never below the bottom
	public void Paste()
	{
		if (!Clipboard.Shared.TryGet(out ClipsItem item) || item.Entries.Count == 0) return;

		List<ClipsItem.Entry> entries = item.Materialize();
		TimeSpan at = UITimeline.PlayheadTime;

		if (pasteTarget is (bool video, int index))
		{
			List<ClipsItem.Entry> ofKind = [.. entries.Where(e => e.Video == video)];

			if (ofKind.Count > 0)
			{
				int reference = item.AnchorIndex >= 0 && entries[item.AnchorIndex].Video == video
					? entries[item.AnchorIndex].ChannelIndex
					: ofKind.Min(e => e.ChannelIndex);

				int shift = index - reference;

				// nothing can go below the first channel of its kind
				shift = Mathf.Max(shift, -ofKind.Min(e => e.ChannelIndex));

				entries = [.. entries.Select(e => e.Video == video ? e with { ChannelIndex = e.ChannelIndex + shift } : e)];
			}
		}

		List<Clip> pasted = [];

		using (Transaction.Scope change = UITimeline.History.Begin(entries.Count == 1 ? "Paste clip" : $"Paste {entries.Count} clips"))
		{
			foreach (ClipsItem.Entry e in entries)
			{
				e.Clip.Start = at + e.Offset;
				EnsureChannel(e.Video, e.ChannelIndex).AddClip(e.Clip);
				pasted.Add(e.Clip);
			}

			// linked when copied, linked when pasted
			foreach (IGrouping<Guid?, ClipsItem.Entry> group in entries.Where(e => e.LinkGroup is not null).GroupBy(e => e.LinkGroup))
			{
				if (group.Count() > 1) UITimeline.Timeline.Link(group.Select(e => e.Clip));
			}

			change.Commit();
		}

		Reconcile();
		SelectClips(pasted);
	}

	Channel EnsureChannel(bool video, int index)
	{
		Timeline timeline = UITimeline.Timeline;

		if (video)
		{
			while (timeline.VideoChannels.Count <= index) timeline.AddChannel(new VideoChannel());
			return timeline.VideoChannels[index];
		}

		while (timeline.AudioChannels.Count <= index) timeline.AddChannel(new AudioChannel());
		return timeline.AudioChannels[index];
	}

	// cuts at the playhead: the selected clips that span it, or every clip
	// that spans it when nothing is selected or `everything` is asked for.
	// the selection follows the clips it was on: a selected clip that was
	// split is replaced by its pieces, the rest stay selected. with nothing
	// selected, nothing ends up selected
	public void SplitAtPlayhead(bool everything)
	{
		TimeSpan at = UITimeline.PlayheadTime;
		Timeline timeline = UITimeline.Timeline;

		IEnumerable<Clip> candidates = everything || Selection.Count == 0
			? timeline.Channels.SelectMany(c => c.Clips)
			: Selection.Select(s => s.Clip);

		List<Clip> spanning = [.. candidates.Where(c => c.Start < at && c.End > at).Distinct()];
		if (spanning.Count == 0) return;

		HashSet<Clip> before = [.. timeline.Channels.SelectMany(c => c.Clips)];

		// where the selected clips were, so their pieces can be found afterwards
		List<Clip> selected = [.. Selection.Select(s => s.Clip)];
		List<(Channel channel, TimeSpan start, TimeSpan end)> selectedSpans = [.. selected.Select(c => (c.Channel, c.Start, c.End))];

		using (Transaction.Scope change = UITimeline.History.Begin(spanning.Count == 1 ? "Split clip" : $"Split {spanning.Count} clips"))
		{
			HashSet<Guid> groups = [];

			foreach (Clip clip in spanning)
			{
				// a linked group splits as one, whichever of its members were picked
				if (clip.LinkGroupId is Guid id)
				{
					if (groups.Add(id)) timeline.GetLinkGroup(id)?.Split(at);
				}
				else clip.Split(at);
			}

			change.Commit();
		}

		Reconcile();

		// the selection: whatever was selected and survived, plus the pieces
		// of whatever was selected and split
		IEnumerable<Clip> pieces = timeline.Channels.SelectMany(c => c.Clips)
			.Where(c => !before.Contains(c))
			.Where(c => selectedSpans.Any(s => ReferenceEquals(s.channel, c.Channel) && c.Start >= s.start && c.End <= s.end));

		SelectClips(selected.Where(c => c.Channel is not null).Concat(pieces));
	}

	public enum RippleScope
	{
		// the clips go, the gaps stay
		None,
		// each clip's own channel closes the gap it left
		OwnChannels,
		// the clips' time ranges come out of every channel, so the whole
		// timeline shortens and nothing drifts out of sync
		AllChannels
	}

	public void DeleteSelection(RippleScope ripple, string description = null)
	{
		if (Selection.Count == 0) return;

		List<Clip> clips = [.. Selection.Select(s => s.Clip)];
		Timeline timeline = UITimeline.Timeline;
		description ??= ripple == RippleScope.None ? "Delete" : "Ripple delete";

		using (Transaction.Scope change = UITimeline.History.Begin(clips.Count == 1 ? $"{description} clip" : $"{description} {clips.Count} clips"))
		{
			switch (ripple)
			{
				case RippleScope.None:
					foreach (Clip clip in clips) clip.Delete();
					break;

				case RippleScope.OwnChannels:
					// latest first, so closing one gap never moves a clip still
					// waiting its turn
					foreach (Clip clip in clips.OrderByDescending(c => c.Start)) clip.RippleDelete();
					break;

				case RippleScope.AllChannels:
					foreach ((TimeSpan start, TimeSpan end) in MergeRanges(clips).OrderByDescending(r => r.start))
						timeline.RippleRemoveRange(start, end);
					break;
			}

			change.Commit();
		}

		Selection.Clear();
		Reconcile();
	}

	// the clips' spans, with any that touch or overlap joined into one
	static List<(TimeSpan start, TimeSpan end)> MergeRanges(IEnumerable<Clip> clips)
	{
		List<(TimeSpan start, TimeSpan end)> merged = [];

		foreach (Clip c in clips.OrderBy(c => c.Start))
		{
			if (merged.Count > 0 && c.Start <= merged[^1].end)
				merged[^1] = (merged[^1].start, c.End > merged[^1].end ? c.End : merged[^1].end);
			else
				merged.Add((c.Start, c.End));
		}

		return merged;
	}

	void SelectClips(IEnumerable<Clip> clips)
	{
		HashSet<Clip> set = [.. clips];

		Selection.Select(UIClips.Where(u => set.Contains(u.Clip)), SelectionMode.Exclusive);
		UpdateSelection();
	}

    public override void _GuiInput(InputEvent _)
	{
		MouseButtonState left = InputManager.Singleton.Mouse.LeftButton;
		MouseButtonState middle = InputManager.Singleton.Mouse.MiddleButton;

        switch (left.Action)
        {
            case MouseAction.Press:
            case MouseAction.DoubleClick:
                if (!left.Capture(this)) break;

                ClaimKeyboard();
                MarkTarget(GetChannelAtPoint(InputManager.Singleton.Mouse.CurrentPosition));

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

			if (mods.Alt)
			{
				UITimeline.ZoomTime(steps, cursor.X);
				AcceptEvent();
			}
			else if (mods.Control)
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

		// the clips may have moved, and the seams sit on them
		UpdateEditPoints();

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
		if (c == handleClip) handleClip = null;
		c.QueueFree();
	}

	// ---- which selected clip wears the handles ----

	// with several clips selected, only the last one the cursor passed over
	// shows its drag handles - a selection five channels tall would
	// otherwise be a thicket of them
	UIClip handleClip;

	public void HoverClip(UIClip clip)
	{
		if (!clip.Selected || clip == handleClip) return;

		SetHandleClip(clip);
	}

	void SetHandleClip(UIClip clip)
	{
		UIClip previous = handleClip;
		handleClip = clip;

		previous?.SetHandlesEnabled(false);
		clip?.SetHandlesEnabled(true);

		// its handles hang over its neighbours, so it goes last in the tree to
		// win the hit test - but never above the seams, which straddle its
		// own edges and have to stay on top of it
		if (clip is null) return;

		clipsControl.MoveChild(clip, -1);
		foreach (UIEditPoint p in editPoints) clipsControl.MoveChild(p, -1);
	}

	// the data moved without this view seeing a drag - an undo or redo - so
	// bring every gui back in line with it: clips that are no longer on the
	// timeline go, clips that came back get a gui, and the rest re-place
	public void Reconcile()
	{
		List<UIClip> gone = [.. UIClips.Where(c => c.Clip.Channel is null || c.Clip.Channel.Timeline != UITimeline.Timeline)];

		foreach (UIClip c in gone)
		{
			Selection.Remove(c);
			RemoveUIClip(c);
		}

		Refresh();
		UpdateSelection();
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

			// a selected clip's drag handles hang outside its rect, over
			// whatever neighbour sits there. godot picks the last child first,
			// so the selection goes to the back of the tree to win that
			if (c.Selected) clipsControl.MoveChild(c, -1);
		}

		// the handles stay with the clip that had them if it is still
		// selected; otherwise the clip just clicked, or the only one there is
		if (handleClip is null || !Selection.Contains(handleClip))
		{
			SetHandleClip(lastClicked is not null && Selection.Contains(lastClicked) ? lastClicked
				: Selection.Count == 1 ? Selection[0]
				: null);
		}

		UpdateEditPoints();
	}

	// ---- edit points: the seam between two selected clips that touch ----

	readonly List<UIEditPoint> editPoints = [];

	// how wide the strip over a seam is, in pixels
	const float EDIT_POINT_WIDTH = 12f;

	// pairs of selected clips that meet end to start on the same channel
	IEnumerable<(UIClip before, UIClip after)> Seams()
	{
		foreach (UIClip a in Selection)
		{
			if (a.Clip.Channel is null) continue;

			foreach (UIClip b in Selection)
			{
				if (ReferenceEquals(a, b) || !ReferenceEquals(a.Clip.Channel, b.Clip.Channel)) continue;
				if (a.Clip.End == b.Clip.Start) yield return (a, b);
			}
		}
	}

	// rebuild the seams from the selection. each hides the handles either
	// side of it and puts a roll handle over the join instead
	void UpdateEditPoints()
	{
		foreach (UIEditPoint p in editPoints)
		{
			clipsControl.RemoveChild(p);
			p.QueueFree();
		}

		editPoints.Clear();

		HashSet<UIClip> seamAtEnd = [];
		HashSet<UIClip> seamAtStart = [];

		foreach ((UIClip before, UIClip after) in Seams())
		{
			seamAtEnd.Add(before);
			seamAtStart.Add(after);

			UIEditPoint point = new() { Before = before, After = after };

			point.DragStarted += (_, _) => BeginRollDrag(point);
			point.DragEnded += (_, _) => FinishEdgeDrag(point);
			point.DragCancelled += (_, _) => CancelEdgeDrag(point);

			// after every clip, so it wins the hit test over the two it straddles
			clipsControl.AddChild(point);
			editPoints.Add(point);
		}

		foreach (UIClip c in UIClips) c.SetSeams(seamAtStart.Contains(c), seamAtEnd.Contains(c));

		LayoutEditPoints();
	}

	void LayoutEditPoints()
	{
		foreach (UIEditPoint p in editPoints)
		{
			float x = (float)UITimeline.TimeSpanToPixels(p.After.Clip.Start);

			p.Position = new(x - EDIT_POINT_WIDTH / 2f, p.After.Position.Y);
			p.Size = new(EDIT_POINT_WIDTH, p.After.Size.Y);
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
		// every clip keeps its inner controls inside the part of it that is on
		// screen, which moves whenever the view scrolls or the clip does
		Vector2 window = UITimeline.ViewHorizontalRange;
		foreach (UIClip c in UIClips) c.KeepControlsInView(window.X, window.Y);

		if (edgeDrag is not null)
		{
			ProcessEdgeDrag(delta);
			return;
		}

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
		UITimeline.SnapLine = null;
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

		// what the content actually needs now, against what it is being held at.
		// the padding is part of what it needs - the fit adds it back the moment
		// it is released, and leaving it out here would slide the view that
		// much short of where the content really settles
		Vector2 natural = Vector2.Zero;
		foreach (UIClip c in UIClips)
		{
			natural = new(Mathf.Max(natural.X, c.GetRect().End.X), Mathf.Max(natural.Y, c.GetRect().End.Y));
		}

		natural += ClipsBounds.Padding;

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

		// the seams sit on clips that are about to move; the refresh at the
		// end of the drag puts them back
		foreach (UIEditPoint p in editPoints) p.Visible = false;

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

		// one entry in the history for the whole drop: the channels it opens
		// and every clip it moves
		using (Transaction.Scope change = UITimeline.History.Begin(Selection.Count == 1 ? "Move clip" : $"Move {Selection.Count} clips"))
		{
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

		change.Commit();
		}

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

		TimeSpan timeDelta = UITimeline.PixelsToTimeSpan(drag.delta.X);

		// magnet: the grabbed clip's start is what lands on the frame grid,
		// and every edge in the selection can catch on another clip or the
		// playhead. the selection's own edges are not targets
		timeDelta = UITimeline.SnapDelta(
			timeDelta,
			Selection.SelectMany(c => new[] { c.Clip.Start, c.Clip.End }),
			uiClip.Clip.Start,
			Selection.Select(c => c.Clip),
			includePlayhead: true,
			out TimeSpan? lineAt
		);

		// don't let the selection move past zero
		if (Selection.EarliestPosition + timeDelta < TimeSpan.Zero)
		{
			// reign it back in
			timeDelta = -Selection.EarliestPosition;
			lineAt = null;
		}

		UITimeline.SnapLine = lineAt;

		return (timeDelta, channelDragDelta);
	}

	// ---- edge drags: the handles hung off either side of a selected clip ----

	public enum EdgeDragKind
	{
		// move the edge over the content: extend or trim
		Extend,
		// move the edge and stretch the content to fit: retime
		Timeshift,
		// move the join between two clips: one grows by what the other gives up
		Roll
	}

	sealed class EdgeDrag
	{
		public UIDragHandle Handle;
		public EdgeDragKind Kind;
		public bool Left;

		// every selected clip whose edge sits where the grabbed one does. they
		// all move together, like a multi-edit trim, so linked video and audio
		// that start together stay together. for a roll, this is the clip
		// after the join, whose start moves
		public List<UIClip> Clips;

		// a roll's clip before the join, whose end moves
		public UIClip Before;

		// where the edge was when grabbed, and where it currently previews
		public TimeSpan EdgeAtStart;
		public TimeSpan Edge;

		public Vector2 ScrollAtStart;
	}

	EdgeDrag edgeDrag;

	public void BeginEdgeDrag(UIClip uiClip, UIDragHandle handle, EdgeDragKind kind)
	{
		// one drag at a time. a clip drag holds the same button, so this
		// cannot overlap one either
		if (edgeDrag is not null || dragClip is not null) return;

		UITimeline.FlushScrollEase();
		UITimeline.BeginViewScroll();

		// hold the content open for the duration - see BeginDrag
		if (ClipsBounds is not null) ClipsBounds.GrowOnly = true;

		bool left = handle.Side == UIDragHandle.HandleSide.Left;
		TimeSpan edge = left ? uiClip.Clip.Start : uiClip.Clip.End;

		List<UIClip> clips = [.. Selection.Where(c => (left ? c.Clip.Start : c.Clip.End) == edge)];
		if (!clips.Contains(uiClip)) clips.Insert(0, uiClip);

		edgeDrag = new()
		{
			Handle = handle,
			Kind = kind,
			Left = left,
			Clips = clips,
			EdgeAtStart = edge,
			Edge = edge,
			ScrollAtStart = UITimeline.ViewScroll
		};
	}

	// the join between two touching clips grabbed: a roll. the edge is the
	// after clip's start, and the before clip's end goes wherever it goes
	public void BeginRollDrag(UIEditPoint point)
	{
		if (edgeDrag is not null || dragClip is not null) return;

		UITimeline.FlushScrollEase();
		UITimeline.BeginViewScroll();

		if (ClipsBounds is not null) ClipsBounds.GrowOnly = true;

		TimeSpan edge = point.After.Clip.Start;

		edgeDrag = new()
		{
			Handle = point,
			Kind = EdgeDragKind.Roll,
			Left = true,
			Clips = [point.After],
			Before = point.Before,
			EdgeAtStart = edge,
			Edge = edge,
			ScrollAtStart = UITimeline.ViewScroll
		};
	}

	void ProcessEdgeDrag(double delta)
	{
		MouseButtonState left = InputManager.Singleton.Mouse.LeftButton;

		// belt and braces: if the gesture ended by any route that did not come
		// back through FinishEdgeDrag or CancelEdgeDrag, stop driving it
		if (!left.HasCapture(edgeDrag.Handle) || left.ClickState == MouseButtonClickState.Released)
		{
			CancelEdgeDrag(edgeDrag.Handle);
			return;
		}

		// sideways only - an edge has no channel to move to
		float push = UITimeline.GetEdgePush(InputManager.Singleton.Mouse.CurrentPosition).X;
		if (push != 0f) UITimeline.ScrollView(new(push * (float)delta, 0f));

		// re-apply every frame, not just on motion - the view can slide out
		// from under a stationary cursor and the edge has to follow it
		UpdateEdgeDrag(left);
	}

	void UpdateEdgeDrag(MouseButtonState left)
	{
		// the edge follows the cursor in content space, so scrolling since the
		// drag began counts as extra travel
		float travel = InputManager.Singleton.Mouse.GetDragDelta(left).X + (UITimeline.ViewScroll.X - edgeDrag.ScrollAtStart.X);
		TimeSpan candidate = edgeDrag.EdgeAtStart + UITimeline.PixelsToTimeSpan(travel);

		// magnet first, then what the clips themselves allow. a snap the clip
		// cannot reach is not a snap, so the line goes if the clamp moved it
		IEnumerable<Clip> moving = edgeDrag.Clips.Select(c => c.Clip);
		if (edgeDrag.Before is not null) moving = moving.Append(edgeDrag.Before.Clip);

		candidate = UITimeline.SnapPoint(candidate, moving, includePlayhead: true, out TimeSpan? lineAt);
		TimeSpan edge = ClampEdge(candidate);

		UITimeline.SnapLine = edge == candidate ? lineAt : null;

		edgeDrag.Edge = edge;

		// the other side of a roll follows the same edge
		edgeDrag.Before?.PreviewSpan(edgeDrag.Before.Clip.Start, edge);

		foreach (UIClip c in edgeDrag.Clips)
		{
			if (edgeDrag.Left) c.PreviewSpan(edge, c.Clip.End);
			else c.PreviewSpan(c.Clip.Start, edge);

			// a timeshift is a speed change in the making - show the speed it
			// would land on, not the one it still has
			if (edgeDrag.Kind == EdgeDragKind.Timeshift)
			{
				TimeSpan duration = edgeDrag.Left ? c.Clip.End - edge : edge - c.Clip.Start;
				c.PreviewSpeed((double)c.Clip.ContentDuration.Ticks / duration.Ticks);
			}
		}
	}

	// the tightest range every clip in the drag can accept: nothing shorter
	// than the minimum duration, nothing before zero, and no extend past the
	// head of the content - a timeshift has no content limit, it stretches
	TimeSpan ClampEdge(TimeSpan edge)
	{
		// the clip before a roll's join cannot be squeezed to nothing either
		if (edgeDrag.Before is not null)
		{
			TimeSpan min = UITimeline.EarliestEndAfter(edgeDrag.Before.Clip.Start);
			if (edge < min) edge = min;
		}

		foreach (UIClip c in edgeDrag.Clips)
		{
			Clip clip = c.Clip;

			if (edgeDrag.Left)
			{
				TimeSpan min = TimeSpan.Zero;

				// a roll pulls the after clip's head over its content just like an extend
				if (edgeDrag.Kind is EdgeDragKind.Extend or EdgeDragKind.Roll)
				{
					TimeSpan limit = clip.HeadExtendLimit;
					if (limit != TimeSpan.MaxValue && clip.Start - limit > min) min = clip.Start - limit;
				}

				TimeSpan max = UITimeline.LatestStartBefore(clip.End);

				if (edge < min) edge = min;
				if (edge > max) edge = max;
			}
			else
			{
				TimeSpan min = UITimeline.EarliestEndAfter(clip.Start);

				if (edge < min) edge = min;
			}
		}

		return edge;
	}

	// when the user lets go of the handle they were dragging
	public void FinishEdgeDrag(UIDragHandle handle)
	{
		if (edgeDrag is null || edgeDrag.Handle != handle) return;

		EdgeDrag drag = edgeDrag;
		edgeDrag = null;
		UITimeline.SnapLine = null;

		// edit underlying clip data, as one history entry
		string what = drag.Kind switch { EdgeDragKind.Timeshift => "Retime", EdgeDragKind.Roll => "Roll", _ => "Trim" };
		using (Transaction.Scope change = UITimeline.History.Begin(drag.Clips.Count == 1 ? $"{what} clip" : $"{what} {drag.Clips.Count} clips"))
		{
			if (drag.Kind == EdgeDragKind.Roll) ApplyRoll(drag);
			else foreach (UIClip c in drag.Clips) ApplyEdge(c.Clip, drag);

			change.Commit();
		}

		// an extend overwrites whatever it grows over. clips that were eaten
		// whole are off their channel now; ones that were split are replaced
		// by fragments Refresh will pick up
		foreach (UIClip c in UIClips.Where(c => c.Clip.Channel is null).ToList()) RemoveUIClip(c);

		Refresh();
		SettleAfterDrag();
	}

	// a roll is one extend: the side that grows overwrites the other by
	// exactly the amount it moved, and the overwrite is what trims the other
	// side - in-point and keyframes included, the same as any trim
	static void ApplyRoll(EdgeDrag drag)
	{
		Clip before = drag.Before.Clip;
		Clip after = drag.Clips[0].Clip;
		TimeSpan delta = drag.Edge - after.Start;

		if (delta > TimeSpan.Zero) before.ExtendEnd(delta);
		else if (delta < TimeSpan.Zero) after.ExtendStart(-delta);
	}

	static void ApplyEdge(Clip clip, EdgeDrag drag)
	{
		TimeSpan delta = drag.Edge - (drag.Left ? clip.Start : clip.End);
		if (delta == TimeSpan.Zero) return;

		switch (drag.Kind, drag.Left)
		{
			case (EdgeDragKind.Extend, true):
				if (delta < TimeSpan.Zero) clip.ExtendStart(-delta);
				else clip.TrimStart(delta);
				break;

			case (EdgeDragKind.Extend, false):
				if (delta > TimeSpan.Zero) clip.ExtendEnd(delta);
				else clip.TrimEnd(-delta);
				break;

			case (EdgeDragKind.Timeshift, true):
				clip.StretchStart(-delta);
				break;

			case (EdgeDragKind.Timeshift, false):
				clip.StretchEnd(delta);
				break;
		}
	}

	// abandon an edge drag that will never get a release of its own. the
	// drag only ever moved the gui, so Refresh puts every clip back
	public void CancelEdgeDrag(UIDragHandle handle)
	{
		if (edgeDrag is null || edgeDrag.Handle != handle) return;

		edgeDrag = null;
		UITimeline.SnapLine = null;

		Refresh();
		SettleAfterDrag();
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
