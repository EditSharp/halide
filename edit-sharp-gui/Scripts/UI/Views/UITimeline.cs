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

	[ExportGroup("Controls")]

	[Export] UIPlayhead playhead;

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
				ruler.Update(value, ProjectManager.Singleton.CurrentProject.RenderSettings.Framerate);

				//
			}
			else throw new ArgumentOutOfRangeException(nameof(value), "Pixels per second must be greater than zero");
		}
	} = 100d;

	public double TimeSpanToPixels(TimeSpan t) => t.TotalSeconds * PixelsPerSecond;
	public TimeSpan PixelsToTimeSpan(double p) => TimeSpan.FromSeconds(p / PixelsPerSecond);


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
	}

	public void SetTimeline(Timeline t)
	{
		Timeline = t;

		RefreshChannelEdits();

		clipsView.UITimeline = this;
		clipsView.Refresh();

		// update ruler
		ruler.Update(PixelsPerSecond, ProjectManager.Singleton.CurrentProject.RenderSettings.Framerate);
	}

    public override void _Process(double delta)
    {
		// before the mirroring below, so the edits and ruler do not spend a frame
		// showing the pre-zoom scroll
		ApplyPendingAnchors();
		AdvanceScrollEase(delta);

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

	// the content-space point under a global position, worked out from the scroll
	// value rather than from where the content node currently sits.
	//
	// the two agree once the container has laid out, but the scroll can be moved
	// mid-frame and the node will not have followed yet - so reading the node
	// makes everything think the cursor jumped by however far the scroll went
	public Vector2 ToViewContent(Vector2 globalPosition)
		=> globalPosition - clipsViewContainer.GlobalPosition + ViewScroll;

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
