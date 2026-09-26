using EditSharp.Components;
using EditSharp.Components.Channels;
using EditSharp.Components.Clips;
using EditSharp.History;
using EditSharpGUI.Scripts;
using EditSharpGUI.Scripts.Input;
using EditSharpGUI.Scripts.UI;
using EditSharpGUI.Scripts.UI.ContextMenu;
using EditSharpGUI.Scripts.UI.DragDrop;
using EditSharp.Components.Media;
using EditSharpGUI.Scripts.UI.Theming;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class UIClipsView : PanelContainer, IDragCancellable, IDropTarget
{
	[ExportGroup("Controls")]

	[Export] Control clipsControl;

	[ExportGroup("Packed Scenes")]

	[Export] PackedScene clipScene;

	[ExportGroup("Menus")]

	// the clip menu, cloned for every showing so its state and handlers are
	// the clip's; and the menu for empty space
	[Export] ContextMenu clipMenu;
	[Export] ContextMenu viewMenu;

	public UITimeline UITimeline;
	public List<UIClip> UIClips { get; private set; } = [];

	// a clip's graph button was pressed; the page opens an editor for it
	public event Action<UIClip> GraphRequested;


	// a captured drag can end without a release - the window lost focus, or the
	// os swallowed the button. the pan cursor would otherwise stay stuck on,
	// and a box would keep following a cursor that let go
	public void CancelDrag(MouseButtonState button)
	{
		MouseDefaultCursorShape = CursorShape.Arrow;
		CancelBox();
	}

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
		Time at = UITimeline.PlayheadTime;

		if (pasteTarget is (bool video, int index))
		{
			// the move is a number of rows on screen, worked out from the clip in
			// hand (or the lowest of the target's kind) to the target channel,
			// and every clip moves by that many rows - video and audio alike.
			// rows count downwards; video indices go up the screen and audio
			// indices go down it, so the two kinds shift in opposite directions
			int videoCount = UITimeline.Timeline.VideoChannels.Count;
			int Row(bool isVideo, int channelIndex) => isVideo ? videoCount - 1 - channelIndex : videoCount + channelIndex;

			ClipsItem.Entry? anchor = item.AnchorIndex >= 0 ? entries[item.AnchorIndex] : null;
			List<ClipsItem.Entry> ofKind = [.. entries.Where(e => e.Video == video)];

			int? fromRow = anchor is ClipsItem.Entry held ? Row(held.Video, held.ChannelIndex)
				: ofKind.Count > 0 ? Row(video, ofKind.Min(e => e.ChannelIndex))
				: null;

			if (fromRow is int from)
			{
				int rows = Row(video, index) - from;
				int videoShift = -rows;
				int audioShift = rows;

				// nothing can go below the first channel of its kind
				List<ClipsItem.Entry> videos = [.. entries.Where(e => e.Video)];
				List<ClipsItem.Entry> audios = [.. entries.Where(e => !e.Video)];

				if (videos.Count > 0) videoShift = Mathf.Max(videoShift, -videos.Min(e => e.ChannelIndex));
				if (audios.Count > 0) audioShift = Mathf.Max(audioShift, -audios.Min(e => e.ChannelIndex));

				entries = [.. entries.Select(e => e with { ChannelIndex = e.ChannelIndex + (e.Video ? videoShift : audioShift) })];
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
		Time at = UITimeline.PlayheadTime;
		Timeline timeline = UITimeline.Timeline;

		IEnumerable<Clip> candidates = everything || Selection.Count == 0
			? timeline.Channels.SelectMany(c => c.Clips)
			: Selection.Select(s => s.Clip);

		List<Clip> spanning = [.. candidates.Where(c => c.Start < at && c.End > at).Distinct()];
		if (spanning.Count == 0) return;

		HashSet<Clip> before = [.. timeline.Channels.SelectMany(c => c.Clips)];

		// where the selected clips were, so their pieces can be found afterwards
		List<Clip> selected = [.. Selection.Select(s => s.Clip)];
		List<(Channel channel, Time start, Time end)> selectedSpans = [.. selected.Select(c => (c.Channel, c.Start, c.End))];

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
					foreach ((Time start, Time end) in MergeRanges(clips).OrderByDescending(r => r.start))
						timeline.RippleRemoveRange(start, end);
					break;
			}

			change.Commit();
		}

		Selection.Clear();
		Reconcile();
	}

	// the clips' spans, with any that touch or overlap joined into one
	static List<(Time start, Time end)> MergeRanges(IEnumerable<Clip> clips)
	{
		List<(Time start, Time end)> merged = [];

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

				// remembered in content space, so a box that starts after the
				// view has scrolled still opens where the press actually landed
				pressedAt = UITimeline.ToViewContent(InputManager.Singleton.Mouse.CurrentPosition);

				// shift and control are the add and remove modifiers, and on
				// empty space there is nothing to add or remove - the box that
				// may follow does that. a plain press starts over
				if (!left.PressModifiers.Shift && !left.PressModifiers.Control) DeselectAll();
				break;

			case MouseAction.DragStart:
				if (!left.HasCapture(this)) break;
				BeginBox(left);
				// fall through so the frame that started the box also draws it
				goto case MouseAction.DragMove;

			case MouseAction.DragMove:
				if (!left.HasCapture(this) || !box.Active) break;
				UpdateBox();
				break;

			case MouseAction.DragEnd:
				if (!left.HasCapture(this) || !box.Active) break;
				FinishBox();
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

		ContextTrigger.Handle(this, ShowViewMenu);

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
	}

	// ---- menus ----

	// the clip's menu, at a point or under its options button. a right click
	// on a clip that is part of a larger selection offers both the clip and
	// the whole selection, as two submenus of the same items; the options
	// button, or a clip on its own, gets the flat menu for that clip
	public void ShowClipMenu(UIClip clip, Vector2 at) => ShowClipMenu(clip, at, null);

	public void ShowClipMenu(UIClip clip, Control button) => ShowClipMenu(clip, null, button);

	void ShowClipMenu(UIClip clip, Vector2? at, Control button)
	{
		if (clipMenu is null) return;

		ClaimKeyboard();
		MarkTarget(clip);

		bool ofSelection = button is null && Selection.Contains(clip) && Selection.Count > 1;
		ContextMenu shown;

		if (ofSelection)
		{
			ContextMenu one = clipMenu.Clone();
			ContextMenu all = clipMenu.Clone();
			ConfigureClipMenu(one, [clip]);
			ConfigureClipMenu(all, [.. Selection]);

			shown = new ContextMenu
			{
				HideOnCheckableItemSelect = clipMenu.HideOnCheckableItemSelect,
				Elements = [
					new ContextSubmenu { Id = "clip.this", Text = new("This Clip"), Elements = one.Elements },
					new ContextSubmenu { Id = "clip.selection", Text = new($"Entire Selection ({Selection.Count})"), Elements = all.Elements },
				]
			};
		}
		else
		{
			shown = clipMenu.Clone();
			ConfigureClipMenu(shown, [clip]);
		}

		if (button is not null) ContextMenus.ShowContextMenu(shown, button);
		else ContextMenus.ShowContextMenu(shown, at);
	}

	// the menu's state and handlers for a set of clips
	void ConfigureClipMenu(ContextMenu menu, List<UIClip> targets)
	{
		Time playhead = UITimeline.PlayheadTime;
		bool spanning = targets.Any(c => c.Clip.Start < playhead && c.Clip.End > playhead);
		bool anySpanning = UITimeline.Timeline.Channels.SelectMany(c => c.Clips).Any(c => c.Start < playhead && c.End > playhead);

		Wire(menu, "clip.cut", Shortcuts.Cut, true, () => WithTargets(targets, CutSelection));
		Wire(menu, "clip.copy", Shortcuts.Copy, true, () => WithTargets(targets, CopySelection));
		Wire(menu, "clip.paste", Shortcuts.Paste, Clipboard.Shared.TryGet(out ClipsItem _), Paste);
		Wire(menu, "clip.delete", Shortcuts.Delete, true, () => WithTargets(targets, () => DeleteSelection(RippleScope.None)));
		Wire(menu, "clip.rippleDelete", Shortcuts.RippleDelete, true, () => WithTargets(targets, () => DeleteSelection(RippleScope.OwnChannels)));
		Wire(menu, "clip.split", Shortcuts.Split, spanning, () => WithTargets(targets, () => SplitAtPlayhead(everything: false)));
		Wire(menu, "clip.splitAll", Shortcuts.SplitAll, anySpanning, () => SplitAtPlayhead(everything: true));
		Wire(menu, "clip.rename", null, targets.Count == 1, targets[0].BeginRename);
		Wire(menu, "clip.resetSpeed", null, targets.Any(c => c.Clip.Speed != Rational.One), () => ResetSpeed(targets));

		// linked: checked when every target is in one group. one clip alone
		// cannot be linked to anything, so it can only be unlinked
		if (menu.Find<ContextButton>("clip.link") is ContextButton link)
		{
			Guid?[] groups = [.. targets.Select(c => c.Clip.LinkGroupId).Distinct()];
			bool linked = groups.Length == 1 && groups[0] is not null;
			link.Checked = linked;
			link.Enabled = linked || targets.Count > 1;
			link.Pressed += () => ToggleLink(targets, link.Checked, wholeSelection: targets.Count > 1);
		}

		ConfigureColorMenu(menu, targets);
	}

	void ConfigureColorMenu(ContextMenu menu, List<UIClip> targets)
	{
		string[] colors = [.. targets.Select(c => c.Clip.Color).Distinct()];

		// the colour they all share; a mix matches nothing
		bool mixed = colors.Length != 1;
		string shared = mixed ? null : colors[0];

		if (menu.Find<ContextRadioList>("clip.color.swatches") is ContextRadioList swatches)
		{
			swatches.SelectedButton = -1;

			for (int i = 0; i < swatches.Buttons.Count; i++)
			{
				ContextButton button = swatches.Buttons[i];
				string name = button.Id["clip.color.".Length..];

				if (ClipColors.Swatch(name) is ClipSwatch swatch)
				{
					button.Icon = SwatchIcon.Get(GetThemeColor(ClipColors.Item(swatch), "Clip"));
					if (!mixed && string.Equals(shared, name, StringComparison.OrdinalIgnoreCase)) swatches.SelectedButton = i;
				}
			}

			swatches.Selected += button => SetColor(targets, button.Id["clip.color.".Length..]);
		}

		if (menu.Find<ContextButton>("clip.color.custom") is ContextButton custom)
		{
			custom.Checked = !mixed && ClipColors.IsCustom(shared);
			if (custom.Checked) custom.Icon = SwatchIcon.Get(Color.FromHtml(shared));
			custom.Pressed += () => PickCustomColor(targets);
		}

		if (menu.Find<ContextButton>("clip.color.default") is ContextButton fallback)
		{
			fallback.Checked = !mixed && shared is null;
			fallback.Icon = SwatchIcon.Get(GetThemeColor(ClipColors.Kind(targets[0].Clip), "Clip"));
			fallback.Pressed += () => SetColor(targets, null);
		}
	}

	void Wire(ContextMenu menu, string id, string shortcut, bool enabled, Action action)
	{
		if (menu.Find<ContextButton>(id) is not ContextButton button) return;

		button.Enabled = enabled;
		if (shortcut is not null) ContextMenus.SetHint(button, shortcut);
		button.Pressed += () => action();
	}

	// runs a selection action on the given clips instead of the selection,
	// then puts the selection back, less anything the action removed
	void WithTargets(List<UIClip> targets, Action action)
	{
		if (targets.Count == Selection.Count && targets.All(Selection.Contains)) { action(); return; }

		List<UIClip> before = [.. Selection];
		Selection.Clear();
		Selection.AddRange(targets);

		action();

		Selection.Clear();
		Selection.AddRange(before.Where(UIClips.Contains));
		UpdateSelection();
	}

	// unlinking one clip takes it out of its group and leaves the rest
	// linked (a pair dissolves); unlinking the whole selection dissolves
	// every group in it
	void ToggleLink(List<UIClip> targets, bool link, bool wholeSelection)
	{
		Timeline timeline = UITimeline.Timeline;

		using (Transaction.Scope change = UITimeline.History.Begin(link ? "Link clips" : "Unlink clips"))
		{
			if (link) timeline.Link(targets.Select(c => c.Clip));
			else if (wholeSelection)
			{
				foreach (Guid id in targets.Select(c => c.Clip.LinkGroupId).OfType<Guid>().Distinct().ToList())
					timeline.GetLinkGroup(id)?.Unlink();
			}
			else
			{
				foreach (UIClip c in targets) timeline.GetLinkGroup(c.Clip.LinkGroupId)?.Remove(c.Clip);
			}

			change.Commit();
		}

		Reconcile();
	}

	void SetColor(List<UIClip> targets, string color)
	{
		using (Transaction.Scope change = UITimeline.History.Begin(targets.Count == 1 ? "Colour clip" : $"Colour {targets.Count} clips"))
		{
			foreach (UIClip c in targets) c.Clip.Color = color;
			change.Commit();
		}

		foreach (UIClip c in targets) c.Refresh();
	}

	// a colour picker in a popup; the clips follow it live and the pick
	// becomes one history entry when it closes
	void PickCustomColor(List<UIClip> targets)
	{
		string[] before = [.. targets.Select(c => c.Clip.Color)];
		string current = before.FirstOrDefault(ClipColors.IsCustom);
		Color start = current is not null ? Color.FromHtml(current) : targets[0].Color;

		PopupPanel popup = new();
		ColorPicker picker = new() { Color = start, EditAlpha = false, PickerShape = ColorPicker.PickerShapeType.HsvRectangle };
		popup.AddChild(picker);
		AddChild(popup);

		picker.ColorChanged += colour =>
		{
			using IDisposable _ = Transaction.Suppress();
			foreach (UIClip c in targets) { c.Clip.Color = "#" + colour.ToHtml(false); c.Refresh(); }
		};

		popup.PopupHide += () =>
		{
			string picked = "#" + picker.Color.ToHtml(false);

			using (Transaction.Suppress())
			{
				for (int i = 0; i < targets.Count; i++) targets[i].Clip.Color = before[i];
			}

			SetColor(targets, picked);
			popup.QueueFree();
		};

		popup.Popup(new Rect2I((Vector2I)InputManager.Singleton.Mouse.CurrentPosition.Round(), Vector2I.Zero));
	}

	void ResetSpeed(List<UIClip> targets)
	{
		using (Transaction.Scope change = UITimeline.History.Begin("Reset speed"))
		{
			foreach (UIClip c in targets) c.Clip.Speed = Rational.One;
			change.Commit();
		}

		Reconcile();
	}

	// the menu for empty space: paste aimed at the clicked channel, the
	// selection, and a gap opened at the clicked time
	void ShowViewMenu(Vector2 at)
	{
		if (viewMenu is null) return;

		ClaimKeyboard();
		MarkTarget(GetChannelAtPoint(at));

		Time time = UITimeline.SnapPoint(UITimeline.PixelsToTimeSpan(UITimeline.ToViewContent(at).X), [], includePlayhead: true, out _);
		if (time < Time.Zero) time = Time.Zero;

		ContextMenu shown = viewMenu.Clone();
		Wire(shown, "clips.paste", Shortcuts.Paste, Clipboard.Shared.TryGet(out ClipsItem _), Paste);
		Wire(shown, "clips.selectAll", Shortcuts.SelectAll, UIClips.Count > 0, SelectAll);
		Wire(shown, "clips.deselect", null, Selection.Count > 0, DeselectAll);
		Wire(shown, "clips.insertGap", null, true, () => InsertGap(time, Time.FromSeconds(1)));

		ContextMenus.ShowContextMenu(shown, at);
	}

	// opens a gap on every channel: clips spanning the time are split there,
	// and everything from the time on moves later by the gap
	public void InsertGap(Time at, Time length)
	{
		Timeline timeline = UITimeline.Timeline;

		using (Transaction.Scope change = UITimeline.History.Begin("Insert gap"))
		{
			HashSet<Guid> groups = [];

			foreach (Clip clip in timeline.Channels.SelectMany(c => c.Clips).Where(c => c.Start < at && c.End > at).ToList())
			{
				if (clip.LinkGroupId is Guid id) { if (groups.Add(id)) timeline.GetLinkGroup(id)?.Split(at); }
				else clip.Split(at);
			}

			// latest first, so a move never lands on a clip still waiting its turn
			foreach (Clip clip in timeline.Channels.SelectMany(c => c.Clips).Where(c => c.Start >= at).OrderByDescending(c => c.Start).ToList())
				clip.Move(clip.Start + length);

			change.Commit();
		}

		Reconcile();
	}

	// ---- media dropped in: linked clips, end to end ----

	// files dropped here: the page brings them in and places what came
	public event Action<IReadOnlyList<string>, Vector2> FilesDropped;

	// where one dropped item would land
	readonly record struct Placement(IMedia Media, Timeline Embedded, bool Video, int Channel, Time Start, Time Duration);

	readonly List<UIClip> ghosts = [];
	readonly List<Placement> ghostPlacements = [];

	public bool CanDrop(DragPayload payload, Vector2 at) => payload is MediaPayload or TimelinePayload or FilesPayload;

	public void DragOver(DragPayload payload, Vector2 at)
	{
		if (DragDrop.Ghost is not null) DragDrop.Ghost.Visible = false;

		List<Placement> placements = Layout(payload, at, out Time? lineAt);
		UITimeline.SnapLine = lineAt;
		ShowGhosts(placements);
	}

	public void DragLeave()
	{
		UITimeline.SnapLine = null;
		ClearGhosts();
	}

	public void Drop(DragPayload payload, Vector2 at)
	{
		UITimeline.SnapLine = null;
		ClearGhosts();

		if (payload is FilesPayload files) { FilesDropped?.Invoke(files.Paths, at); return; }

		Place(Layout(payload, at, out _));
	}

	public void PlaceMediaAt(IReadOnlyList<IMedia> media, Vector2 globalPosition)
		=> Place(Layout(new MediaPayload(media), globalPosition, out _));

	public void PlaceMedia(IReadOnlyList<IMedia> media, Time at, bool video, int channelIndex)
		=> Place(Layout(new MediaPayload(media), at, video, channelIndex));

	// how long an item runs when placed: its natural length, or five
	// seconds for a still, an unknown file or an empty timeline
	static Time DurationOf(object item)
	{
		Time fallback = Time.FromSeconds(5);

		return item switch
		{
			IMedia media => media.TryGetNaturalLength(out Time? length) && length is Time l && l > Time.Zero ? l : fallback,
			Timeline timeline => timeline.Duration > Time.Zero ? timeline.Duration : fallback,
			_ => fallback
		};
	}

	List<Placement> Layout(DragPayload payload, Vector2 at, out Time? lineAt)
	{
		(ChannelType type, int index, bool _) = GetChannelAtPoint(at);
		Time time = UITimeline.PixelsToTimeSpan(UITimeline.ToViewContent(at).X);
		if (time < Time.Zero) time = Time.Zero;

		List<object> items = payload switch
		{
			MediaPayload m => [.. m.Media],
			TimelinePayload t => [.. t.Timelines.Where(tl => tl != UITimeline.Timeline)],
			FilesPayload f => [.. f.Paths.Select(p => (object)p)],
			_ => []
		};

		// the head of the row snaps; the rest follow it
		Time total = Time.Zero;
		foreach (object item in items) total += DurationOf(item);
		time = UITimeline.SnapDelta(Time.Zero, [time, time + total], time, [], includePlayhead: true, out lineAt) + time;
		if (time < Time.Zero) time = Time.Zero;

		return Layout(items, time, type == ChannelType.Video, index);
	}

	List<Placement> Layout(DragPayload payload, Time at, bool video, int channelIndex)
		=> Layout(payload switch { MediaPayload m => [.. m.Media], TimelinePayload t => [.. t.Timelines], _ => [] }, at, video, channelIndex);

	// end to end from a time, on the channel under the cursor and its
	// mirror: video channel i pairs with audio channel i. an item that has
	// no clip for the hovered kind is refused
	List<Placement> Layout(List<object> items, Time at, bool video, int channelIndex)
	{
		List<Placement> placements = [];
		Time cursor = at;

		foreach (object item in items)
		{
			Time duration = DurationOf(item);

			switch (item)
			{
				case VideoMedia media:
					placements.Add(new(media, null, true, channelIndex, cursor, duration));
					if (media.Audio is not null) placements.Add(new(media.Audio, null, false, channelIndex, cursor, duration));
					else if (!video) continue;
					break;

				case AudioMedia audio:
					if (video) continue;
					placements.Add(new(audio, null, false, channelIndex, cursor, duration));
					break;

				case Timeline timeline:
					placements.Add(new(null, timeline, true, channelIndex, cursor, duration));
					placements.Add(new(null, timeline, false, channelIndex, cursor, duration));
					break;

				default:
					// a file not yet in the library: a stand-in for the preview
					placements.Add(new(null, null, video, channelIndex, cursor, duration));
					break;
			}

			cursor += duration;
		}

		return placements;
	}

	// ghosts are clips of the placements, off the timeline and translucent,
	// laid out on the rows the drop would land on. they are remade when
	// the items change and only moved otherwise
	void ShowGhosts(List<Placement> placements)
	{
		bool same = ghosts.Count == placements.Count && ghostPlacements.Count == placements.Count
			&& placements.Zip(ghostPlacements).All(p => ReferenceEquals(p.First.Media, p.Second.Media) && ReferenceEquals(p.First.Embedded, p.Second.Embedded) && p.First.Video == p.Second.Video);

		if (!same)
		{
			ClearGhosts();

			foreach (Placement p in placements)
			{
				Clip clip = GhostClip(p);
				if (clip is null) continue;

				UIClip ghost = clipScene.Instantiate<UIClip>();
				ghost.Clip = clip;
				ghost.ClipsView = this;
				ghost.GhostRow = 0;
				ghosts.Add(ghost);
				clipsControl.AddChild(ghost);
			}

			ghostPlacements.Clear();
			ghostPlacements.AddRange(placements);
		}

		int videoCount = UITimeline.Timeline.VideoChannels.Count;

		for (int i = 0; i < ghosts.Count && i < placements.Count; i++)
		{
			Placement p = placements[i];
			int row = p.Video ? videoCount - 1 - p.Channel : videoCount + p.Channel;

			using (Transaction.Suppress())
			{
				ghosts[i].Clip.Start = p.Start;
				ghosts[i].Clip.Duration = p.Duration;
			}

			ghosts[i].GhostRow = row;
			ghosts[i].Visible = row >= 0;
			ghosts[i].Refresh();
		}

		// above every clip, selected or not, like a selection being dragged
		int top = UIClips.Select(c => c.ZIndex).DefaultIfEmpty(0).Max() + 1;
		foreach (UIClip ghost in ghosts)
		{
			ghost.ZIndex = top;
			clipsControl.MoveChild(ghost, -1);
		}
	}

	// a clip of what the placement would make, never placed
	static Clip GhostClip(Placement p) => Transaction.Suppressed<Clip>(() =>
	{
		if (p.Embedded is Timeline embedded)
			return p.Video ? VideoClip.CreateTimelineEmbed(embedded, p.Start, p.Duration) : AudioClip.CreateTimelineEmbed(embedded, p.Start, p.Duration);
		if (p.Media is VideoMedia video) return VideoClip.CreateFromMedia(video, p.Start, p.Duration);
		if (p.Media is AudioMedia audio) return AudioClip.CreateFromMedia(audio, p.Start, p.Duration);

		// a file not in the library yet stands in as an empty clip
		Clip blank = p.Video ? VideoClip.CreateColorGenerator(new SkiaSharp.SKColor(0, 0, 0, 0), p.Start, p.Duration) : AudioClip.CreateTone(p.Start, p.Duration);
		blank.Name = "Import";
		return blank;
	});

	void ClearGhosts()
	{
		foreach (UIClip ghost in ghosts) ghost.QueueFree();
		ghosts.Clear();
		ghostPlacements.Clear();
	}

	// the clips, made and placed in one entry. a video and its soundtrack,
	// or a timeline's picture and sound, are linked
	void Place(List<Placement> placements)
	{
		List<Clip> made = [];
		if (placements.Count == 0) return;

		using (Transaction.Scope change = UITimeline.History.Begin(placements.Count == 1 ? "Add clip" : "Add clips"))
		{
			Dictionary<(object Source, Time Start), List<Clip>> pairs = [];

			foreach (Placement p in placements)
			{
				Clip clip;

				if (p.Embedded is Timeline embedded)
					clip = p.Video ? VideoClip.CreateTimelineEmbed(embedded, p.Start, p.Duration) : AudioClip.CreateTimelineEmbed(embedded, p.Start, p.Duration);
				else if (p.Media is VideoMedia video)
					clip = VideoClip.CreateFromMedia(video, p.Start, p.Duration);
				else if (p.Media is AudioMedia audio)
					clip = AudioClip.CreateFromMedia(audio, p.Start, p.Duration);
				else continue;

				clip.Name = p.Embedded is not null ? "Timeline" : p.Media.Name;
				EnsureChannel(p.Video, p.Channel).AddClip(clip);
				made.Add(clip);

				object source = p.Embedded ?? (object)(p.Media is AudioMedia a && placements.Any(o => o.Media is VideoMedia v && ReferenceEquals(v.Audio, a)) ? placements.First(o => o.Media is VideoMedia v && ReferenceEquals(v.Audio, a)).Media : p.Media);
				if (!pairs.TryGetValue((source, p.Start), out List<Clip> group)) pairs[(source, p.Start)] = group = [];
				group.Add(clip);
			}

			foreach (List<Clip> group in pairs.Values) if (group.Count > 1) UITimeline.Timeline.Link(group);

			change.Commit();
		}

		UITimeline.RefreshChannelEdits();
		Reconcile();
		SelectClips(made);
	}

	// ---- rubber-band selection: a drag on empty space ----

	// the driver decides what the box does to the selection; the overlay is
	// only ever told where to be. both live here rather than in the scene so
	// the view carries them wherever it is instanced
	BoxSelect<UIClip> box;
	UISelectionBox selectionBox;

	// where the press landed, in content space
	Vector2 pressedAt;

	public override void _Ready()
	{
		box = new(Selection);
		selectionBox = new();
		clipsControl.AddChild(selectionBox);
	}

	void BeginBox(MouseButtonState left)
	{
		UITimeline.FlushScrollEase();
		UITimeline.BeginViewScroll();

		box.Begin(pressedAt, BoxSelect<UIClip>.ModeFor(left.PressModifiers));
	}

	// the box follows the cursor in content space, so any scrolling since it
	// began counts as reach - the anchor stays put while the view moves under
	// the cursor
	void UpdateBox()
	{
		box.Update(UITimeline.ToViewContent(InputManager.Singleton.Mouse.CurrentPosition));

		Rect2 rect = box.Rect;
		selectionBox.Cover(rect);

		// the selected clips go to the back of the tree to win the hit test,
		// which would put them over the box - so the box goes behind them again
		clipsControl.MoveChild(selectionBox, -1);

		// a boxed clip brings its linked clips with it, as a click on it does
		List<UIClip> hits = [.. UIClips.Where(c => rect.Intersects(c.GetRect()))];
		HashSet<Guid> groups = [.. hits.Where(c => c.Clip.LinkGroupId is not null).Select(c => c.Clip.LinkGroupId.Value)];

		hits.AddRange([.. UIClips.Where(c => !hits.Contains(c) && c.Clip.LinkGroupId is Guid id && groups.Contains(id))]);

		if (box.Apply(hits)) UpdateSelection();
	}

	void FinishBox()
	{
		box.End();
		selectionBox.Hide();
	}

	// the box never got its release, so the selection goes back to what it
	// was before the box began
	void CancelBox()
	{
		if (box is null || !box.Active) return;

		if (box.Cancel()) UpdateSelection();
		selectionBox.Hide();
	}

	// driven every frame, like a clip drag: the view scrolls while the cursor
	// pushes against an edge, and the box has to follow the view even on
	// frames where the cursor never moved
	void ProcessBox(double delta)
	{
		MouseButtonState left = InputManager.Singleton.Mouse.LeftButton;

		// belt and braces: if the gesture ended by any route that did not come
		// back through FinishBox or CancelBox, stop driving it
		if (!left.HasCapture(this) || left.ClickState == MouseButtonClickState.Released)
		{
			CancelBox();
			return;
		}

		Vector2 push = UITimeline.GetEdgePush(InputManager.Singleton.Mouse.CurrentPosition);
		if (push != Vector2.Zero) UITimeline.ScrollView(push * (float)delta);

		UpdateBox();
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
		clip.GraphRequested += c => GraphRequested?.Invoke(c);

		clipsControl.AddChild(clip);

		return clip;
	}

	public void RemoveUIClip(UIClip c)
	{
		UIClips.Remove(c);
		if (c == handleClip) handleClip = null;
		box?.Forget(c);
		c.QueueFree();
	}

	// ---- which clip wears the handles ----

	// only the clip nearest the cursor shows its drag handles, selected or
	// not - every clip showing them would be a thicket, and two touching
	// clips would put handles in the crevice
	UIClip handleClip;

	void SetHandleClip(UIClip clip)
	{
		UIClip previous = handleClip;
		handleClip = clip;

		previous?.SetHandlesEnabled(false);
		clip?.SetHandlesEnabled(true);

		// its handles hang over its neighbours, so it goes last in the tree
		// to win the hit test
		if (clip is not null) clipsControl.MoveChild(clip, -1);
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
	// public void AddClip(IMedia media, int channelIndex)

	// all selected clips
	public class ClipsSelection : Selection<UIClip>
	{
		public Time EarliestPosition => this.Min(c => c.Clip.Start);
		public Time LatestPosition => this.Max(c => c.Clip.End);

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

	// the selection as data, for whoever shows it - the inspector, through
	// the timeline. raised only when the set really changed, however many
	// times the view re-ran its selection
	public event EventHandler SelectionChanged;
	public IReadOnlyList<Clip> SelectedClips => [.. Selection.Select(c => c.Clip)];

	readonly List<UIClip> notifiedSelection = [];

	void NotifySelection()
	{
		if (notifiedSelection.Count == Selection.Count && notifiedSelection.All(Selection.Contains)) return;

		notifiedSelection.Clear();
		notifiedSelection.AddRange(Selection);
		SelectionChanged?.Invoke(this, EventArgs.Empty);
	}

	void UpdateSelection()
	{
		NotifySelection();

		// highlight current selection, unhighlight any other clips
		foreach (UIClip c in UIClips)
		{
			c.Selected = Selection.Contains(c);

			// a selected clip's drag handles hang outside its rect, over
			// whatever neighbour sits there. godot picks the last child first,
			// so the selection goes to the back of the tree to win that
			if (c.Selected) clipsControl.MoveChild(c, -1);
		}
	}

	// the clip nearest the cursor wears the handles - nearest, not hovered,
	// so they turn up as the cursor comes towards a clip rather than only
	// once it is over it. left alone during a drag, since a handle in the
	// middle of one must not vanish
	void FollowCursorWithHandles()
	{
		if (dragClip is not null || edgeDrag is not null || box.Active || ghosts.Count > 0) return;

		Vector2 cursor = InputManager.Singleton.Mouse.CurrentPosition;
		if (!UITimeline.ViewContains(cursor)) return;

		// handles hang over the neighbouring clip; while the cursor is on
		// them they stay put, or reaching for one would hand them away
		if (handleClip is not null && handleClip.HandlesContain(cursor)) return;

		Vector2 point = UITimeline.ToViewContent(cursor);

		UIClip closest = null;
		float best = float.MaxValue;

		foreach (UIClip c in UIClips)
		{
			float distance = DistanceSquared(point, c.GetRect());

			if (distance < best)
			{
				best = distance;
				closest = c;
			}
		}

		if (closest is not null && closest != handleClip) SetHandleClip(closest);
	}

	// zero inside the rect, else the square of the gap to its nearest edge
	static float DistanceSquared(Vector2 point, Rect2 rect)
	{
		float dx = Mathf.Max(Mathf.Max(rect.Position.X - point.X, 0f), point.X - rect.End.X);
		float dy = Mathf.Max(Mathf.Max(rect.Position.Y - point.Y, 0f), point.Y - rect.End.Y);

		return dx * dx + dy * dy;
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

		FollowCursorWithHandles();
		NotifySelection();

		if (box.Active)
		{
			ProcessBox(delta);
			return;
		}

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
		long timeOrder = move.timeDelta >= Time.Zero ? -1 : 1;

		List<UIClip> ordered = [.. Selection
			.OrderBy(c => channelOrder * c.Clip.Channel.Index)
			.ThenBy(c => timeOrder * c.Clip.Start.Ticks)];

		// resolve every target before moving anything, so relocating one clip
		// can never perturb another clip's own target
		List<(Clip clip, Time start, Channel channel)> moves = [];
		foreach (UIClip s in ordered)
		{
			int targetIndex = s.Clip.Channel.Index + move.channelDelta;

			Channel targetChannel = s.Clip is VideoClip
				? UITimeline.Timeline.VideoChannels[targetIndex]
				: UITimeline.Timeline.AudioChannels[targetIndex];

			moves.Add((s.Clip, s.Clip.Start + move.timeDelta, targetChannel));
		}

		// edit underlying clip data
		foreach ((Clip clip, Time start, Channel channel) in moves) clip.Move(start, channel);

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

	(Time timeDelta, int channelDelta) GetClipMove(UIClip uiClip, (Vector2 start, Vector2 delta) drag)
	{
		int channelDragDelta = GetChannelDragDelta(uiClip, drag);

		Time timeDelta = UITimeline.PixelsToTimeSpan(drag.delta.X);

		// magnet: the grabbed clip's start is what lands on the frame grid,
		// and every edge in the selection can catch on another clip or the
		// playhead. the selection's own edges are not targets
		timeDelta = UITimeline.SnapDelta(
			timeDelta,
			Selection.SelectMany(c => new[] { c.Clip.Start, c.Clip.End }),
			uiClip.Clip.Start,
			Selection.Select(c => c.Clip),
			includePlayhead: true,
			out Time? lineAt
		);

		// don't let the selection move past zero
		if (Selection.EarliestPosition + timeDelta < Time.Zero)
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
		Timeshift
	}

	sealed class EdgeDrag
	{
		public UIDragHandle Handle;
		public EdgeDragKind Kind;
		public bool Left;

		// every selected clip whose edge sits where the grabbed one does. they
		// all move together, like a multi-edit trim, so linked video and audio
		// that start together stay together
		public List<UIClip> Clips;

		// where the edge was when grabbed, and where it currently previews
		public Time EdgeAtStart;
		public Time Edge;

		public Vector2 ScrollAtStart;
	}

	EdgeDrag edgeDrag;

	public void BeginEdgeDrag(UIClip uiClip, UIDragHandle handle, EdgeDragKind kind)
	{
		// one drag at a time. a clip drag holds the same button, so this
		// cannot overlap one either
		if (edgeDrag is not null || dragClip is not null) return;

		// an unselected clip's handle takes the selection, like a click on it would
		SelectClip(uiClip, SelectionMode.ExclusiveIfUnselected);

		UITimeline.FlushScrollEase();
		UITimeline.BeginViewScroll();

		// hold the content open for the duration - see BeginDrag
		if (ClipsBounds is not null) ClipsBounds.GrowOnly = true;

		bool left = handle.Side == UIDragHandle.HandleSide.Left;
		Time edge = left ? uiClip.Clip.Start : uiClip.Clip.End;

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
		Time candidate = edgeDrag.EdgeAtStart + UITimeline.PixelsToTimeSpan(travel);

		// magnet first, then what the clips themselves allow. a snap the clip
		// cannot reach is not a snap, so the line goes if the clamp moved it
		candidate = UITimeline.SnapPoint(candidate, edgeDrag.Clips.Select(c => c.Clip), includePlayhead: true, out Time? lineAt);
		Time edge = ClampEdge(candidate);

		UITimeline.SnapLine = edge == candidate ? lineAt : null;

		edgeDrag.Edge = edge;

		foreach (UIClip c in edgeDrag.Clips)
		{
			if (edgeDrag.Left) c.PreviewSpan(edge, c.Clip.End);
			else c.PreviewSpan(c.Clip.Start, edge);

			// a timeshift is a speed change in the making - show the speed it
			// would land on, not the one it still has
			if (edgeDrag.Kind == EdgeDragKind.Timeshift)
			{
				Time duration = edgeDrag.Left ? c.Clip.End - edge : edge - c.Clip.Start;
				c.PreviewSpeed(new Rational(c.Clip.ContentDuration.Ticks, duration.Ticks));
			}
		}
	}

	// the tightest range every clip in the drag can accept: nothing shorter
	// than the minimum duration, nothing before zero, and no extend past
	// either end of the content - a timeshift has no content limit, it stretches
	Time ClampEdge(Time edge)
	{
		foreach (UIClip c in edgeDrag.Clips)
		{
			Clip clip = c.Clip;

			if (edgeDrag.Left)
			{
				Time min = Time.Zero;

				if (edgeDrag.Kind == EdgeDragKind.Extend)
				{
					Time limit = clip.HeadExtendLimit;
					if (limit != Time.MaxValue && clip.Start - limit > min) min = clip.Start - limit;
				}

				Time max = UITimeline.LatestStartBefore(clip.End);

				if (edge < min) edge = min;
				if (edge > max) edge = max;
			}
			else
			{
				Time min = UITimeline.EarliestEndAfter(clip.Start);

				if (edgeDrag.Kind == EdgeDragKind.Extend)
				{
					Time limit = clip.TailExtendLimit;
					if (limit != Time.MaxValue && edge > clip.End + limit) edge = clip.End + limit;
				}

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
		string what = drag.Kind == EdgeDragKind.Timeshift ? "Retime" : "Trim";
		using (Transaction.Scope change = UITimeline.History.Begin(drag.Clips.Count == 1 ? $"{what} clip" : $"{what} {drag.Clips.Count} clips"))
		{
			foreach (UIClip c in drag.Clips) ApplyEdge(c.Clip, drag);
			change.Commit();
		}

		// an extend overwrites whatever it grows over. clips that were eaten
		// whole are off their channel now; ones that were split are replaced
		// by fragments Refresh will pick up
		foreach (UIClip c in UIClips.Where(c => c.Clip.Channel is null).ToList()) RemoveUIClip(c);

		Refresh();
		SettleAfterDrag();
	}

	static void ApplyEdge(Clip clip, EdgeDrag drag)
	{
		Time delta = drag.Edge - (drag.Left ? clip.Start : clip.End);
		if (delta == Time.Zero) return;

		switch (drag.Kind, drag.Left)
		{
			case (EdgeDragKind.Extend, true):
				if (delta < Time.Zero) clip.ExtendStart(-delta);
				else clip.TrimStart(delta);
				break;

			case (EdgeDragKind.Extend, false):
				if (delta > Time.Zero) clip.ExtendEnd(delta);
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
