using EditSharp.Audio.Analysis;
using EditSharp.Caching.Proxy;
using EditSharp.Components;
using EditSharp.Components.Media;
using EditSharp.History;
using EditSharp.Video;
using EditSharpGUI.Scripts.Input;
using EditSharpGUI.Scripts.UI;
using EditSharpGUI.Scripts.UI.ContextMenu;
using EditSharpGUI.Scripts.UI.DragDrop;
using EditSharpGUI.Scripts.UI.Thumbnails;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// the project's media as tiles: what the library holds and the project's
// timelines, filtered by the tab along the top, the Filter menu and the
// search field, in the order the Sort menu says. it brings files in, and
// answers every gesture on a tile - selecting, dragging out, renaming, the
// menu. it never sees the timeline or the inspector: it raises events and
// the page wires them. laid out in MediaViewer.tscn
public partial class MediaViewer : Control, IDropTarget
{
	[ExportGroup("Controls")]
	[Export] TabBar tabs;
	[Export] Button sortButton;
	[Export] Button filterButton;
	[Export] LineEdit search;
	[Export] ScrollContainer scroll;
	[Export] HFlowContainer flow;
	[Export] Control addTile;
	[Export] Control addContent;
	[Export] Label empty;
	[Export] Control overlay;

	[ExportGroup("Scenes")]
	[Export] PackedScene itemScene;

	[ExportGroup("Menus")]
	[Export] ContextMenu tileMenu;
	[Export] ContextMenu viewerMenu;
	[Export] ContextMenu sortMenu;
	[Export] ContextMenu filterMenu;

	public enum Kind { Video, Image, Audio, Timeline }
	public enum Tab { Video, Images, Audio, Timelines, All }
	public enum SortKey { Name, Added, Duration, FrameRate, Tags, Online, Resolution }

	// the project whose media and timelines are shown
	public Project Project
	{
		get => project;
		set
		{
			if (project is not null)
			{
				project.Media.Changed -= OnLibraryChanged;
				project.TimelinesChanged -= OnLibraryChanged;
			}

			project = value;

			if (project is not null)
			{
				project.Media.Changed += OnLibraryChanged;
				project.TimelinesChanged += OnLibraryChanged;
			}

			if (IsNodeReady()) Rebuild();
		}
	}
	Project project;

	// where the tiles get their pictures and waveforms
	public MediaThumbnails Thumbnails
	{
		get => thumbnails;
		set
		{
			if (thumbnails is not null) thumbnails.Updated -= OnThumbnail;
			thumbnails = value;
			if (thumbnails is not null) thumbnails.Updated += OnThumbnail;
			foreach (UIMediaItem tile in tiles.Values) tile.RefreshPicture();
		}
	}
	MediaThumbnails thumbnails;

	// the selected media or timelines, as data
	public event Action<IReadOnlyList<object>> SelectionChanged;
	public IReadOnlyList<object> Selected => [.. selection.Select(t => t.Subject)];

	// a media wants placing at the playhead
	public event Action<IReadOnlyList<IMedia>> AddToTimelineRequested;

	// a tile was double-clicked: a media or a timeline to view
	public event Action<object> OpenRequested;

	// ---- state ----

	Tab tab = Tab.Video;
	SortKey sortKey = SortKey.Added;
	bool descending;

	bool showUsed = true, showUnused = true, showOffline = true;
	readonly HashSet<ProxyState> shownProxyStates = [.. Enum.GetValues<ProxyState>()];
	bool requireAudio, requireAlpha, matchFrameRate, matchResolution;
	readonly HashSet<string> tagFilter = [];

	// the tab, the sort and every filter, remembered with the project
	public System.Text.Json.Nodes.JsonObject SaveState() => new()
	{
		["tab"] = tab.ToString(),
		["sort"] = sortKey.ToString(),
		["descending"] = descending,
		["used"] = showUsed,
		["unused"] = showUnused,
		["offline"] = showOffline,
		["proxyStates"] = new System.Text.Json.Nodes.JsonArray([.. shownProxyStates.Select(s => (System.Text.Json.Nodes.JsonNode)s.ToString())]),
		["audio"] = requireAudio,
		["alpha"] = requireAlpha,
		["frameRate"] = matchFrameRate,
		["resolution"] = matchResolution,
		["tags"] = new System.Text.Json.Nodes.JsonArray([.. tagFilter.Select(t => (System.Text.Json.Nodes.JsonNode)t)]),
		["search"] = search?.Text ?? "",
	};

	public void RestoreState(System.Text.Json.Nodes.JsonObject state)
	{
		if (state is null) return;

		if (Enum.TryParse(state["tab"]?.GetValue<string>(), out Tab t)) { tab = t; if (tabs is not null) tabs.CurrentTab = (int)t; }
		if (Enum.TryParse(state["sort"]?.GetValue<string>(), out SortKey k)) sortKey = k;
		descending = state["descending"]?.GetValue<bool>() ?? descending;
		showUsed = state["used"]?.GetValue<bool>() ?? showUsed;
		showUnused = state["unused"]?.GetValue<bool>() ?? showUnused;
		showOffline = state["offline"]?.GetValue<bool>() ?? showOffline;
		requireAudio = state["audio"]?.GetValue<bool>() ?? requireAudio;
		requireAlpha = state["alpha"]?.GetValue<bool>() ?? requireAlpha;
		matchFrameRate = state["frameRate"]?.GetValue<bool>() ?? matchFrameRate;
		matchResolution = state["resolution"]?.GetValue<bool>() ?? matchResolution;

		if (state["proxyStates"] is System.Text.Json.Nodes.JsonArray states)
		{
			shownProxyStates.Clear();
			foreach (System.Text.Json.Nodes.JsonNode s in states) if (Enum.TryParse(s?.GetValue<string>(), out ProxyState p)) shownProxyStates.Add(p);
		}

		tagFilter.Clear();
		foreach (System.Text.Json.Nodes.JsonNode tag in state["tags"]?.AsArray() ?? []) if (tag?.GetValue<string>() is { } text) tagFilter.Add(text);

		if (search is not null) search.Text = state["search"]?.GetValue<string>() ?? "";

		Rebuild();
	}

	readonly Dictionary<object, UIMediaItem> tiles = [];
	readonly List<UIMediaItem> shown = [];
	readonly Selection<UIMediaItem> selection = new();
	BoxSelect<UIMediaItem> box;
	UISelectionBox selectionBox;

	public override void _Ready()
	{
		box = new(selection);
		selectionBox = new();
		(overlay ?? this).AddChild(selectionBox);

		if (tabs is not null) tabs.TabChanged += t => { tab = (Tab)t; Rebuild(); };
		if (sortButton is not null) sortButton.Pressed += () => ShowSortMenu(sortButton);
		if (filterButton is not null) filterButton.Pressed += () => ShowFilterMenu(filterButton);
		if (search is not null) search.TextChanged += _ => Rebuild();

		if (scroll is not null) scroll.GuiInput += OnEmptyInput;

		if (addTile is not null)
		{
			addTile.GuiInput += e => { if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }) ImportDialog(); };
			addTile.MouseDefaultCursorShape = CursorShape.PointingHand;
			addTile.TooltipText = "Import media files";
		}

		ProxyCache.StatusChanged += OnProxyStatus;
		Rebuild();
	}

	public override void _EnterTree() => InputManager.Singleton.Keyboard.Register(this, OnShortcut);

	public override void _ExitTree()
	{
		InputManager.Singleton.Keyboard.Unregister(this);
		ProxyCache.StatusChanged -= OnProxyStatus;
	}

	void OnLibraryChanged() => Rebuild();

	void OnThumbnail(object subject)
	{
		if (tiles.TryGetValue(subject, out UIMediaItem tile)) tile.RefreshPicture();
	}

	// ---- what is shown ----

	// the media or timelines the tab lists, before filtering
	IEnumerable<object> Subjects()
	{
		if (project is null) yield break;

		switch (tab)
		{
			case Tab.Video:
				foreach (IMedia m in project.Media) if (m is VideoMedia && KindOf(m) == Kind.Video) yield return m;
				break;

			case Tab.Images:
				foreach (IMedia m in project.Media) if (m is VideoMedia && KindOf(m) == Kind.Image) yield return m;
				break;

			case Tab.Audio:
				foreach (IMedia m in project.Media.For(typeof(AudioMedia))) yield return m;
				break;

			case Tab.Timelines:
				foreach (Timeline t in project.Timelines) yield return t;
				break;

			default:
				foreach (IMedia m in project.Media) yield return m;
				foreach (Timeline t in project.Timelines) yield return t;
				break;
		}
	}

	public Kind KindOf(object subject) => subject switch
	{
		Timeline => Kind.Timeline,
		AudioMedia => Kind.Audio,
		VideoMedia video => video.TryGetInfo(out MediaInfo info) ? (info.IsStillImage ? Kind.Image : Kind.Video)
			: ImageExtensions.Contains(Path.GetExtension(video.Path).ToLowerInvariant()) ? Kind.Image : Kind.Video,
		_ => Kind.Video
	};

	public string NameOf(object subject) => subject switch
	{
		IMedia media => media.Name,
		Timeline timeline => project is not null && project.Timelines.ToList().IndexOf(timeline) is int i && i >= 0 ? $"Timeline {i + 1}" : "Timeline",
		_ => subject?.ToString() ?? ""
	};

	static MediaInfo? InfoOf(object subject) => subject is IMedia m && m.TryGetInfo(out MediaInfo info) ? info : null;

	static bool IsOffline(object subject) => subject is IMedia m && !string.IsNullOrEmpty(m.Path) && !File.Exists(m.Path);

	bool Passes(object subject)
	{
		if (search is not null && search.Text.Length > 0 && !NameOf(subject).Contains(search.Text, StringComparison.OrdinalIgnoreCase)) return false;

		bool used = subject switch { IMedia m => m.UsedBy.Count > 0, Timeline t => t.UsedBy.Count > 0, _ => false };
		if (used && !showUsed) return false;
		if (!used && !showUnused) return false;
		if (!showOffline && IsOffline(subject)) return false;

		if (subject is IMedia media)
		{
			MediaInfo? info = InfoOf(media);

			if (KindOf(media) == Kind.Video && !shownProxyStates.Contains(ProxyStateOf(media))) return false;
			if (requireAudio && tab != Tab.Audio && !(media is AudioMedia || info?.HasAudio == true)) return false;
			if (requireAlpha && info?.HasAlpha != true) return false;
			if (matchFrameRate && info?.FrameRate != project.RenderSettings.Framerate) return false;
			if (matchResolution && !(info is MediaInfo i && i.Width == (int)project.RenderSettings.Resolution.X && i.Height == (int)project.RenderSettings.Resolution.Y)) return false;
			if (tagFilter.Count > 0 && !media.Tags.Any(tagFilter.Contains)) return false;
		}
		else if (tagFilter.Count > 0) return false;

		return true;
	}

	IComparable SortValue(object subject) => sortKey switch
	{
		SortKey.Name => NameOf(subject),
		SortKey.Duration => subject is IMedia m && m.TryGetNaturalLength(out Time? l) && l is Time d ? d.Ticks : subject is Timeline t ? t.Duration.Ticks : long.MaxValue,
		SortKey.FrameRate => InfoOf(subject)?.FrameRate ?? new Rational(long.MaxValue),
		SortKey.Tags => subject is IMedia tagged && tagged.Tags.Count > 0 ? string.Join(",", tagged.Tags.OrderBy(x => x)) : "￿",
		SortKey.Online => IsOffline(subject) ? 1 : 0,
		SortKey.Resolution => InfoOf(subject) is MediaInfo i ? (long)i.Width * i.Height : long.MaxValue,
		_ => AddedIndex(subject)
	};

	int AddedIndex(object subject) => subject switch
	{
		IMedia m => project.Media.ToList().FindIndex(e => ReferenceEquals(e, m) || (e is VideoMedia v && ReferenceEquals(v.Audio, m))),
		Timeline t => project.Timelines.ToList().IndexOf(t),
		_ => 0
	};

	// the tiles, from the data as it stands: one per subject that passes,
	// in sort order, made once and kept while the subject is in the project
	public void Rebuild()
	{
		if (flow is null || !IsNodeReady()) return;

		List<object> subjects = [.. Subjects().Where(Passes)];
		var ordered = subjects.OrderBy(SortValue).ThenBy(NameOf);
		if (descending) ordered = subjects.OrderByDescending(SortValue).ThenByDescending(NameOf);
		subjects = [.. ordered];

		HashSet<object> present = [.. Subjects()];
		foreach (object gone in tiles.Keys.Where(k => !present.Contains(k) && !Everything().Contains(k)).ToList())
		{
			UIMediaItem tile = tiles[gone];
			selection.Remove(tile);
			box?.Forget(tile);
			tiles.Remove(gone);
			tile.QueueFree();
		}

		foreach (UIMediaItem tile in shown) if (tile.GetParent() == flow) flow.RemoveChild(tile);
		shown.Clear();

		Vector2 pictureSize = PictureSize();
		int index = addTile is not null && addTile.GetParent() == flow ? addTile.GetIndex() + 1 : 0;

		// the add tile's box is the size of a tile's picture, over the same name row
		if (addContent is not null) addContent.CustomMinimumSize = pictureSize;
		if (addTile is not null) addTile.CustomMinimumSize = new Vector2(pictureSize.X, 0f);

		foreach (object subject in subjects)
		{
			if (!tiles.TryGetValue(subject, out UIMediaItem tile))
			{
				tile = itemScene.Instantiate<UIMediaItem>();
				tile.Setup(this, subject);
				tiles[subject] = tile;
				if (subject is IMedia media) media.InfoAvailable += OnInfo;
			}

			tile.SetPictureSize(pictureSize);
			flow.AddChild(tile);
			flow.MoveChild(tile, index++);
			tile.Refresh();
			tile.Selected = selection.Contains(tile);
			shown.Add(tile);
		}

		if (empty is not null) empty.Visible = shown.Count == 0;

		foreach (KeyValuePair<string, double> build in proxyProgress)
			foreach (UIMediaItem tile in shown) if (tile.Media?.Path == build.Key) tile.SetProxyProgress(build.Value);

		NotifySelection();
	}

	IEnumerable<object> Everything()
	{
		if (project is null) yield break;
		foreach (IMedia m in project.Media) { yield return m; if (m is VideoMedia { Audio: { } a }) yield return a; }
		foreach (Timeline t in project.Timelines) yield return t;
	}

	// a probe finished, on another thread: kinds and details may have changed
	void OnInfo(IMedia media) => Callable.From(() =>
	{
		if (IsInsideTree()) { Rebuild(); StartProxyIfVideo(media); }
	}).CallDeferred();

	// the project's aspect on a 120 pixel long side
	Vector2 PictureSize()
	{
		const float longSide = 120f;
		float aspect = project is not null && project.RenderSettings.Resolution.Y > 0f
			? project.RenderSettings.Resolution.X / project.RenderSettings.Resolution.Y
			: 16f / 9f;

		return aspect >= 1f ? new Vector2(longSide, Mathf.Round(longSide / aspect)) : new Vector2(Mathf.Round(longSide * aspect), longSide);
	}

	// ---- importing ----

	static readonly HashSet<string> VideoExtensions = [".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v", ".mts", ".m2ts", ".wmv", ".flv", ".mxf", ".gif", ".mpg", ".mpeg", ".ts"];
	static readonly HashSet<string> ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".webp", ".tga", ".exr", ".psd", ".dds", ".heic", ".avif"];
	static readonly HashSet<string> AudioExtensions = [".wav", ".mp3", ".flac", ".aac", ".m4a", ".ogg", ".opus", ".aiff", ".aif", ".wma", ".ac3"];

	public void ImportDialog()
	{
		FileDialog dialog = new()
		{
			FileMode = FileDialog.FileModeEnum.OpenFiles,
			Access = FileDialog.AccessEnum.Filesystem,
			UseNativeDialog = true,
			Title = "Import media",
		};

		AddChild(dialog);
		dialog.FilesSelected += files =>
		{
			dialog.QueueFree();
			Import(files);
			if (files.Length > 0) ShowTabFor(files[0]);
		};
		dialog.Canceled += dialog.QueueFree;
		dialog.PopupCentered(new Vector2I(900, 600));
	}

	// files become media by their extension; a file of no known kind is
	// taken as video and corrected once its probe says otherwise. every
	// video starts building its proxy once the probe confirms it is one
	public IReadOnlyList<IMedia> Import(IEnumerable<string> paths)
	{
		if (project is null) return [];

		List<IMedia> added = [];

		using (Transaction.Scope change = project.History.Begin("Import media"))
		{
			foreach (string path in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
			{
				string extension = Path.GetExtension(path).ToLowerInvariant();
				if (project.Media.Any(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase))) continue;

				IMedia media = Transaction.Suppressed<IMedia>(() => AudioExtensions.Contains(extension) ? new AudioMedia { Path = path } : new VideoMedia { Path = path });
				project.Media.Add(media);
				added.Add(media);
			}

			change.Commit();
		}

		foreach (IMedia media in added)
		{
			bool known = VideoExtensions.Contains(Path.GetExtension(media.Path).ToLowerInvariant()) || ImageExtensions.Contains(Path.GetExtension(media.Path).ToLowerInvariant()) || media is AudioMedia;
			if (!known) media.InfoAvailable += CorrectKind;
			if (media.TryGetInfo(out _)) StartProxyIfVideo(media);
		}

		Rebuild();
		return added;
	}

	// a file of unknown extension that turned out to be audio only
	void CorrectKind(IMedia media) => Callable.From(() =>
	{
		media.InfoAvailable -= CorrectKind;
		if (project is null || media is not VideoMedia || !media.TryGetInfo(out MediaInfo info) || info.HasVideo || !info.HasAudio) return;

		using Transaction.Scope change = project.History.Begin("Import media");
		project.Media.Remove(media);
		project.Media.Add(Transaction.Suppressed<IMedia>(() => new AudioMedia { Path = media.Path }));
		change.Commit();
	}).CallDeferred();

	// ---- proxies ----

	readonly Dictionary<string, ProxyStatus> proxyStatus = [];
	readonly Dictionary<string, double> proxyProgress = [];
	readonly Dictionary<string, CancellationTokenSource> proxyBuilds = [];

	ProxyState ProxyStateOf(IMedia media)
	{
		if (proxyStatus.TryGetValue(media.Path, out ProxyStatus status)) return status.State;
		if (proxyBuilds.ContainsKey(media.Path) || ProxyCache.IsBuilding(media.Path)) return ProxyState.Building;
		if (ProxyCache.TryGetEntry(media.Path, out _)) return ProxyState.Complete;

		// asked once; the answer arrives through StatusChanged or here
		if (File.Exists(media.Path) && !proxyAsked.Add(media.Path)) return ProxyState.NotCached;
		if (File.Exists(media.Path)) _ = ProxyCache.GetStatusAsync(media.Path).ContinueWith(t =>
		{
			if (t.IsCompletedSuccessfully) Callable.From(() => { proxyStatus[media.Path] = t.Result; Rebuild(); }).CallDeferred();
		}, TaskScheduler.Default);

		return ProxyState.NotCached;
	}

	readonly HashSet<string> proxyAsked = [];

	void OnProxyStatus(object sender, ProxyStatusChangedEventArgs e) => Callable.From(() =>
	{
		proxyStatus[e.SourcePath] = e.Status;
		if (e.Status.State is ProxyState.Complete or ProxyState.Failed or ProxyState.NotCached) { proxyProgress.Remove(e.SourcePath); proxyBuilds.Remove(e.SourcePath); }
		if (IsInsideTree()) Rebuild();
	}).CallDeferred();

	void StartProxyIfVideo(IMedia media)
	{
		StartAnalysisIfAudio(media);

		if (media is not VideoMedia || !media.TryGetInfo(out MediaInfo info) || !info.HasVideo || info.IsStillImage) return;
		if (ProxyStateOf(media) is ProxyState.Complete or ProxyState.Building or ProxyState.Queued) return;
		StartProxy(media.Path);
	}

	// a file with sound gets its frequency-domain analysis right away, so
	// waveforms are there when the media is first placed
	static void StartAnalysisIfAudio(IMedia media)
	{
		if (media is null || string.IsNullOrEmpty(media.Path) || !File.Exists(media.Path)) return;
		if (media.TryGetInfo(out MediaInfo info) && !info.HasAudio && media is not AudioMedia) return;
		if (AudioAnalysisCache.TryGet(media.Path, out _) || AudioAnalysisCache.IsPending(media.Path)) return;

		_ = AudioAnalysisCache.GetAsync(media.Path).ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
	}

	void StartProxy(string path)
	{
		if (proxyBuilds.ContainsKey(path) || !File.Exists(path)) return;

		CancellationTokenSource cts = new();
		proxyBuilds[path] = cts;
		proxyProgress[path] = 0d;

		Progress<double> progress = new(p => Callable.From(() =>
		{
			proxyProgress[path] = p;
			foreach (UIMediaItem tile in shown) if (tile.Media?.Path == path) tile.SetProxyProgress(p);
		}).CallDeferred());

		_ = ProxyCache.BuildAsync(path, null, progress, ct: cts.Token).ContinueWith(t =>
		{
			_ = t.Exception;
			Callable.From(() =>
			{
				proxyBuilds.Remove(path);
				proxyProgress.Remove(path);
				foreach (UIMediaItem tile in shown) if (tile.Media?.Path == path) tile.SetProxyProgress(null);
				proxyStatus.Remove(path);
				proxyAsked.Remove(path);
				if (IsInsideTree()) Rebuild();
			}).CallDeferred();
		}, TaskScheduler.Default);

		Rebuild();
	}

	void CancelProxy(string path)
	{
		if (proxyBuilds.Remove(path, out CancellationTokenSource cts)) cts.Cancel();
	}

	// ---- selection ----

	public void ClaimKeyboard() => InputManager.Singleton.Keyboard.Capture(this);

	void UpdateSelection()
	{
		foreach (UIMediaItem tile in tiles.Values) tile.Selected = selection.Contains(tile);
		NotifySelection();
	}

	readonly List<UIMediaItem> notified = [];

	void NotifySelection()
	{
		if (notified.Count == selection.Count && notified.All(selection.Contains)) return;

		notified.Clear();
		notified.AddRange(selection);
		SelectionChanged?.Invoke(Selected);
	}

	public void SelectAll()
	{
		selection.Select(shown, SelectionMode.Inclusive);
		UpdateSelection();
	}

	public void DeselectAll()
	{
		selection.Clear();
		UpdateSelection();
	}

	internal void PressTile(UIMediaItem tile, Modifiers modifiers)
	{
		ClaimKeyboard();

		if (modifiers.Shift) selection.Select(tile, SelectionMode.Inclusive);
		else if (modifiers.Control) selection.Deselect(tile);
		else selection.Select(tile, SelectionMode.ExclusiveIfUnselected);

		UpdateSelection();
	}

	internal void ClickTile(UIMediaItem tile)
	{
		selection.Select(tile, SelectionMode.Exclusive);
		UpdateSelection();
	}

	internal void OpenTile(UIMediaItem tile)
	{
		ClaimKeyboard();
		selection.Select(tile, SelectionMode.Exclusive);
		UpdateSelection();
		OpenRequested?.Invoke(tile.Subject);
	}

	// ---- dragging tiles out ----

	internal void BeginTileDrag(UIMediaItem tile)
	{
		if (!selection.Contains(tile)) { selection.Select(tile, SelectionMode.Exclusive); UpdateSelection(); }

		List<UIMediaItem> dragged = [.. shown.Where(selection.Contains)];
		if (dragged.Count == 0) dragged = [tile];

		DragPayload payload = dragged.All(t => t.Timeline is not null)
			? new TimelinePayload([.. dragged.Select(t => t.Timeline)])
			: new MediaPayload([.. dragged.Where(t => t.Media is not null).Select(t => t.Media)]);

		Control ghost = MakeGhost(tile, dragged.Count);
		DragDrop.Begin(payload, ghost, ghost.Size / 2f, this);
	}

	Control MakeGhost(UIMediaItem tile, int count)
	{
		Vector2 size = PictureSize() * 0.75f;
		Panel ghost = new() { Size = size, ThemeTypeVariation = "MediaContent", Modulate = new Color(1f, 1f, 1f, 0.75f) };

		if (thumbnails?.GetFrame(tile.Subject, 240, 135) is Texture2D texture)
		{
			TextureRect picture = new() { Texture = texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = TextureFilterEnum.LinearWithMipmaps };
			picture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			picture.OffsetLeft = 3f; picture.OffsetTop = 3f; picture.OffsetRight = -3f; picture.OffsetBottom = -3f;
			ghost.AddChild(picture);
		}

		Label label = new() { Text = count > 1 ? $"{count} items" : NameOf(tile.Subject), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ClipText = true, ThemeTypeVariation = "MediaName" };
		label.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
		label.OffsetTop = -18f;
		ghost.AddChild(label);

		return ghost;
	}

	internal void UpdateTileDrag(UIMediaItem tile) => DragDrop.Update(InputManager.Singleton.Mouse.CurrentPosition);

	internal void FinishTileDrag(UIMediaItem tile) => DragDrop.Finish(InputManager.Singleton.Mouse.CurrentPosition);

	internal void CancelTileDrag(UIMediaItem tile) => DragDrop.Cancel();

	// ---- empty space: box select, the viewer menu ----

	void OnEmptyInput(InputEvent _)
	{
		MouseButtonState left = InputManager.Singleton.Mouse.LeftButton;

		switch (left.Action)
		{
			case MouseAction.Press:
			case MouseAction.DoubleClick:
				if (!left.Capture(scroll)) break;
				ClaimKeyboard();
				if (!left.PressModifiers.Shift && !left.PressModifiers.Control) DeselectAll();
				break;

			case MouseAction.DragStart:
				if (!left.HasCapture(scroll)) break;
				box.Begin(left.ClickStartPosition, BoxSelect<UIMediaItem>.ModeFor(left.PressModifiers));
				goto case MouseAction.DragMove;

			case MouseAction.DragMove:
				if (!left.HasCapture(scroll) || !box.Active) break;
				box.Update(InputManager.Singleton.Mouse.CurrentPosition);
				ApplyBox();
				break;

			case MouseAction.DragEnd:
				if (!left.HasCapture(scroll) || !box.Active) break;
				box.End();
				selectionBox.Visible = false;
				break;
		}

		ContextTrigger.Handle(scroll, ShowViewerMenu);
	}

	void ApplyBox()
	{
		Rect2 rect = box.Rect;
		Control host = selectionBox.GetParent() as Control;
		selectionBox.Cover(new Rect2(rect.Position - (host?.GlobalPosition ?? Vector2.Zero), rect.Size));

		if (box.Apply(shown.Where(t => t.GetGlobalRect().Intersects(rect)))) UpdateSelection();
	}

	public override void _Process(double delta)
	{
		if (!box.Active) return;

		// the release can go missing under a native menu or a lost focus
		MouseButtonState left = InputManager.Singleton.Mouse.LeftButton;
		if (!left.HasCapture(scroll) || left.ClickState == MouseButtonClickState.Released)
		{
			if (box.Cancel()) UpdateSelection();
			selectionBox.Visible = false;
		}
	}

	// ---- keyboard ----

	void OnShortcut(ShortcutEventArgs e)
	{
		switch (e.Action)
		{
			case Shortcuts.SelectAll:
				SelectAll();
				e.Handled = true;
				break;

			case Shortcuts.Delete:
			case Shortcuts.RippleDelete:
				RemoveSelected();
				e.Handled = true;
				break;

			case Shortcuts.MediaRename:
				if (selection.Count == 1) selection[0].BeginRename();
				e.Handled = true;
				break;

			case Shortcuts.MediaImport:
				ImportDialog();
				e.Handled = true;
				break;
		}
	}

	// ---- edits ----

	public void Rename(IMedia media, string name)
	{
		if (project is null) return;

		using (Transaction.Scope change = project.History.Begin("Rename media"))
		{
			media.Name = name;
			change.Commit();
		}

		Rebuild();
	}

	public void RemoveSelected()
	{
		if (project is null || selection.Count == 0) return;

		List<object> subjects = [.. selection.Select(t => t.Subject)];

		using (Transaction.Scope change = project.History.Begin(subjects.Count == 1 ? "Remove media" : $"Remove {subjects.Count} media"))
		{
			foreach (object subject in subjects)
			{
				// a soundtrack goes with its video
				if (subject is IMedia media) project.Media.Remove(project.Media.FirstOrDefault(m => ReferenceEquals(m, media) || (m is VideoMedia v && ReferenceEquals(v.Audio, media))));
				else if (subject is Timeline timeline) project.RemoveTimeline(timeline);
			}

			change.Commit();
		}

		selection.Clear();
		Rebuild();
	}

	void NewTimeline()
	{
		if (project is null) return;

		using Transaction.Scope change = project.History.Begin("New timeline");
		project.AddTimeline(new Timeline());
		change.Commit();
	}

	void SetTag(IReadOnlyList<IMedia> media, string tag, bool on)
	{
		using (Transaction.Scope change = project.History.Begin(on ? "Tag media" : "Untag media"))
		{
			foreach (IMedia m in media) { if (on) m.AddTag(tag); else m.RemoveTag(tag); }
			change.Commit();
		}

		Rebuild();
	}

	void Relink(IMedia media)
	{
		FileDialog dialog = new()
		{
			FileMode = FileDialog.FileModeEnum.OpenFile,
			Access = FileDialog.AccessEnum.Filesystem,
			UseNativeDialog = true,
			Title = $"Relink {media.Name}",
		};

		if (!string.IsNullOrEmpty(media.Path)) dialog.CurrentPath = media.Path;

		AddChild(dialog);
		dialog.FileSelected += path =>
		{
			dialog.QueueFree();

			using (Transaction.Scope change = project.History.Begin("Relink media"))
			{
				media.Path = path;
				change.Commit();
			}

			thumbnails?.Forget(media);
			Rebuild();
		};
		dialog.Canceled += dialog.QueueFree;
		dialog.PopupCentered(new Vector2I(900, 600));
	}

	// ---- menus ----

	IEnumerable<string> AllTags() => Everything().OfType<IMedia>().SelectMany(m => m.Tags).Distinct().OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase);

	internal void ShowTileMenu(UIMediaItem tile, Vector2 at) => ShowTileMenu(tile, at, null);

	internal void ShowTileMenu(UIMediaItem tile, Control button) => ShowTileMenu(tile, null, button);

	void ShowTileMenu(UIMediaItem tile, Vector2? at, Control button)
	{
		if (tileMenu is null || project is null) return;

		ClaimKeyboard();
		if (!selection.Contains(tile)) { selection.Select(tile, SelectionMode.Exclusive); UpdateSelection(); }

		List<UIMediaItem> targets = [.. shown.Where(selection.Contains)];
		if (targets.Count == 0) targets = [tile];
		List<IMedia> media = [.. targets.Select(t => t.Media).Where(m => m is not null)];

		ContextMenu menu = tileMenu.Clone();

		Wire(menu, "media.add", media.Count > 0, () => AddToTimelineRequested?.Invoke(media));
		Wire(menu, "media.rename", targets.Count == 1 && tile.Media is not null, tile.BeginRename);
		Wire(menu, "media.reveal", tile.Media is IMedia r && File.Exists(r.Path), () => OS.ShellShowInFileManager(tile.Media.Path));
		Wire(menu, "media.relink", targets.Count == 1 && tile.Media is not null, () => Relink(tile.Media));
		Wire(menu, "media.remove", true, RemoveSelected);

		if (menu.Find<ContextSubmenu>("media.tags") is ContextSubmenu tags)
		{
			tags.Enabled = media.Count > 0;
			tags.Elements.Clear();

			List<string> all = [.. AllTags()];
			if (all.Count > 0)
			{
				ContextCheckList list = new() { Id = "media.tags.list" };
				for (int i = 0; i < all.Count; i++)
				{
					list.Buttons.Add(new ContextButton { Id = $"media.tags.{all[i]}", Text = new(all[i]) });
					if (media.All(m => m.Tags.Contains(all[i]))) list.CheckedButtons.Add(i);
				}

				list.Toggled += (b, on) => SetTag(media, b.Text.Text, on);
				tags.Elements.Add(list);
				tags.Elements.Add(new ContextDivider());
			}

			ContextButton add = new() { Id = "media.tags.new", Text = new("New Tag...") };
			add.Pressed += () => PromptTag(media);
			tags.Elements.Add(add);
		}

		if (menu.Find<ContextSubmenu>("media.proxy") is ContextSubmenu proxy)
		{
			List<IMedia> videos = [.. media.Where(m => KindOf(m) == Kind.Video)];
			proxy.Enabled = videos.Count > 0;

			bool building = videos.Any(v => ProxyStateOf(v) is ProxyState.Building or ProxyState.Queued);
			bool complete = videos.Count > 0 && videos.All(v => ProxyStateOf(v) == ProxyState.Complete);

			Wire(menu, "media.proxy.build", videos.Count > 0 && !building && !complete, () => { foreach (IMedia v in videos) StartProxy(v.Path); });
			Wire(menu, "media.proxy.cancel", building, () => { foreach (IMedia v in videos) CancelProxy(v.Path); });
			Wire(menu, "media.proxy.rebuild", complete, () => { foreach (IMedia v in videos) StartProxy(v.Path); });

			// only once there is a proxy on disk to show
			string proxyFile = tile.Media is IMedia clicked ? ProxyFileOf(clicked) : null;
			if (menu.Find<ContextButton>("media.proxy.reveal") is ContextButton reveal) reveal.Visible = proxyFile is not null;
			if (menu.Find<ContextDivider>("media.proxy.revealDivider") is ContextDivider line) line.Visible = proxyFile is not null;
			Wire(menu, "media.proxy.reveal", proxyFile is not null, () => OS.ShellShowInFileManager(proxyFile));
		}

		if (button is not null) ContextMenus.ShowContextMenuBelow(menu, button);
		else ContextMenus.ShowContextMenu(menu, this, at);
	}

	// the proxy file a media has on disk: the .esrp, or a MOV proxy's finished
	// .mov (its sidecar while it is still building); null when there is none
	static string ProxyFileOf(IMedia media)
	{
		if (string.IsNullOrEmpty(media.Path) || !ProxyCache.TryGetEntry(media.Path, out ProxyEntry entry)) return null;

		string path = entry.Path;
		if (!entry.IsEsrp && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(path[..^".json".Length])) path = path[..^".json".Length];

		return File.Exists(path) ? path : null;
	}

	// a small popup with a field for the tag's name
	void PromptTag(IReadOnlyList<IMedia> media)
	{
		PopupPanel popup = new();
		LineEdit field = new() { PlaceholderText = "Tag name", CustomMinimumSize = new(180f, 0f) };
		popup.AddChild(field);
		AddChild(popup);

		field.TextSubmitted += text =>
		{
			if (!string.IsNullOrWhiteSpace(text)) SetTag(media, text.Trim(), true);
			popup.Hide();
		};
		popup.PopupHide += popup.QueueFree;

		popup.Popup(new Rect2I((Vector2I)InputManager.Singleton.Mouse.CurrentPosition.Round(), Vector2I.Zero));
		field.GrabFocus();
	}

	void ShowViewerMenu(Vector2 at)
	{
		if (viewerMenu is null) return;

		ClaimKeyboard();
		ContextMenu menu = viewerMenu.Clone();

		Wire(menu, "viewer.import", true, ImportDialog);
		Wire(menu, "viewer.newTimeline", project is not null, NewTimeline);
		Wire(menu, "viewer.selectAll", shown.Count > 0, SelectAll);

		if (menu.Find<ContextSubmenu>("viewer.sort") is ContextSubmenu sort && sortMenu is not null)
		{
			ContextMenu inner = sortMenu.Clone();
			ConfigureSort(inner);
			sort.Elements = inner.Elements;
		}

		if (menu.Find<ContextSubmenu>("viewer.filter") is ContextSubmenu filter && filterMenu is not null)
		{
			ContextMenu inner = filterMenu.Clone();
			ConfigureFilter(inner);
			filter.Elements = inner.Elements;
		}

		ContextMenus.ShowContextMenu(menu, this, at);
	}

	void ShowSortMenu(Control button)
	{
		if (sortMenu is null) return;
		ContextMenu shown = sortMenu.Clone();
		ConfigureSort(shown);
		ContextMenus.ShowContextMenuBelow(shown, button);
	}

	void ShowFilterMenu(Control button)
	{
		if (filterMenu is null) return;
		ContextMenu shown = filterMenu.Clone();
		ConfigureFilter(shown);
		ContextMenus.ShowContextMenuBelow(shown, button);
	}

	static readonly (string Id, SortKey Key)[] SortKeys =
	[
		("sort.name", SortKey.Name), ("sort.added", SortKey.Added), ("sort.duration", SortKey.Duration), ("sort.fps", SortKey.FrameRate),
		("sort.tags", SortKey.Tags), ("sort.online", SortKey.Online), ("sort.resolution", SortKey.Resolution),
	];

	void ConfigureSort(ContextMenu menu)
	{
		if (menu.Find<ContextRadioList>("sort.key") is ContextRadioList keys)
		{
			for (int i = 0; i < keys.Buttons.Count; i++)
			{
				if (SortKeys.FirstOrDefault(k => k.Id == keys.Buttons[i].Id).Key == sortKey && SortKeys.Any(k => k.Id == keys.Buttons[i].Id)) keys.SelectedButton = i;
			}

			keys.Selected += b =>
			{
				sortKey = SortKeys.FirstOrDefault(k => k.Id == b.Id).Key;
				Rebuild();
			};
		}

		if (menu.Find<ContextButton>("sort.descending") is ContextButton order)
		{
			order.Checked = descending;
			order.Pressed += () => { descending = order.Checked; Rebuild(); };
		}
	}

	void ConfigureFilter(ContextMenu menu)
	{
		Check(menu, "filter.used", showUsed, v => showUsed = v);
		Check(menu, "filter.unused", showUnused, v => showUnused = v);
		Check(menu, "filter.offline", showOffline, v => showOffline = v);
		Check(menu, "filter.audio", requireAudio, v => requireAudio = v);
		Check(menu, "filter.alpha", requireAlpha, v => requireAlpha = v);
		Check(menu, "filter.fps", matchFrameRate, v => matchFrameRate = v);
		Check(menu, "filter.resolution", matchResolution, v => matchResolution = v);

		if (menu.Find<ContextButton>("filter.audio") is ContextButton audio) audio.Visible = tab != Tab.Audio;

		if (menu.Find<ContextCheckList>("filter.proxy") is ContextCheckList proxy)
		{
			ProxyState[] states = [ProxyState.NotCached, ProxyState.Failed, ProxyState.Building, ProxyState.Complete];
			proxy.CheckedButtons.Clear();
			for (int i = 0; i < states.Length; i++) if (shownProxyStates.Contains(states[i])) proxy.CheckedButtons.Add(i);

			proxy.Toggled += (b, on) =>
			{
				int index = proxy.Buttons.IndexOf(b);
				if (index < 0 || index >= states.Length) return;

				// building covers everything in progress
				IEnumerable<ProxyState> set = states[index] == ProxyState.Building ? [ProxyState.Queued, ProxyState.Building, ProxyState.Partial] : [states[index]];
				foreach (ProxyState s in set) { if (on) shownProxyStates.Add(s); else shownProxyStates.Remove(s); }
				Rebuild();
			};
		}

		if (menu.Find<ContextSubmenu>("filter.tags") is ContextSubmenu tags)
		{
			tags.Elements.Clear();
			List<string> all = [.. AllTags()];
			tags.Enabled = all.Count > 0;

			ContextCheckList list = new() { Id = "filter.tags.list" };
			for (int i = 0; i < all.Count; i++)
			{
				list.Buttons.Add(new ContextButton { Id = $"filter.tags.{all[i]}", Text = new(all[i]) });
				if (tagFilter.Contains(all[i])) list.CheckedButtons.Add(i);
			}

			list.Toggled += (b, on) => { if (on) tagFilter.Add(b.Text.Text); else tagFilter.Remove(b.Text.Text); Rebuild(); };
			tags.Elements.Add(list);
		}
	}

	void Check(ContextMenu menu, string id, bool state, Action<bool> set)
	{
		if (menu.Find<ContextButton>(id) is not ContextButton button) return;
		button.Checked = state;
		button.Pressed += () => { set(button.Checked); Rebuild(); };
	}

	static void Wire(ContextMenu menu, string id, bool enabled, Action action)
	{
		if (menu.Find<ContextButton>(id) is not ContextButton button) return;
		button.Enabled = enabled;
		button.Pressed += () => action();
	}

	// ---- files dropped on the viewer come in ----

	public bool CanDrop(DragPayload payload, Vector2 at) => payload is FilesPayload;

	// files hovering over the viewer open the tab their kind lives on
	public void DragOver(DragPayload payload, Vector2 at)
	{
		if (payload is FilesPayload files && files.Paths.Count > 0) ShowTabFor(files.Paths[0]);
	}

	// the tab a file's kind lives on, by its extension
	void ShowTabFor(string path)
	{
		if (tabs is null) return;

		string extension = Path.GetExtension(path).ToLowerInvariant();
		Tab wanted = AudioExtensions.Contains(extension) ? Tab.Audio : ImageExtensions.Contains(extension) ? Tab.Images : Tab.Video;

		if (tabs.CurrentTab != (int)wanted) tabs.CurrentTab = (int)wanted;
	}

	public void Drop(DragPayload payload, Vector2 at)
	{
		if (payload is FilesPayload files) Import(files.Paths);
	}
}
