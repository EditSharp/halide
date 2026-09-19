using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using EditSharp;
using EditSharp.Components.Clips;
using EditSharpGUI.Scripts.Input;
using static UIClipsView;
using EditSharpGUI.Scripts.UI;

public partial class UIClip : PanelContainer, IDragCancellable
{
	[ExportGroup("Controls")]

	[Export] Control content;

	// the box the inner controls live in. it is anchored across the whole
	// clip in the scene; at runtime its offsets are pulled in to whatever
	// part of the clip is actually on screen, so the controls stay in view
	// on a clip that runs off the edge - see KeepControlsInView
	[Export] Control controlsBox;

	[Export] Label clipName;
	[Export] Control speed;
	[Export] Label speedPercentage;

	// the inner controls, least important first. when the clip is too narrow
	// to hold them all, they are hidden from the front of this list until
	// what is left fits - and if not even the last one fits, all of them go.
	// drag handles are never in here: they live outside the clip on purpose
	[Export] Control[] widthPriority;

	// a control that expands to fill (the name label) reports no minimum
	// width of its own, so this is what it has to be given to be worth showing
	[Export] float minimumExpandingWidth = 40f;

	[Export] Panel thumbnail;
	[Export] Panel outline;

	[ExportSubgroup("Drag Handles")]

	[Export] UIDragHandle[] extends;
	[Export] UIDragHandle[] timeshifts;

	// the boxes the handles sit in, one off each side. shown only while the
	// clip is selected, and the start side not even then when the clip is
	// already at zero - there is nowhere for its start to go
	[Export] Control startControls;
	[Export] Control endControls;

	[ExportGroup("Styles")]

	[Export] StyleBoxFlat contentStyleBox;
	[Export] Color videoColor;
	[Export] Color audioColor;

	public Clip Clip;
	public UIClipsView ClipsView;

	public Color Color
	{
        get;
        set
		{
			StyleBoxFlat style = contentStyleBox.DuplicateDeep() as StyleBoxFlat;
			style.BgColor = value;
			content.AddThemeStyleboxOverride("panel", style);
			field = value;
		}
	}
	public bool Selected
	{
		get
		{
			return outline.Visible;
		}
		set
		{
			outline.Visible = value;
			UpdateHandles();
		}
	}

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// update gui based on provided clip
		clipName.Text = Clip.Name;

		if (Clip is VideoClip) Color = videoColor;
		else Color = audioColor;

		// the handles only report the life of a drag. the view runs it from the
		// cursor every frame, the same as a clip drag, so it edge-scrolls and
		// snaps like one
		foreach (UIDragHandle handle in extends) Wire(handle, EdgeDragKind.Extend);
		foreach (UIDragHandle handle in timeshifts) Wire(handle, EdgeDragKind.Timeshift);

		// the margins the scene gave the controls box are what it keeps from
		// the visible edges once it starts following the view
		if (controlsBox is not null)
		{
			insetLeft = controlsBox.OffsetLeft;
			insetRight = -controlsBox.OffsetRight;
		}

		// of a multi-selection, the clip under the cursor is the one that
		// wears the handles
		MouseEntered += () => ClipsView.HoverClip(this);

		Refresh();
		UpdateThumbnail();

		void Wire(UIDragHandle handle, EdgeDragKind kind)
		{
			handle.DragStarted += (_, _) => ClipsView.BeginEdgeDrag(this, handle, kind);
			handle.DragEnded += (_, _) => ClipsView.FinishEdgeDrag(handle);
			handle.DragCancelled += (_, _) => ClipsView.CancelEdgeDrag(handle);
		}
	}

	// a seam is where this clip meets another selected clip on its channel.
	// the handles on that side give way to the seam's own roll handle - see
	// UIEditPoint and the clips view
	bool seamAtStart;
	bool seamAtEnd;

	public void SetSeams(bool atStart, bool atEnd)
	{
		seamAtStart = atStart;
		seamAtEnd = atEnd;
		UpdateHandles();
	}

	// whether this clip is the one in the selection wearing the handles -
	// the view picks one, the last selected clip hovered
	bool handlesEnabled;

	public void SetHandlesEnabled(bool enabled)
	{
		handlesEnabled = enabled;
		UpdateHandles();
	}

	void UpdateHandles()
	{
		bool selected = outline.Visible && handlesEnabled;

		if (endControls is not null) endControls.Visible = selected && !seamAtEnd;
		if (startControls is not null) startControls.Visible = selected && !seamAtStart && Clip.Start > TimeSpan.Zero;
	}

	// ---- keeping the inner controls on screen ----

	// the scene's margins between the controls box and the clip's edges
	float insetLeft = 3f;
	float insetRight = 3f;

	// the view's horizontal window, in the same space as this clip's
	// Position. infinite until the view has said
	float viewLeft = float.NegativeInfinity;
	float viewRight = float.PositiveInfinity;

	// how much of this clip's width the controls can currently use
	float visibleWidth = -1f;

	// called every frame by the view. the controls box is pulled in to the
	// part of the clip inside the window, so the row lays out - name left,
	// buttons pinned right - against the visible edges, not the clip's
	public void KeepControlsInView(float left, float right)
	{
		viewLeft = left;
		viewRight = right;

		if (ApplyVisibleSpan()) FitControls();
	}

	// returns whether the span changed
	bool ApplyVisibleSpan()
	{
		float left = Mathf.Clamp(viewLeft - Position.X, 0f, Size.X);
		float right = Mathf.Clamp(viewRight - Position.X, 0f, Size.X);

		// a clip entirely off screen keeps whatever it had, rather than
		// collapsing to nothing and reshuffling when it comes back
		if (right - left < 1f) return false;

		float width = right - left;
		bool changed = width != visibleWidth;
		visibleWidth = width;

		if (controlsBox is not null)
		{
			float offsetLeft = left + insetLeft;
			float offsetRight = -(Size.X - right) - insetRight;

			if (controlsBox.OffsetLeft != offsetLeft || controlsBox.OffsetRight != offsetRight)
			{
				controlsBox.OffsetLeft = offsetLeft;
				controlsBox.OffsetRight = offsetRight;
				changed = true;
			}
		}

		return changed;
	}

	// the speed readout only earns its place when there is something to say
	void UpdateSpeed() => ShowSpeed(Clip.Speed);

	// what a timeshift in progress would set the speed to. Refresh puts the
	// real value back
	public void PreviewSpeed(double value) => ShowSpeed(value);

	void ShowSpeed(double value)
	{
		if (speed is null || speedPercentage is null) return;

		speedPercentage.Text = $"{Math.Round(value * 100d)}%";
		SetWanted(speed, Math.Abs(value - 1d) > 0.0005d);
	}

	// ---- fitting the inner controls to the clip's width ----

	// controls the width rule has hidden. kept apart from Visible so a control
	// hidden for its own reasons (the speed readout at 100%) is not brought
	// back the moment the clip gets wider - only these are
	readonly HashSet<Control> hiddenForWidth = [];

	// a control's own say in whether it shows. the width rule only ever
	// takes away from controls that want to be shown
	void SetWanted(Control control, bool wanted)
	{
		hiddenForWidth.Remove(control);
		control.Visible = wanted;

		FitControls();
	}

	public override void _Notification(int what)
	{
		if (what != NotificationResized) return;

		ApplyVisibleSpan();
		FitControls();
	}

	void FitControls()
	{
		if (widthPriority is null || widthPriority.Length == 0 || content is null) return;

		// everything that would show given the room, least important first
		List<Control> wanted = [.. widthPriority.Where(c => c.Visible || hiddenForWidth.Contains(c))];

		hiddenForWidth.Clear();

		float separation = widthPriority[0].GetParent() is BoxContainer row ? row.GetThemeConstant("separation") : 0f;

		// measured against what this clip has on screen (its whole width until
		// the view has said otherwise), not the content panel's size: the
		// resize notification arrives before the panel has re-laid out its
		// child, so the content would still be a frame behind
		float span = visibleWidth >= 0f ? visibleWidth : Size.X;
		float available = span - (insetLeft + insetRight);

		// drop from the front until the rest fits. no room for even the most
		// important one means no room for any
		while (wanted.Count > 0 && RowWidth(wanted, separation) > available)
		{
			hiddenForWidth.Add(wanted[0]);
			wanted.RemoveAt(0);
		}

		foreach (Control c in widthPriority)
		{
			bool show = wanted.Contains(c);

			// leave controls hidden on their own account alone
			if (c.Visible != show && (show || hiddenForWidth.Contains(c))) c.Visible = show;
		}
	}

	float RowWidth(List<Control> controls, float separation)
	{
		float width = 0f;

		foreach (Control c in controls) width += RequiredWidth(c);

		return width + separation * Mathf.Max(0, controls.Count - 1);
	}

	float RequiredWidth(Control c)
	{
		float width = c.GetCombinedMinimumSize().X;

		if (c.SizeFlagsHorizontal.HasFlag(SizeFlags.Expand)) width = Mathf.Max(width, minimumExpandingWidth);

		return width;
	}


	// our captured drag will never get a release. hand it to the view to undo -
	// Mouse calls this directly because no input event is coming to route it
	public void CancelDrag(MouseButtonState button) => ClipsView.CancelDrag();

    public override void _GuiInput(InputEvent _)
	{
		MouseButtonState left = InputManager.Singleton.Mouse.LeftButton;

        switch (left.Action)
        {
            case MouseAction.Press:
            case MouseAction.DoubleClick:
                // claim the press, so whatever drag follows is unambiguously ours
                if (!left.Capture(this)) break;
                Press(left);
                break;

            case MouseAction.DragStart:
                if (!left.HasCapture(this)) break;
                ClipsView.BeginDrag(this);
                // fall through so the frame that started the drag also moves the
                // clips - otherwise the selection visibly jumps the threshold
                goto case MouseAction.DragMove;

            case MouseAction.DragMove:
                if (!left.HasCapture(this)) break;
                ClipsView.DragSelection(this, Drag(left));
                break;

            case MouseAction.DragEnd:
                if (!left.HasCapture(this)) break;
                ClipsView.FinishDrag(this, Drag(left));
                break;

            case MouseAction.Click:
                if (!left.HasCapture(this)) break;
                // a modified click already did its work on press; collapsing the
                // selection here would immediately undo it
                if (left.PressModifiers.Shift || left.PressModifiers.Control) break;
                ClipsView.SelectClip(this, SelectionMode.Exclusive);
                break;
        }

		void Press(MouseButtonState left)
		{
			// clicking a clip is clicking the view, as far as the keyboard goes,
			// and its channel is where the next paste is aimed
			ClipsView.ClaimKeyboard();
			ClipsView.MarkTarget(this);

			if (left.PressModifiers.Shift) ClipsView.SelectClip(this, SelectionMode.Inclusive);
			else if (left.PressModifiers.Control) ClipsView.DeselectClip(this);
			// ExclusiveIfUnselected rather than Exclusive: dragging a clip that is
			// already part of a multi-selection has to take the rest of the selection
			// with it. if this turns out to be a click and not a drag, the Click case
			// collapses it afterwards
			else ClipsView.SelectClip(this, SelectionMode.ExclusiveIfUnselected);
		}

		// where the drag began, and how far it has travelled since
		(Vector2 start, Vector2 delta) Drag(MouseButtonState left)
			=> (left.ClickStartPosition, InputManager.Singleton.Mouse.GetDragDelta(left));
	}

	// reset clip ui to what is actually stored in data
	public void Refresh()
	{
		Position = new(
			(float)ClipsView.UITimeline.TimeSpanToPixels(Clip.Start),
			(float)(ClipsView.UITimeline.VerticalScale * (GetChannelsDown() + ClipsView.ChannelOffset))
		);

		Size = new(
			(float)ClipsView.UITimeline.TimeSpanToPixels(Clip.Duration),
			(float)ClipsView.UITimeline.VerticalScale
		);

		// the start may have moved onto or off zero
		UpdateHandles();
		UpdateSpeed();
	}

	// show the clip spanning start to end, leaving the data alone - an edge
	// drag in progress. Refresh puts it back
	public void PreviewSpan(TimeSpan start, TimeSpan end)
	{
		Position = new((float)ClipsView.UITimeline.TimeSpanToPixels(start), Position.Y);
		Size = new((float)ClipsView.UITimeline.TimeSpanToPixels(end - start), Size.Y);
	}

	// move clip ui relative to what is actually stored in data
	public void MoveGUI(TimeSpan timeDelta, int channelDelta)
	{
		Position = new(
			(float)ClipsView.UITimeline.TimeSpanToPixels(Clip.Start + timeDelta),
			(float)(ClipsView.UITimeline.VerticalScale * (GetChannelsDownAfter(channelDelta) + ClipsView.ChannelOffset))
		);
	}

	// which row this clip would sit on after moving the given number of channels.
	// video channels are drawn bottom-up, so a positive delta moves them up the
	// screen and a negative row means it wants a channel that does not exist yet
	public int GetChannelsDownAfter(int channelDelta)
		=> GetChannelsDown() + ((Clip is VideoClip) ? -channelDelta : channelDelta);

	int GetChannelsDown()
	{
		// video clip
		if (Clip is VideoClip)
		{
			return Clip.Channel.Timeline.VideoChannels.Count - 1 - Clip.Channel.Index;
		}
		// audio clip
		else
		{
			return Clip.Channel.Timeline.VideoChannels.Count + Clip.Channel.Index;
		}
	}

	public void SetTransparency(float alpha)
	{
		Color transparency = Modulate;
		transparency.A = alpha;
		Modulate = transparency;
	}

	public void SetLength(float length)
	{
		content.CustomMinimumSize = new(length, content.CustomMinimumSize.Y);

		CustomMinimumSize = new(
			content.Size.X,
			CustomMinimumSize.Y
		);

		UpdateThumbnail();
	}

	void UpdateThumbnail()
	{
		
	}
}
