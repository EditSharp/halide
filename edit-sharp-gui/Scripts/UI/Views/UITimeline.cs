using EditSharp.Components;
using EditSharp.Components.Channels;
using EditSharp.Components.Clips;
using EditSharp.History;
using EditSharpGUI.Scripts.Input;
using EditSharpGUI.Scripts.UI.Thumbnails;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class UITimeline : Control
{
	[ExportGroup("Options")]

	[Export] OptionButton magnetLevel;
	[Export] Slider widthSlider;
	[Export] Slider heightSlider;

	// how close, in screen pixels, something being dragged has to get to a
	// clip edge or the playhead before the magnet catches it
	[Export] float snapDistance = 10f;

	[ExportGroup("Controls")]

	[Export] UIPlayhead playhead;
	[Export] Control snapLine;
	[Export] Control chasm;

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

	public Timeline Timeline;

	// where the clips get their frames from. handed over by whoever wires
	// this view up, before the timeline is set, since it owns a playback
	// of its own
	public ThumbnailCache Thumbnails { get; set; }

	// where audio clips get their waveforms from, handed over the same way
	public WaveformCache Waveforms { get; set; }

	// a clip's graph button was pressed
	public event Action<UIClip> GraphRequested;

	// files were dropped on the clips: the page imports them and places what came in
	public event Action<IReadOnlyList<string>, Vector2> FilesDropped;

	// media placed as linked clips, end to end from a time on a channel and its mirror
	public void PlaceMedia(IReadOnlyList<EditSharp.Components.Media.IMedia> media, Time at, bool video, int channelIndex)
		=> clipsView.PlaceMedia(media, at, video, channelIndex);

	// the same, at a point on screen
	public void PlaceMediaAt(IReadOnlyList<EditSharp.Components.Media.IMedia> media, Vector2 globalPosition)
		=> clipsView.PlaceMediaAt(media, globalPosition);

	// the clips selected in the view, as data, and a word when that changes
	public event EventHandler SelectionChanged;
	public IReadOnlyList<Clip> SelectedClips => clipsView.SelectedClips;

	void OnClipsSelectionChanged(object sender, EventArgs e) => SelectionChanged?.Invoke(this, EventArgs.Empty);

	void OnGraphRequested(UIClip clip) => GraphRequested?.Invoke(clip);

	void OnFilesDropped(IReadOnlyList<string> files, Vector2 at) => FilesDropped?.Invoke(files, at);

	List<UIChannelEdit> ChannelEdits = [];

	// placeholder rows for channels a drag is reaching for but that do not exist
	// yet. they carry detached Channel objects that are never handed to the
	// timeline - the drag only touches the project when it is dropped - and they
	// keep this column the same shape as the clips view while it is in progress
	readonly List<UIChannelEdit> phantomEdits = [];

	public void SetPhantomChannels(int above, int below)
	{
		// removed from the container before being freed. QueueFree alone leaves
		// them in the layout until the end of the frame, and for that frame this
		// column is a row taller than the clips it is supposed to line up with
		foreach (UIChannelEdit e in phantomEdits)
		{
			edits.RemoveChild(e);
			e.QueueFree();
		}

		phantomEdits.Clear();

		// video channels are listed top down, so the new ones go on the front
		for (int i = 0; i < above; i++) AddPhantomEdit(new VideoChannel { Name = "New Video Channel" }, 0);
		for (int i = 0; i < below; i++) AddPhantomEdit(new AudioChannel { Name = "New Audio Channel" }, -1);
	}

	void AddPhantomEdit(Channel channel, int index)
	{
		UIChannelEdit edit = channelEditScene.Instantiate() as UIChannelEdit;

		edit.Channel = channel;
		edit.UITimeline = this;

		edits.AddChild(edit);
		edits.MoveChild(edit, index);

		phantomEdits.Add(edit);
	}

	public double VerticalScale { 
		get; 
		set
		{
			if (value > 0d)
			{
				// store before refreshing - both refreshes lay out against this property
				field = value;

				// update channel edits
				RefreshChannelEdits();

				// update clips view
				clipsView.Refresh();
			}
			else throw new ArgumentOutOfRangeException(nameof(value), "Vertical scale must be greater than zero");
		}
	} = 90d;

	public double PixelsPerSecond { 
		get; 
		set
		{
			if (value > 0d)
			{
				// store before refreshing - the clips view lays out against this property
				field = value;

				// update clips view
				clipsView.Refresh();

				// update ruler
				ruler.Update(value, Framerate);

				//
			}
			else throw new ArgumentOutOfRangeException(nameof(value), "Pixels per second must be greater than zero");
		}
	} = 100d;

	public double TimeSpanToPixels(Time t) => t.Seconds * PixelsPerSecond;
	public Time PixelsToTimeSpan(double p) => Time.FromSeconds(p / PixelsPerSecond);


	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// add event listeners.
		// both capture their anchor before overwriting the scale it was measured
		// against, then hand it to ApplyPendingAnchors to re-centre the view
		heightSlider.ValueChanged += h =>
		{
			float offset = channelAnchorOverride ?? clipsViewContainer.Size.Y / 2f;
			double channels = (clipsViewContainer.ScrollVertical + offset) / VerticalScale;

			VerticalScale = h;

			pendingChannelAnchor = (channels, offset);
			anchorFrames = ANCHOR_FRAMES;
		};

		widthSlider.ValueChanged += w =>
		{
			float offset = timeAnchorOverride ?? clipsViewContainer.Size.X / 2f;
			double seconds = (clipsViewContainer.ScrollHorizontal + offset) / PixelsPerSecond;

			PixelsPerSecond = w;

			pendingTimeAnchor = (seconds, offset);
			anchorFrames = ANCHOR_FRAMES;
		};

		// grabbing the playhead keeps it however far from the cursor it was
		// grabbed, so it does not jump under the hand. the ruler summons it to
		// the cursor instead, and from then on the two drags are the same drag
		playhead.Pressed += (_, _) => BeginPlayheadDrag(playhead, PlayheadTime - CursorTime);
		playhead.Released += (_, _) => EndPlayheadDrag();

		ruler.Pressed += (_, _) => BeginPlayheadDrag(ruler, Time.Zero);
		ruler.Released += (_, _) => EndPlayheadDrag();

		ApplyThemeColors();
	}

	// the few colours drawn by hand here come from the theme like the rest
	void ApplyThemeColors()
	{
		if (snapLine is ColorRect line) line.Color = GetThemeColor("snap_line", "Timeline");
	}

	public override void _Notification(int what)
	{
		if (what == NotificationThemeChanged && IsNodeReady()) ApplyThemeColors();
	}

	// ---- keyboard ----

	// the shortcuts that belong to the timeline as a whole. a key climbs here
	// from anything inside - the clips view, a clip, the ruler, even the
	// option buttons along the top - so this is the one place to answer them
	public override void _EnterTree() => InputManager.Singleton.Keyboard.Register(this, OnShortcut);

	public override void _ExitTree() => InputManager.Singleton.Keyboard.Unregister(this);

	void OnShortcut(ShortcutEventArgs e)
	{
		switch (e.Action)
		{
			case Shortcuts.SelectAll:
				clipsView.SelectAll();
				e.Handled = true;
				break;

			case Shortcuts.Copy:
				clipsView.CopySelection();
				e.Handled = true;
				break;

			case Shortcuts.Deselect:
				clipsView.SelectClips([]);
				e.Handled = true;
				break;

			case Shortcuts.ZoomIn or Shortcuts.ZoomOut:
				ZoomStep(e.Action == Shortcuts.ZoomIn);
				e.Handled = true;
				break;

			case Shortcuts.ZoomFit:
				ZoomToFit();
				e.Handled = true;
				break;
		}

		// the rest change the data. not mid-gesture: a drag in flight is
		// positioned from clips that could vanish under it
		if (e.Handled || MouseDragging) return;

		switch (e.Action)
		{
			case Shortcuts.Cut:
				clipsView.CutSelection();
				e.Handled = true;
				break;

			case Shortcuts.Paste:
				clipsView.Paste();
				e.Handled = true;
				break;

			case Shortcuts.Duplicate:
				clipsView.DuplicateSelection();
				e.Handled = true;
				break;

			case Shortcuts.Delete:
				clipsView.DeleteSelection(UIClipsView.RippleScope.None);
				e.Handled = true;
				break;

			case Shortcuts.RippleDelete:
				clipsView.DeleteSelection(UIClipsView.RippleScope.OwnChannels);
				e.Handled = true;
				break;

			case Shortcuts.RippleDeleteAll:
				clipsView.DeleteSelection(UIClipsView.RippleScope.AllChannels);
				e.Handled = true;
				break;

			case Shortcuts.Split:
				clipsView.SplitAtPlayhead(everything: false);
				e.Handled = true;
				break;

			case Shortcuts.SplitAll:
				clipsView.SplitAtPlayhead(everything: true);
				e.Handled = true;
				break;
		}
	}

	static bool MouseDragging => InputManager.Singleton.Mouse.LeftButton.ClickState == MouseButtonClickState.Dragging;

	// ---- playhead ----

	// where the playhead sits, in timeline time. setting this only moves the
	// line: playback hears about the playhead through PlayheadDrag, never
	// through here, so whatever drives playback can write this every frame
	// without hearing its own echo back
	public Time PlayheadTime
	{
		get;
		set => field = value < Time.Zero ? Time.Zero : value;
	}

	// the user took hold of the playhead, moved it, and let go. a click on the
	// ruler is a zero-length drag: started, one move, ended. PlayheadDrag only
	// fires when the time actually changed
	public event EventHandler PlayheadDragStarted;
	public event EventHandler<Time> PlayheadDrag;
	public event EventHandler PlayheadDragEnded;

	// the drag in progress: which control holds the press, and how far the
	// playhead sat from the cursor when it was grabbed
	Node playheadCaptor;
	Time? playheadGrabOffset;

	Time CursorTime => PixelsToTimeSpan(ToViewContent(InputManager.Singleton.Mouse.CurrentPosition).X);

	void BeginPlayheadDrag(Node captor, Time grabOffset)
	{
		if (playheadGrabOffset is not null) return;

		// a click here is a click on the timeline, as far as the keyboard goes
		InputManager.Singleton.Keyboard.Capture(this);

		playheadCaptor = captor;
		playheadGrabOffset = grabOffset;

		FlushScrollEase();
		BeginViewScroll();

		PlayheadDragStarted?.Invoke(this, EventArgs.Empty);

		// land it now rather than a frame from now - a ruler click should
		// summon the playhead the moment the button goes down
		DragPlayheadTo(CursorTime + grabOffset);
	}

	void EndPlayheadDrag()
	{
		if (playheadGrabOffset is null) return;

		playheadGrabOffset = null;
		playheadCaptor = null;
		SnapLine = null;

		PlayheadDragEnded?.Invoke(this, EventArgs.Empty);
	}

	// magnet, clamp, move, and tell whoever is listening - but only if it moved.
	// no snap line here: the playhead is the line, so it would only draw over itself
	void DragPlayheadTo(Time candidate)
	{
		Time time = SnapPoint(candidate, [], includePlayhead: false, out _);

		if (time < Time.Zero) time = Time.Zero;

		if (time == PlayheadTime) return;

		PlayheadTime = time;
		PlayheadDrag?.Invoke(this, time);
	}

	// runs every frame of a drag, not just on motion. the view can scroll out
	// from under a stationary cursor - from the edge scroll here, or from the
	// user scrolling mid-drag - and the playhead has to stay with the cursor
	void UpdatePlayheadDrag(double delta)
	{
		if (playheadGrabOffset is not Time offset) return;

		MouseButtonState left = InputManager.Singleton.Mouse.LeftButton;

		// belt and braces: if the press ended by any route that never came
		// back through Released, stop driving it
		if (!left.HasCapture(playheadCaptor) || left.ClickState == MouseButtonClickState.Released)
		{
			EndPlayheadDrag();
			return;
		}

		// sideways only - the playhead has no vertical position to chase
		float push = GetEdgePush(InputManager.Singleton.Mouse.CurrentPosition).X;
		if (push != 0f) ScrollView(new(push * (float)delta, 0f));

		DragPlayheadTo(CursorTime + offset);
	}

	// ---- magnet ----

	public enum MagnetMode
	{
		// free movement, between frames if you like
		Off = 0,
		// everything lands on a frame boundary
		Framerate = 1,
		// frames, plus catching on any clip edge or the playhead
		All = 2
	}

	// the option button's item ids are the enum values. nothing selected
	// reads as -1, which is nothing to snap to
	public MagnetMode Magnet => magnetLevel.Selected < 0 ? MagnetMode.Off : (MagnetMode)magnetLevel.GetSelectedId();

	Rational Framerate => ProjectSession.Of(this)?.Project.RenderSettings.Framerate ?? 30;

	// the one place a frame number becomes a time, so every grid point is
	// rounded the same way and edges that should line up compare equal
	Time FrameToTime(long frame) => Time.FromFrame(frame, Framerate);

	// the nearest frame boundary, or t untouched when the magnet is off
	public Time SnapToFrame(Time t)
	{
		if (Magnet == MagnetMode.Off) return t;

		return FrameToTime(t.ToFrame(Framerate, Rounding.Nearest));
	}

	// the shortest a clip may be, as an edge position. under the magnet that
	// is one whole frame on the grid; with the magnet off it is the data
	// model's floor of one tick
	public Time EarliestEndAfter(Time start)
	{
		if (Magnet == MagnetMode.Off) return start + Clip.MinimumDuration;

		// the first grid point strictly after start
		return FrameToTime(start.ToFrame(Framerate, Rounding.Floor) + 1);
	}

	public Time LatestStartBefore(Time end)
	{
		if (Magnet == MagnetMode.Off) return end - Clip.MinimumDuration;

		// the last grid point strictly before end
		return FrameToTime(end.ToFrame(Framerate, Rounding.Ceiling) - 1);
	}

	// snap one moving point. exclude is the clips whose own edges must not
	// count as targets - the ones being dragged
	public Time SnapPoint(Time candidate, IEnumerable<Clip> exclude, bool includePlayhead, out Time? lineAt)
		=> candidate + SnapDelta(Time.Zero, [candidate], candidate, exclude, includePlayhead, out lineAt);

	// snap a set of points that all move together by delta. anchor is the one
	// point that lands on the frame grid - the rest keep their spacing from
	// it - and after that every point can catch on a clip edge or the
	// playhead, with the closest catch winning for all of them.
	//
	// lineAt is where the snap line belongs, or null when nothing caught or
	// the catch was the playhead, which is already a line
	public Time SnapDelta(Time delta, IEnumerable<Time> points, Time anchor, IEnumerable<Clip> exclude, bool includePlayhead, out Time? lineAt)
	{
		lineAt = null;

		if (Magnet == MagnetMode.Off) return delta;

		delta = SnapToFrame(anchor + delta) - anchor;

		if (Magnet != MagnetMode.All) return delta;

		Time threshold = PixelsToTimeSpan(snapDistance);
		Time[] moving = [.. points];
		HashSet<Clip> excluded = [.. exclude];

		Time? bestAdjust = null;
		Time? bestLine = null;
		Time bestDistance = Time.MaxValue;

		foreach ((Time target, bool isClip) in SnapTargets(excluded, includePlayhead))
		{
			foreach (Time point in moving)
			{
				Time adjust = target - (point + delta);
				Time distance = adjust.Abs();

				if (distance > threshold || distance >= bestDistance) continue;

				bestDistance = distance;
				bestAdjust = adjust;
				bestLine = isClip ? target : null;
			}
		}

		if (bestAdjust is Time a)
		{
			delta += a;
			lineAt = bestLine;
		}

		return delta;
	}

	// every edge on every channel, plus the playhead when asked for
	IEnumerable<(Time time, bool isClip)> SnapTargets(HashSet<Clip> exclude, bool includePlayhead)
	{
		foreach (Channel channel in Timeline.Channels)
		{
			foreach (Clip clip in channel.Clips)
			{
				if (exclude.Contains(clip)) continue;

				yield return (clip.Start, true);
				yield return (clip.End, true);
			}
		}

		if (includePlayhead) yield return (PlayheadTime, false);
	}

	// where the snap line is drawn, in timeline time. null hides it. whoever
	// runs a drag sets this as it goes and clears it when the drag ends
	public Time? SnapLine { get; set; }

	// put the playhead and snap line over the view for the current scroll.
	// called at the end of _Process, and again by every scroll this class
	// makes, so a scroll from a child's _Process never leaves them a frame behind
	void LayoutOverlays()
	{
		float scroll = clipsViewContainer.ScrollHorizontal;

		playhead.Move((float)TimeSpanToPixels(PlayheadTime) - scroll);

		if (snapLine is null) return;

		snapLine.Visible = SnapLine.HasValue;

		if (SnapLine is Time t)
			snapLine.Position = new((float)TimeSpanToPixels(t) - scroll - snapLine.Size.X / 2f, snapLine.Position.Y);
	}

	public void SetTimeline(Timeline t)
	{
		Timeline = t;

		RefreshChannelEdits();

		clipsView.UITimeline = this;
		clipsView.SelectionChanged -= OnClipsSelectionChanged;
		clipsView.SelectionChanged += OnClipsSelectionChanged;
		clipsView.GraphRequested -= OnGraphRequested;
		clipsView.GraphRequested += OnGraphRequested;
		clipsView.FilesDropped -= OnFilesDropped;
		clipsView.FilesDropped += OnFilesDropped;
		clipsView.Refresh();

		// update ruler
		ruler.Update(PixelsPerSecond, Framerate);
	}

    public override void _Process(double delta)
    {
		// before the mirroring below, so the edits and ruler do not spend a frame
		// showing the pre-zoom scroll
		ApplyPendingAnchors();
		AdvanceScrollEase(delta);
		UpdatePlayheadDrag(delta);

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

		// spooky chasm to show that there is more on the left
		chasm.Visible = clipsViewContainer.ScrollHorizontal != 0;

		LayoutOverlays();
    }


	// the history every edit made through this timeline lands in
	public History History => ProjectSession.Of(this)?.Project.History;

	// ---- a channel dragged by its handle ----
	// it lifts and follows the pointer within its kind, its old slot becomes the gap
	// where it would land, and the move is one step on release; escape puts it back

	UIChannelEdit draggedEdit, liftedEdit;
	int dragRow, dragTarget;
	float dragGrabY;

	public bool DraggingChannel => draggedEdit is not null;

	// rows count down from the top: video channels top down, then audio
	int RowOf(Channel channel) => channel is VideoChannel ? Timeline.VideoChannels.Count - 1 - channel.Index : Timeline.VideoChannels.Count + channel.Index;

	(int First, int Last) RowsOfKind(Channel channel)
	{
		int video = Timeline.VideoChannels.Count;
		return channel is VideoChannel ? (0, video - 1) : (video, video + Timeline.AudioChannels.Count - 1);
	}

	public void BeginChannelDrag(UIChannelEdit edit, float globalY)
	{
		if (draggedEdit is not null || !ReferenceEquals(edit.Channel.Timeline, Timeline) || phantomEdits.Count > 0) return;

		draggedEdit = edit;
		dragRow = dragTarget = RowOf(edit.Channel);
		dragGrabY = globalY;

		liftedEdit = channelEditScene.Instantiate<UIChannelEdit>();
		liftedEdit.Channel = edit.Channel;
		liftedEdit.UITimeline = this;
		liftedEdit.Lifted = true;
		liftedEdit.TopLevel = true;
		liftedEdit.ZIndex = 100;
		AddChild(liftedEdit);

		// placed by hand, so its scene anchors must not size it
		liftedEdit.SetAnchorsPreset(LayoutPreset.TopLeft);

		edit.Modulate = Colors.Transparent;
		UpdateChannelDrag(globalY);
	}

	public void UpdateChannelDrag(float globalY)
	{
		if (draggedEdit is null) return;

		// either header gone from under the drag: it's over
		if (!IsInstanceValid(draggedEdit) || !IsInstanceValid(liftedEdit) || draggedEdit.GetParent() != edits)
		{
			CancelChannelDrag();
			return;
		}

		(int first, int last) = RowsOfKind(draggedEdit.Channel);
		float row = Mathf.Clamp(dragRow + (globalY - dragGrabY) / (float)VerticalScale, first, last);
		int target = Mathf.RoundToInt(row);

		if (target != dragTarget)
		{
			dragTarget = target;
			edits.MoveChild(draggedEdit, target);
		}

		liftedEdit.Size = new Vector2(edits.Size.X, (float)VerticalScale);
		liftedEdit.GlobalPosition = new Vector2(edits.GlobalPosition.X, edits.GlobalPosition.Y + row * (float)VerticalScale);
		clipsView.PreviewChannelMove(draggedEdit.Channel, dragRow, dragTarget, row);
	}

	public void FinishChannelDrag()
	{
		if (draggedEdit is null) return;

		Channel channel = draggedEdit.Channel;
		int from = dragRow, to = dragTarget;
		EndChannelDrag();
		if (from == to || !ReferenceEquals(channel.Timeline, Timeline)) return;

		// down the screen is a lower index for video and a higher one for audio
		bool down = to > from;
		using (Transaction.Scope change = History.Begin("Move channel"))
		{
			for (int i = 0; i < Math.Abs(to - from); i++)
			{
				if (channel is VideoChannel == down) channel.MoveDown();
				else channel.MoveUp();
			}
			change.Commit();
		}

		Reconcile();
	}

	public void CancelChannelDrag()
	{
		if (draggedEdit is not null) EndChannelDrag();
	}

	// the drag is over before anything is tidied, so nothing that fails below can leave it half open
	void EndChannelDrag()
	{
		UIChannelEdit dragged = draggedEdit, lifted = liftedEdit;
		draggedEdit = null;
		liftedEdit = null;

		clipsView.PreviewChannelMove(null, 0, 0, 0f);
		if (IsInstanceValid(lifted)) lifted.QueueFree();

		// back in its own slot; a move rebuilds the column from the model after
		if (IsInstanceValid(dragged) && !dragged.IsQueuedForDeletion())
		{
			dragged.Modulate = Colors.White;
			if (dragged.GetParent() == edits) edits.MoveChild(dragged, Math.Min(dragRow, edits.GetChildCount() - 1));
		}
	}

	public override void _Input(InputEvent e)
	{
		if (draggedEdit is not null && e is InputEventKey { Keycode: Key.Escape, Pressed: true })
		{
			CancelChannelDrag();
			GetViewport().SetInputAsHandled();
		}
	}

	// the data moved without this view seeing a drag - an undo or redo. the
	// channel list is rebuilt outright, since a channel can come or go
	// anywhere in it and the incremental refresh only ever appends
	public void Reconcile()
	{
		// the headers are about to be rebuilt; a drag holding one of them ends here
		CancelChannelDrag();

		foreach (UIChannelEdit e in ChannelEdits)
		{
			edits.RemoveChild(e);
			e.QueueFree();
		}

		ChannelEdits.Clear();
		RefreshChannelEdits();

		clipsView.Reconcile();
	}

	public int RefreshChannelEdits(bool all = false)
	{
		if (all)
		{
			//delete all channel edits
			foreach (UIChannelEdit c in ChannelEdits) c.QueueFree();
			ChannelEdits.Clear();
		}
		
		// find all missing channel edits and generate their guis
		int channels = 0;
		foreach (Channel c in Timeline.Channels)
		{
			// existing channel edit
			if (ChannelEdits.Any(ch => ReferenceEquals(ch.Channel, c)))
			{
				ChannelEdits.First(ch => ReferenceEquals(ch.Channel, c)).Refresh();
				continue;
			}

			// new channel edit
			UIChannelEdit edit = channelEditScene.Instantiate() as UIChannelEdit;
			edit.Channel = c;
			edit.UITimeline = this;
			edits.AddChild(edit);
			if (c is VideoChannel) edits.MoveChild(edit, 0);
			ChannelEdits.Add(edit);

			channels++;
		}

		return channels;
	}

	public Vector2 ViewScroll => new(clipsViewContainer.ScrollHorizontal, clipsViewContainer.ScrollVertical);
	public Vector2 ViewSize => clipsViewContainer.Size;

	// the exact global-space X a click lands on to be read back as `t` -- the inverse of ToViewContent's
	// X axis, against the same clipsViewContainer.GlobalPosition reference CursorTime uses. the ruler sits
	// in its own row above the clips and isn't guaranteed to share the container's global X, so a test (or
	// anything else) clicking a precise moment needs this rather than the ruler's own GlobalPosition
	public float GlobalXOfTime(Time t) => clipsViewContainer.GlobalPosition.X + (float)(TimeSpanToPixels(t) - ViewScroll.X);

	// the slice of content the view is actually showing, top and bottom, in
	// content pixels. taken from the scrollbar page rather than the container
	// size, so the strip a visible horizontal scrollbar covers is not counted as
	// somewhere a clip can be seen
	public Vector2 ViewVerticalRange
	{
		get
		{
			float top = clipsViewContainer.ScrollVertical;

			return new(top, top + (float)clipsViewContainer.GetVScrollBar().Page);
		}
	}

	// whether a global position is over the clips view at all
	public bool ViewContains(Vector2 globalPosition) => clipsViewContainer.GetGlobalRect().HasPoint(globalPosition);

	// the same sideways: the slice of content in view, left and right, in
	// content pixels
	public Vector2 ViewHorizontalRange
	{
		get
		{
			float left = clipsViewContainer.ScrollHorizontal;

			return new(left, left + (float)clipsViewContainer.GetHScrollBar().Page);
		}
	}

	// the content-space point under a global position, worked out from the scroll
	// value rather than from where the content node currently sits.
	//
	// the two agree once the container has laid out, but the scroll can be moved
	// mid-frame and the node will not have followed yet - so reading the node
	// makes everything think the cursor jumped by however far the scroll went
	public Vector2 ToViewContent(Vector2 globalPosition)
		=> globalPosition - clipsViewContainer.GlobalPosition + ViewScroll;

	// ---- remembered with the project ----

	// zoom, scroll, playhead and which clips were selected
	public System.Text.Json.Nodes.JsonObject SaveState() => new()
	{
		["pixelsPerSecond"] = PixelsPerSecond,
		["verticalScale"] = VerticalScale,
		["scrollX"] = ViewScroll.X,
		["scrollY"] = ViewScroll.Y,
		["playhead"] = PlayheadTime.Ticks,
		["selected"] = new System.Text.Json.Nodes.JsonArray([.. SelectedClips.Select(c => (System.Text.Json.Nodes.JsonNode)c.Id.ToString())]),
	};

	public void RestoreState(System.Text.Json.Nodes.JsonObject state)
	{
		if (state is null) return;

		if (state["pixelsPerSecond"]?.GetValue<double>() is double pps and > 0d) PixelsPerSecond = pps;
		if (state["verticalScale"]?.GetValue<double>() is double scale and > 0d) VerticalScale = scale;
		if (state["playhead"]?.GetValue<long>() is long ticks) PlayheadTime = new Time(ticks);

		HashSet<Guid> ids = [.. (state["selected"]?.AsArray() ?? []).Select(n => Guid.TryParse(n?.GetValue<string>(), out Guid id) ? id : Guid.Empty)];
		clipsView.SelectClips(clipsView.UIClips.Select(u => u.Clip).Where(c => ids.Contains(c.Id)));

		// once the view has laid out, so the scroll has room to land
		Vector2 scroll = new(state["scrollX"]?.GetValue<float>() ?? 0f, state["scrollY"]?.GetValue<float>() ?? 0f);
		Callable.From(() => SetViewScroll(scroll)).CallDeferred();
	}

	void SetViewScroll(Vector2 scroll)
	{
		clipsViewContainer.ScrollHorizontal = Mathf.RoundToInt(scroll.X);
		clipsViewContainer.ScrollVertical = Mathf.RoundToInt(scroll.Y);
	}

	// move the scroll now, with no easing and no sub-pixel carry. for when the
	// content has just been re-laid out underneath it and the two have to stay
	// in step inside the same frame - anything deferred shows as a jump
	public void ScrollViewNow(Vector2 delta)
	{
		clipsViewContainer.ScrollHorizontal += Mathf.RoundToInt(delta.X);
		clipsViewContainer.ScrollVertical += Mathf.RoundToInt(delta.Y);

		LayoutOverlays();
	}

	// a scroll position the view is sliding to rather than snapping to. letting
	// the content shrink at the end of a drag can move the scroll a long way in
	// one frame, which reads as the view teleporting somewhere new - a short
	// slide shows that it moved, and roughly where it went
	const float SCROLL_EASE_SECONDS = 0.11f;

	Vector2? scrollEaseTarget;
	Action scrollEaseArrived;

	// only the axes that actually have somewhere to go. those are driven every
	// frame, which locks the user out of them for the duration; the other axis
	// is never written, so scrolling across the animation still works
	bool easingX;
	bool easingY;

	public void EaseScrollTo(Vector2 target, Action onArrive = null)
	{
		Vector2 remaining = target - ViewScroll;

		easingX = Mathf.Abs(remaining.X) >= 1f;
		easingY = Mathf.Abs(remaining.Y) >= 1f;

		// nowhere to slide to, so there is nothing to wait for
		if (!easingX && !easingY)
		{
			onArrive?.Invoke();
			return;
		}

		scrollEaseTarget = target;
		scrollEaseArrived = onArrive;
	}

	public void CancelScrollEase()
	{
		scrollEaseTarget = null;
		scrollEaseArrived = null;
		easingX = false;
		easingY = false;
	}

	// stop easing and run the arrival callback anyway. that callback is what
	// hands back the phantom rows and the held-open content, so it has to happen
	// however the ease ends
	void FinishScrollEase()
	{
		Action arrived = scrollEaseArrived;
		CancelScrollEase();
		arrived?.Invoke();
	}

	// finish a slide still in flight right now, arrival and all. a new drag has
	// to start from a settled view, not halfway through the last one
	public void FlushScrollEase()
	{
		if (scrollEaseTarget is not null) FinishScrollEase();
	}

	void AdvanceScrollEase(double delta)
	{
		if (scrollEaseTarget is not Vector2 target) return;

		Vector2 scroll = ViewScroll;
		float rate = 1f - Mathf.Exp(-(float)delta / SCROLL_EASE_SECONDS);

		float stepX = easingX ? Step(target.X - scroll.X, ref easingX) : 0f;
		float stepY = easingY ? Step(target.Y - scroll.Y, ref easingY) : 0f;

		if (stepX != 0f || stepY != 0f)
		{
			ScrollView(new(stepX, stepY));

			// the scroll can refuse to move, when it is already against a clamp.
			// having asked for a whole pixel at least, standing still means it
			// cannot go any further - so stop driving at it
			Vector2 moved = ViewScroll;
			if (Mathf.IsEqualApprox(moved.X, scroll.X)) easingX = false;
			if (Mathf.IsEqualApprox(moved.Y, scroll.Y)) easingY = false;
		}

		if (easingX || easingY) return;

		FinishScrollEase();

		float Step(float remaining, ref bool easing)
		{
			if (Mathf.Abs(remaining) < 1f)
			{
				easing = false;
				return 0f;
			}

			// never ask for less than a whole pixel. the tail of an exponential
			// ease is all sub-pixel steps, and those round away to nothing - the
			// slide stalls a few pixels out and whatever comes next snaps the
			// rest of the way
			float step = remaining * rate;

			return Mathf.Abs(step) < 1f ? Mathf.Sign(remaining) : step;
		}
	}

	// where the scroll will end up once the content stops being held open, given
	// how much it is about to shrink by and how far its contents are about to
	// slide. asks the scroll bars what they allow today rather than re-deriving
	// it from the content - that way panel margins and the space a visible
	// scrollbar takes are already accounted for, instead of being guessed at and
	// rebounding an axis that never moved
	public Vector2 GetSettledScroll(Vector2 shrink, float floorY)
	{
		HScrollBar h = clipsViewContainer.GetHScrollBar();
		VScrollBar v = clipsViewContainer.GetVScrollBar();

		float maxX = Mathf.Max(0f, (float)(h.MaxValue - h.Page - shrink.X));

		// floorY is the phantom rows still standing. they come out at the end and
		// take the scroll down with them, so it must not slide below what that
		// still needs to be possible
		float maxY = Mathf.Max(floorY, (float)(v.MaxValue - v.Page - shrink.Y));

		Vector2 scroll = ViewScroll;

		return new(Mathf.Clamp(scroll.X, 0f, maxX), Mathf.Clamp(scroll.Y, floorY, maxY));
	}

	// grow the scrollable area and move the scroll by the same amount in one go.
	// the scroll container only recomputes its limits when it next lays out, so
	// raising the scroll on its own clamps against the old maximum and silently
	// drops the difference. writing the maximum first makes the pair atomic: the
	// content really has grown by this much, so the value survives the next
	// layout, and nothing on screen appears to move
	public void ReserveViewTop(float height)
	{
		VScrollBar v = clipsViewContainer.GetVScrollBar();

		v.MaxValue += height;
		clipsViewContainer.ScrollVertical += Mathf.RoundToInt(height);
	}

	// the scroll offsets are whole pixels, but the things that drive them are
	// not - a drag delta becomes fractional once the window is scaled up past
	// the base 1920x1080, and an edge scroll is pixels-per-second times a frame
	// time. truncating every call would drop that fraction each time and the
	// view would crawl, so carry the remainder into the next one
	Vector2 scrollRemainder;

	public void BeginViewScroll() => scrollRemainder = Vector2.Zero;

	public void ScrollView(Vector2 delta)
	{
		Vector2 total = delta + scrollRemainder;

		int x = (int)total.X;
		int y = (int)total.Y;

		scrollRemainder = total - new Vector2(x, y);

		clipsViewContainer.ScrollHorizontal += x;
		clipsViewContainer.ScrollVertical += y;

		LayoutOverlays();
	}

	// dragging the view moves the content with the cursor, so the scroll goes
	// the other way
	public void DragView(Vector2 delta) => ScrollView(-delta);

	// how hard the view should scroll while something is dragged against its
	// edge, in pixels per second. zero while the cursor is comfortably inside
	const float EDGE_SCROLL_MARGIN = 48f;
	const float EDGE_SCROLL_SPEED = 900f;
	const float EDGE_SCROLL_MAX_RAMP = 3f;

	public Vector2 GetEdgePush(Vector2 globalPosition)
	{
		Rect2 view = clipsViewContainer.GetGlobalRect();

		return new(
			Axis(globalPosition.X, view.Position.X, view.End.X),
			Axis(globalPosition.Y, view.Position.Y, view.End.Y)
		);

		static float Axis(float p, float min, float max)
		{
			if (p < min + EDGE_SCROLL_MARGIN) return -Ramp(min + EDGE_SCROLL_MARGIN - p);
			if (p > max - EDGE_SCROLL_MARGIN) return Ramp(p - (max - EDGE_SCROLL_MARGIN));

			return 0f;
		}

		// ramps in across the margin, then keeps climbing once the cursor is
		// past the edge, so dragging further out scrolls faster
		static float Ramp(float depth)
			=> Mathf.Min(depth / EDGE_SCROLL_MARGIN, EDGE_SCROLL_MAX_RAMP) * EDGE_SCROLL_SPEED;
	}

	// zoom is anchored: whatever sits under the anchor should still sit under it
	// afterwards. the wheel passes the cursor, the sliders pass nothing and get
	// the middle of the visible area instead.
	//
	// these are set only for the duration of the slider assignment below, which
	// raises ValueChanged synchronously - that handler is the single place the
	// anchor is captured, so dragging the slider by hand is anchored too
	float? timeAnchorOverride;
	float? channelAnchorOverride;

	// zoom runs through the sliders rather than the properties directly, so the
	// sliders stay in step with the view and their ranges do the clamping.
	// PixelsPerSecond and VerticalScale both throw if pushed to zero, and a
	// wheel has nothing stopping it from getting there
	public void ZoomTime(double steps, float globalAnchorX)
	{
		timeAnchorOverride = globalAnchorX - clipsViewContainer.GlobalPosition.X;
		widthSlider.Value -= steps;
		timeAnchorOverride = null;
	}

	// a quarter in or out, around the playhead when it's in view and the middle otherwise
	public void ZoomStep(bool zoomIn)
	{
		float playhead = (float)TimeSpanToPixels(PlayheadTime) - clipsViewContainer.ScrollHorizontal;
		timeAnchorOverride = playhead >= 0f && playhead <= clipsViewContainer.Size.X ? playhead : null;
		widthSlider.Value = widthSlider.Value * (zoomIn ? 1.25 : 0.8);
		timeAnchorOverride = null;
	}

	// the whole timeline across the view, from its start
	public void ZoomToFit()
	{
		double seconds = Math.Max(1d, Timeline?.Duration.Seconds ?? 0d);
		widthSlider.Value = clipsViewContainer.Size.X * 0.95 / seconds;
		pendingTimeAnchor = (0d, 0f);
		anchorFrames = ANCHOR_FRAMES;
	}

	public void SelectClips(IEnumerable<Clip> clips) => clipsView.SelectClips(clips);

	// a shortcut offered straight to the timeline, as if it had the keyboard; whether it took it
	public bool TakeShortcut(ShortcutEventArgs e)
	{
		OnShortcut(e);
		return e.Handled;
	}

	public void DuplicateSelection() => clipsView.DuplicateSelection();

	// whether the timeline has clips selected, for greying out what needs them
	public bool HasSelection => SelectedClips.Count > 0;

	public void ZoomChannelHeight(double steps, float globalAnchorY)
	{
		channelAnchorOverride = globalAnchorY - clipsViewContainer.GlobalPosition.Y;
		heightSlider.Value -= steps;
		channelAnchorOverride = null;
	}

	// what the anchor was looking at, in units that survive the zoom, plus where
	// in the visible area it was sitting
	(double seconds, float offset)? pendingTimeAnchor;
	(double channels, float offset)? pendingChannelAnchor;

	// FitToChildren only resizes the content in its own _Process, and the scroll
	// container clamps to a max that is stale until then - so zooming in cannot
	// land its scroll on the frame it happens. the anchor is held in content
	// units, which makes re-applying it idempotent, so just keep asking for a
	// few frames until the clamp catches up
	const int ANCHOR_FRAMES = 3;
	int anchorFrames;

	void ApplyPendingAnchors()
	{
		if (anchorFrames <= 0) return;

		anchorFrames--;

		if (pendingTimeAnchor is (double seconds, float xOffset))
			clipsViewContainer.ScrollHorizontal = (int)(seconds * PixelsPerSecond - xOffset);

		if (pendingChannelAnchor is (double channels, float yOffset))
			clipsViewContainer.ScrollVertical = (int)(channels * VerticalScale - yOffset);

		if (anchorFrames > 0) return;

		pendingTimeAnchor = null;
		pendingChannelAnchor = null;
	}

	
}
