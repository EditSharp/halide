using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using EditSharp;
using EditSharp.Components.Clips;
using EditSharp.History;
using EditSharpGUI.Scripts.Input;
using EditSharpGUI.Scripts.UI.ContextMenu;
using EditSharpGUI.Scripts.UI.Theming;
using EditSharpGUI.Scripts.UI.Thumbnails;
using static UIClipsView;
using EditSharpGUI.Scripts.UI;

public partial class UIClip : PanelContainer, IDragCancellable
{
	[ExportGroup("Controls")]

	[Export] Control content;

	// the row the inner controls live in. at runtime it is narrowed and slid
	// to whatever part of the clip is actually on screen, so the controls
	// stay in view on a clip that runs off the edge, while everything else
	// in the clip - the thumbnail under it - still spans the whole clip.
	// see KeepControlsInView
	[Export] Control controlsBox;

	[Export] Label clipName;
	[Export] LineEdit nameEdit;
	[Export] Control speed;
	[Export] Label speedPercentage;
	[Export] Button graph;
	[Export] Button options;

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

	public Clip Clip;
	public UIClipsView ClipsView;

	// a ghost previews a drop: a clip not on the timeline, laid out on a
	// row the view names, translucent, taking no mouse
	public int? GhostRow;
	public bool IsGhost => GhostRow is not null;

	// the graph button was pressed; opening an editor for it is the page's job
	public event Action<UIClip> GraphRequested;

	// the clip's colour: its own pick, or the theme's colour for its kind,
	// painted over the theme's ClipContent style - so the theme decides the
	// palette and the shape, and this only picks which colour
	public Color Color
	{
        get;
        set
		{
			StyleBox box = GetThemeStylebox("panel", "ClipContent");
			StyleBoxFlat style = box is ThemedStyleBox themed ? themed.MakeFlat() : box.Duplicate() as StyleBoxFlat;

			if (style is not null)
			{
				style.BgColor = value;
				content.AddThemeStyleboxOverride("panel", style);
			}

			field = value;
			waveform?.SetColor(ClipColors.Waveform(this, value));
		}
	}

	void ApplyThemeColor() => Color = ClipColors.Resolve(this, Clip);

	// the colour again only when it changed - a switch of the clip's input,
	// a pick from the menu - since painting it rebuilds the panel's style
	void RefreshThemeColor()
	{
		Color wanted = ClipColors.Resolve(this, Clip);
		if (wanted != Color) Color = wanted;
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

		ApplyThemeColor();

		// the handles only report the life of a drag. the view runs it from the
		// cursor every frame, the same as a clip drag, so it edge-scrolls and
		// snaps like one
		foreach (UIDragHandle handle in extends) Wire(handle, EdgeDragKind.Extend);
		foreach (UIDragHandle handle in timeshifts) Wire(handle, EdgeDragKind.Timeshift);

		// the row is laid out by the box around it. from here on it takes its
		// width from the visible slice of the clip and slides along inside the
		// box to sit under it - the box, and the thumbnail below the row, keep
		// spanning the whole clip. the box's own margins are what the row
		// keeps from the visible edges
		if (controlsBox is not null)
		{
			controlsBox.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
			controlsBox.OffsetTransformEnabled = true;
			controlsBox.OffsetTransformVisualOnly = false;

			if (controlsBox.GetParent() is Control box)
			{
				insetLeft = box.OffsetLeft;
				insetRight = -box.OffsetRight;
			}
		}

		if (options is not null) options.Pressed += () => ClipsView.ShowClipMenu(this, options);
		if (graph is not null) graph.Pressed += () => GraphRequested?.Invoke(this);

		if (IsGhost)
		{
			SetTransparency(0.5f);
			IgnoreMouse(this);
		}

		if (nameEdit is not null)
		{
			nameEdit.Visible = false;
			nameEdit.TextSubmitted += _ => EndRename(commit: true);
			nameEdit.FocusExited += () => EndRename(commit: true);
		}

		Refresh();
		UpdateThumbnail();

		void Wire(UIDragHandle handle, EdgeDragKind kind)
		{
			handle.DragStarted += (_, _) => ClipsView.BeginEdgeDrag(this, handle, kind);
			handle.DragEnded += (_, _) => ClipsView.FinishEdgeDrag(handle);
			handle.DragCancelled += (_, _) => ClipsView.CancelEdgeDrag(handle);
		}
	}

	static void IgnoreMouse(Control control)
	{
		control.MouseFilter = MouseFilterEnum.Ignore;
		foreach (Node child in control.GetChildren()) if (child is Control c) IgnoreMouse(c);
	}

	// ---- renaming in place ----

	bool renaming;

	// the name label becomes a field until enter or a click elsewhere
	public void BeginRename()
	{
		if (nameEdit is null || renaming) return;

		renaming = true;
		nameEdit.Text = Clip.Name;
		nameEdit.Visible = true;
		clipName.Visible = false;
		nameEdit.GrabFocus();
		nameEdit.SelectAll();
	}

	void EndRename(bool commit)
	{
		if (!renaming) return;
		renaming = false;

		nameEdit.Visible = false;
		clipName.Visible = true;

		string name = nameEdit.Text.Trim();
		if (!commit || name.Length == 0 || name == Clip.Name) { FitControls(); return; }

		using (Transaction.Scope change = ClipsView.UITimeline.History.Begin("Rename clip"))
		{
			Clip.Name = name;
			change.Commit();
		}

		Refresh();
	}

	// whether this clip is the one wearing the handles - the view picks
	// one, the clip nearest the cursor
	bool handlesEnabled;

	public void SetHandlesEnabled(bool enabled)
	{
		handlesEnabled = enabled;
		UpdateHandles();
	}

	// whether a point (global) is over one of the handles this clip is
	// showing, or the gap between it and the clip's edge that the cursor
	// crosses on the way to it
	public bool HandlesContain(Vector2 point)
	{
		Rect2 clip = GetGlobalRect();
		return Reaches(startControls, clip, point) || Reaches(endControls, clip, point);
	}

	static bool Reaches(Control handles, Rect2 clip, Vector2 point)
	{
		if (handles is null || !handles.IsVisibleInTree()) return false;

		Rect2 box = handles.GetGlobalRect();
		float left = Mathf.Min(box.Position.X, clip.End.X < box.Position.X ? clip.End.X : box.Position.X);
		float right = Mathf.Max(box.End.X, clip.Position.X > box.End.X ? clip.Position.X : box.End.X);

		return point.X >= left && point.X <= right && point.Y >= box.Position.Y && point.Y <= box.End.Y;
	}

	void UpdateHandles()
	{
		if (IsGhost) return;
		if (endControls is not null) endControls.Visible = handlesEnabled;
		if (startControls is not null) startControls.Visible = handlesEnabled && Clip.Start > Time.Zero;
	}

	// ---- keeping the inner controls on screen ----

	// the scene's margins between the controls box and the clip's edges
	float insetLeft = 3f;
	float insetRight = 3f;

	// the view's horizontal window, in the same space as this clip's
	// Position. infinite until the view has said
	float viewLeft = float.NegativeInfinity;
	float viewRight = float.PositiveInfinity;

	// how much of this clip's width the controls can currently use, and
	// where that starts inside the box
	float visibleWidth = -1f;
	float rowStart;

	// called every frame by the view. the row is narrowed to the part of the
	// clip inside the window and slid across to it, so it lays out - name
	// left, buttons pinned right - against the visible edges, not the clip's
	public void KeepControlsInView(float left, float right)
	{
		viewLeft = left;
		viewRight = right;

		if (ApplyVisibleSpan()) FitControls();

		KeepThumbnailInView(left, right);
	}

	// returns whether the span changed
	bool ApplyVisibleSpan()
	{
		float left = Mathf.Clamp(viewLeft - Position.X, 0f, Size.X);
		float right = Mathf.Clamp(viewRight - Position.X, 0f, Size.X);

		// a clip entirely off screen keeps whatever it had, rather than
		// collapsing to nothing and reshuffling when it comes back
		if (right - left < 1f) return false;

		// into the box's own space, which is inset from the clip's edges
		float boxWidth = Mathf.Max(0f, Size.X - insetLeft - insetRight);
		float start = Mathf.Clamp(left - insetLeft, 0f, boxWidth);
		float end = Mathf.Clamp(right - insetLeft, 0f, boxWidth);
		float width = Mathf.Max(0f, end - start);

		bool changed = width != visibleWidth || start != rowStart;
		visibleWidth = width;
		rowStart = start;

		if (changed && controlsBox is not null)
		{
			controlsBox.OffsetTransformPosition = new(start, 0f);
			controlsBox.CustomMinimumSize = new(width, controlsBox.CustomMinimumSize.Y);
		}

		return changed;
	}

	// the speed readout only earns its place when there is something to say
	void UpdateSpeed() => ShowSpeed(Clip.Speed);

	// what a timeshift in progress would set the speed to. Refresh puts the
	// real value back
	public void PreviewSpeed(Rational value)
	{
		ShowSpeed(value);
		waveform?.SetPreviewSpeed(value.Value);
	}

	void ShowSpeed(Rational value)
	{
		if (speed is null || speedPercentage is null) return;

		speedPercentage.Text = value.IsZero ? "Freeze" : $"{Math.Round(value.Value * 100d)}%";
		SetWanted(speed, value != Rational.One);
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
		// the theme can change under a running app - a preset switch, an
		// accent change - and the colour was copied out of it
		if (what == NotificationThemeChanged && Clip is not null && content is not null) ApplyThemeColor();

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
		float available = visibleWidth >= 0f ? visibleWidth : Size.X - (insetLeft + insetRight);

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

			// leave controls hidden on their own account alone. the name
			// label stays hidden while its field stands in for it
			if (renaming && c == clipName) continue;
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

		// a right click opens the clip's menu at the cursor
		ContextTrigger.Handle(this, at => ClipsView.ShowClipMenu(this, at));

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
		// the name and the kind of input can change in the inspector
		if (clipName is not null && clipName.Text != Clip.Name)
		{
			clipName.Text = Clip.Name;
			FitControls();
		}

		if (content is not null) RefreshThemeColor();

		int row = GhostRow ?? GetChannelsDown();

		Position = new(
			(float)ClipsView.UITimeline.TimeSpanToPixels(Clip.Start),
			(float)(ClipsView.UITimeline.VerticalScale * (row + ClipsView.ChannelOffset))
		);

		Size = new(
			(float)ClipsView.UITimeline.TimeSpanToPixels(Clip.Duration),
			(float)ClipsView.UITimeline.VerticalScale
		);

		if (IsGhost)
		{
			UpdateThumbnail();
			return;
		}

		// the start may have moved onto or off zero
		UpdateHandles();
		UpdateSpeed();

		// the scale may have changed, and any head preview is over
		UpdateThumbnail();
		strip?.SetPreviewShift(Time.Zero);
		waveform?.SetPreviewShift(Time.Zero);
		waveform?.SetPreviewSpeed(null);
	}

	// show the clip spanning start to end, leaving the data alone - an edge
	// drag in progress. Refresh puts it back
	public void PreviewSpan(Time start, Time end)
	{
		Position = new((float)ClipsView.UITimeline.TimeSpanToPixels(start), Position.Y);
		Size = new((float)ClipsView.UITimeline.TimeSpanToPixels(end - start), Size.Y);

		// the head moving is the edge sliding over the content, not the
		// content moving with the edge - tell the strip how far
		Time shift = Clip.ToContentTime(start - Clip.Start);
		strip?.SetPreviewShift(shift);
		waveform?.SetPreviewShift(shift);
	}

	// move clip ui relative to what is actually stored in data
	public void MoveGUI(Time timeDelta, int channelDelta)
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

	// ---- the thumbnail ----

	// the filmstrip filling the thumbnail panel, from the scene, for video
	// clips; the waveform beside it, for audio clips. each is fed the first
	// time its cache is there to feed it, since the timeline may hand the
	// caches over after this clip exists
	[Export] ThumbnailStrip strip;
	[Export] WaveformStrip waveform;
	bool stripFed;

	void UpdateThumbnail()
	{
		if (Clip is VideoClip video)
		{
			if (waveform is not null) waveform.Visible = false;
			if (strip is null) return;

			if (!stripFed)
			{
				if (ClipsView?.UITimeline?.Thumbnails is not ThumbnailCache cache) return;

				strip.Setup(cache, video);
				stripFed = true;
			}

			strip.SetScale(ClipsView.UITimeline.PixelsPerSecond);
		}
		else if (Clip is AudioClip audio)
		{
			if (strip is not null) strip.Visible = false;
			if (waveform is null) return;

			if (!stripFed)
			{
				if (ClipsView?.UITimeline?.Waveforms is not WaveformCache cache) return;

				waveform.Setup(cache, audio);
				waveform.SetColor(ClipColors.Waveform(this, Color));
				waveform.Visible = true;
				stripFed = true;
			}

			waveform.SetScale(ClipsView.UITimeline.PixelsPerSecond);
		}
	}

	// the view's window, in the strip's own x. the strip sits inset from
	// the clip's edge by the scene's margins
	void KeepThumbnailInView(float left, float right)
	{
		Control shown = Clip is VideoClip ? strip : waveform;
		if (shown is null) return;

		float offset = shown.GlobalPosition.X - GlobalPosition.X;
		float from = left - Position.X - offset, to = right - Position.X - offset;

		strip?.SetVisibleRange(from, to);
		waveform?.SetVisibleRange(from, to);
	}
}
