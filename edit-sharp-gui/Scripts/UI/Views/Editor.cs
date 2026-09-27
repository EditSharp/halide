using EditSharp.Components;
using EditSharp.Components.Channels;
using EditSharp.Components.Clips;
using EditSharp.Components.Media;
using EditSharp.History;
using EditSharp.Playback;
using EditSharpGUI.Scripts.Input;
using EditSharpGUI.Scripts.UI.DragDrop;
using EditSharpGUI.Scripts.UI.Thumbnails;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public partial class Editor : Control
{
	[ExportGroup("Views")]

	[Export] UITimeline UITimeline;
	[Export] UIPlayback UIPlayback;
	[Export] UIPlayback SourceViewer;
	[Export] TabsView LeftTabs;
	[Export] TabsView RightTabs;

	[ExportGroup("Packed Scenes")]

	[Export] PackedScene MediaViewerScene;
	[Export] PackedScene InspectorScene;
	[Export] PackedScene GraphEditorScene;

	MediaViewer mediaViewer;
	Inspector inspector;
	Control graphEditor;

	// every view the tab bars can hold, by id; kept alive while closed
	readonly Dictionary<string, (string Title, Control View)> views = [];

	// the clips' frames and waveforms. each owns a playback of its own,
	// apart from the one the user watches, so none waits on another
	ThumbnailCache thumbnails;
	WaveformCache waveforms;
	MediaThumbnails mediaThumbnails;

	// Called when the node enters the scene tree for the first time.
	// the project this editor edits: its window's
	ProjectSession session;
	Project project => session.Project;

	public override void _Ready()
	{
		session = ProjectSession.Of(this);

		mediaViewer = MediaViewerScene.Instantiate<MediaViewer>();
		inspector = InspectorScene.Instantiate<Inspector>();
		graphEditor = GraphEditorScene?.Instantiate<Control>();

		views["media"] = ("Media", mediaViewer);
		views["inspector"] = ("Inspector", inspector);
		if (graphEditor is not null) views["graph"] = ("Graph Editor", graphEditor);

		WireTabs(LeftTabs, RightTabs);
		WireTabs(RightTabs, LeftTabs);
		ResetLayout();

		thumbnails = new ThumbnailCache(project.Timeline, project.RenderSettings, project.History);
		waveforms = new WaveformCache(project.Timeline, project.History);
		mediaThumbnails = new MediaThumbnails(project.RenderSettings);
		UITimeline.Thumbnails = thumbnails;
		UITimeline.Waveforms = waveforms;

		UITimeline.SetTimeline(project.Timeline);
		UIPlayback.SetPlayback(new()
		{
			Timeline = project.Timeline,
			RenderSettings = project.RenderSettings with { Resolution = new(1280, 720), Framerate = 60, SourceMode = SourceMode.ProxiesAndSource }
		});

		// the timeline and playback never see each other - this is the only
		// place the two are joined. the playhead drives playback: grabbing it
		// scrubs, letting go picks playback back up if it was running. playback
		// drives the playhead back - but only the line, never the view. the
		// view stays wherever the user put it. the inspector follows the
		// playhead too, for what an animated value is right now
		UITimeline.PlayheadDragStarted += (_, _) => UIPlayback.BeginScrub();
		UITimeline.PlayheadDrag += (_, time) => { UIPlayback.ScrubTo(time); inspector.Playhead = time; };
		UITimeline.PlayheadDragEnded += (_, _) => UIPlayback.EndScrub();

		UIPlayback.PositionChanged += (_, time) => { UITimeline.PlayheadTime = time; inspector.Playhead = time; };

		// the inspector shows whatever the timeline or the media viewer has
		// selected, records into the project's history, and can ask for the
		// playhead to be moved to a keyframe
		inspector.History = project.History;
		inspector.Media = project.Media;
		inspector.Framerate = project.RenderSettings.Framerate;
		inspector.FrameSize = new((int)project.RenderSettings.Resolution.X, (int)project.RenderSettings.Resolution.Y);
		inspector.Playhead = UITimeline.PlayheadTime;
		UITimeline.SelectionChanged += (_, _) => inspector.ShowClips(UITimeline.SelectedClips);
		inspector.SeekRequested += (_, time) =>
		{
			UIPlayback.BeginScrub();
			UIPlayback.ScrubTo(time);
			UIPlayback.EndScrub();
			UITimeline.PlayheadTime = time;
			inspector.Playhead = time;
		};

		// the media viewer: its selection goes to the inspector, its media go
		// to the timeline, and a double-click opens a media in the source viewer
		mediaViewer.Project = project;
		mediaViewer.Thumbnails = mediaThumbnails;
		mediaViewer.SelectionChanged += selected =>
		{
			List<IMedia> media = [.. selected.OfType<IMedia>()];
			if (media.Count > 0) inspector.ShowMedia(media);
		};
		mediaViewer.AddToTimelineRequested += media => UITimeline.PlaceMedia(media, UITimeline.PlayheadTime, video: true, channelIndex: 0);
		mediaViewer.OpenRequested += ShowSource;

		// files dropped on the timeline come in through the library first
		UITimeline.FilesDropped += PlaceDroppedFiles;
		GetWindow().FilesDropped += files => DragDrop.DropFiles(files, GetViewport().GetMousePosition(), this);
		EditSharpGUI.Scripts.UI.DragDrop.Platform.Windows.OleDragHover.Install(GetWindow());

		// the graph editor opens in a window of its own in a later step
		UITimeline.GraphRequested += clip => GD.Print($"Graph editor for '{clip.Clip.Name}': not built yet.");

		// the project's history is the one stray writes fall into, and an undo
		// or redo moves the data with no view watching - so the timeline
		// re-reads it. a commit needs nothing: the view that made the change
		// already updated itself. (not on every commit: a model call made
		// outside a transaction commits each primitive as it goes, and a
		// view re-reading the data between two of them would see it half
		// moved.) the inspector is the exception - it edits clips the
		// timeline shows - so its edits are what tell the timeline to re-read
		History history = project.History;
		History.Active = history;
		history.Changed += (_, e) => { if (e.Action != HistoryAction.Commit) UITimeline.Reconcile(); };
		inspector.Edited += (_, _) => UITimeline.Reconcile();

		// waveforms follow inspector edits as they happen: a gain slider redraws the strip live
		inspector.Edited += (_, _) => waveforms?.RefreshEdited();

		// the media viewer re-reads names and tags edited in the inspector
		inspector.Edited += (_, _) => mediaViewer.Rebuild();
		history.Changed += (_, e) => { if (e.Action != HistoryAction.Commit) mediaViewer.Rebuild(); };

		// the preview is a still picture while stopped, so anything that
		// changes the timeline shows it again: every history entry (a drag,
		// a trim, an undo), and live inspector edits as they happen. and
		// once to begin with, so there's a picture before anything is played
		history.Changed += (_, _) => UIPlayback.RefreshFrame();
		inspector.Edited += (_, _) => UIPlayback.RefreshFrame();
		UIPlayback.RefreshFrame();

		// page-wide shortcuts. every key that nothing closer wanted climbs up
		// to here, since this page is above every view in it
		InputManager.Singleton.Keyboard.Register(this, OnShortcut);

		// the view as the project file left it, once everything has laid out;
		// and what to save when the project is
		session.GuiProvider = SaveState;
		Callable.From(() => RestoreState(session.Gui)).CallDeferred();
	}

	// ---- remembered with the project ----

	System.Text.Json.Nodes.JsonObject SaveState()
	{
		System.Text.Json.Nodes.JsonObject state = new()
		{
			["playhead"] = UITimeline.PlayheadTime.Ticks,
			["timelineId"] = UITimeline.Timeline.Id.ToString(),
			["timeline"] = UITimeline.SaveState(),
			["media"] = mediaViewer.SaveState(),
			["splits"] = new System.Text.Json.Nodes.JsonObject
			{
				["main"] = GetNode<SplitContainer>("VSplitContainer").SplitOffset,
				["panels"] = GetNode<SplitContainer>("VSplitContainer/HSplitContainer").SplitOffset,
				["viewers"] = GetNode<SplitContainer>("VSplitContainer/HSplitContainer/Viewers").SplitOffset,
			},
			["tabs"] = new System.Text.Json.Nodes.JsonObject
			{
				["left"] = TabIds(LeftTabs),
				["right"] = TabIds(RightTabs),
			},
		};

		if (GetWindow() is ProjectWindow window)
		{
			state["window"] = new System.Text.Json.Nodes.JsonObject
			{
				["x"] = window.Position.X,
				["y"] = window.Position.Y,
				["width"] = window.Size.X,
				["height"] = window.Size.Y,
				["maximized"] = window.Mode == Window.ModeEnum.Maximized,
			};
		}

		return state;
	}

	System.Text.Json.Nodes.JsonArray TabIds(TabsView tabs) =>
		[.. tabs.Views.Select(v => views.FirstOrDefault(p => p.Value.View == v).Key).Where(id => id is not null).Select(id => (System.Text.Json.Nodes.JsonNode)id)];

	void RestoreState(System.Text.Json.Nodes.JsonObject state)
	{
		if (state is null || state.Count == 0) return;

		if (state["window"] is System.Text.Json.Nodes.JsonObject w && GetWindow() is ProjectWindow window)
		{
			window.Position = new(w["x"]?.GetValue<int>() ?? window.Position.X, w["y"]?.GetValue<int>() ?? window.Position.Y);
			window.Size = new(w["width"]?.GetValue<int>() ?? window.Size.X, w["height"]?.GetValue<int>() ?? window.Size.Y);
			if (w["maximized"]?.GetValue<bool>() == true) window.Mode = Window.ModeEnum.Maximized;
		}

		if (state["splits"] is System.Text.Json.Nodes.JsonObject s)
		{
			if (s["main"]?.GetValue<int>() is int main) GetNode<SplitContainer>("VSplitContainer").SplitOffset = main;
			if (s["panels"]?.GetValue<int>() is int panels) GetNode<SplitContainer>("VSplitContainer/HSplitContainer").SplitOffset = panels;
			if (s["viewers"]?.GetValue<int>() is int viewers) GetNode<SplitContainer>("VSplitContainer/HSplitContainer/Viewers").SplitOffset = viewers;
		}

		if (state["tabs"] is System.Text.Json.Nodes.JsonObject tabs)
		{
			foreach ((string _, Control view) in views.Values) { LeftTabs.RemoveView(view); RightTabs.RemoveView(view); }
			foreach (System.Text.Json.Nodes.JsonNode id in tabs["left"]?.AsArray() ?? []) if (views.TryGetValue(id?.GetValue<string>() ?? "", out var v)) LeftTabs.AddTab(v.View);
			foreach (System.Text.Json.Nodes.JsonNode id in tabs["right"]?.AsArray() ?? []) if (views.TryGetValue(id?.GetValue<string>() ?? "", out var v)) RightTabs.AddTab(v.View);
		}

		mediaViewer.RestoreState(state["media"]?.AsObject());
		UITimeline.RestoreState(state["timeline"]?.AsObject());

		// the playhead, and the picture there
		Time playhead = UITimeline.PlayheadTime;
		inspector.Playhead = playhead;
		UIPlayback.BeginScrub();
		UIPlayback.ScrubTo(playhead);
		UIPlayback.EndScrub();
	}

	// ---- the tab bars ----

	void WireTabs(TabsView tabs, TabsView other)
	{
		tabs.AvailableViews = () => views.Where(v => !LeftTabs.Holds(v.Value.View) && !RightTabs.Holds(v.Value.View)).Select(v => (v.Key, v.Value.Title));
		tabs.OpenRequested += id => { if (views.TryGetValue(id, out (string Title, Control View) v)) tabs.AddTab(v.View); };
		tabs.MoveToOtherSideRequested += view => { tabs.RemoveView(view); other.AddTab(view); };
		tabs.ResetLayoutRequested += ResetLayout;
		tabs.FloatRequested += view => GD.Print($"Floating '{view.Name}': comes with the rearrangeable views step.");
	}

	// media on the left, the inspector on the right
	void ResetLayout()
	{
		foreach ((string _, Control view) in views.Values)
		{
			LeftTabs.RemoveView(view);
			RightTabs.RemoveView(view);
		}

		LeftTabs.AddTab(mediaViewer);
		RightTabs.AddTab(inspector);
	}

	// ---- the source viewer ----

	// a media, or a timeline, plays on its own beside the timeline's
	// playback. a media gets a scratch timeline of its clips, off history
	async void ShowSource(object subject)
	{
		if (SourceViewer is null) return;

		Timeline timeline;

		switch (subject)
		{
			case Timeline t:
				timeline = t;
				break;

			case IMedia media:
				timeline = await ScratchTimelineAsync(media);
				if (timeline is null) return;
				break;

			default:
				return;
		}

		SourceViewer.Visible = true;
		SourceViewer.SetPlayback(new Playback
		{
			Timeline = timeline,
			RenderSettings = project.RenderSettings with { Resolution = new(1280, 720), Framerate = 60, SourceMode = SourceMode.ProxiesAndSource }
		});
		SourceViewer.RefreshFrame();
	}

	static async Task<Timeline> ScratchTimelineAsync(IMedia media)
	{
		Time? natural = null;
		try { natural = await media.GetNaturalLengthAsync(); } catch (Exception) { }
		Time duration = natural is Time n && n > Time.Zero ? n : Time.FromSeconds(5);

		using IDisposable _ = Transaction.Suppress();

		Timeline timeline = new();
		timeline.AddChannel(new VideoChannel());
		timeline.AddChannel(new AudioChannel());

		switch (media)
		{
			case VideoMedia video:
				(VideoClip v, AudioClip a) = Clip.CreateClipsFromMedia(video, Time.Zero, duration);
				timeline.VideoChannels[0].AddClip(v);
				if (a is not null) timeline.AudioChannels[0].AddClip(a);
				break;

			case AudioMedia audio:
				timeline.AudioChannels[0].AddClip(AudioClip.CreateFromMedia(audio, Time.Zero, duration));
				break;

			default:
				return null;
		}

		return timeline;
	}

	public override void _ExitTree()
	{
		EditSharpGUI.Scripts.UI.DragDrop.Platform.Windows.OleDragHover.Uninstall(GetWindow());
		InputManager.Singleton.Keyboard.Unregister(this);
		if (History.Active == project.History) History.Active = null;

		thumbnails?.Dispose();
		thumbnails = null;
		waveforms?.Dispose();
		waveforms = null;
		mediaThumbnails?.Dispose();
		mediaThumbnails = null;
	}

	// the files' media, imported if they are new, placed once each one's length
	// is known so a clip runs as long as its file
	async void PlaceDroppedFiles(IReadOnlyList<string> files, Vector2 at)
	{
		mediaViewer.Import(files);

		List<IMedia> media = [.. files
			.Select(path => project.Media.FirstOrDefault(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase)))
			.Where(m => m is not null)];

		await Task.WhenAll(media.Select(async m =>
		{
			try { await m.GetNaturalLengthAsync(); }
			catch (Exception) { /* unreadable: placed at the fallback length, and shown offline */ }
		}));

		if (!IsInsideTree()) return;
		UITimeline.PlaceMediaAt(media, at);
	}

	bool stopHeld, stopUsed;

	public override void _Process(double delta)
	{
		if (!stopHeld || InputManager.Singleton.Keyboard.IsHeld(Shortcuts.PlaybackStop)) return;

		stopHeld = false;
		if (!stopUsed) UIPlayback.ShuttleStop();
	}

	void OnShortcut(ShortcutEventArgs e)
	{
		switch (e.Action)
		{
			case Shortcuts.PlaybackToggle:
				UIPlayback.TogglePlayback();
				e.Handled = true;
				break;

			case Shortcuts.Save:
				if (session.FilePath is null) ProjectManager.Singleton.SaveAs(session);
				else ProjectManager.Singleton.Save(session);
				e.Handled = true;
				break;

			case Shortcuts.SaveAs:
				ProjectManager.Singleton.SaveAs(session);
				e.Handled = true;
				break;

			case Shortcuts.ShowHome:
				ProjectManager.Singleton.ShowHome();
				e.Handled = true;
				break;

			case Shortcuts.ShowSettings:
				ProjectManager.Singleton.ShowSettings();
				e.Handled = true;
				break;

			// J and L speed up; with K held they slow down instead
			case Shortcuts.PlaybackForward or Shortcuts.PlaybackReverse:
			{
				int direction = e.Action == Shortcuts.PlaybackForward ? 1 : -1;

				if (InputManager.Singleton.Keyboard.IsHeld(Shortcuts.PlaybackStop))
				{
					UIPlayback.SlowShuttle(direction);
					stopUsed = true;
				}
				else UIPlayback.Shuttle(direction);

				e.Handled = true;
				break;
			}

			// K pauses when it comes up, and only if no J or L went with it
			case Shortcuts.PlaybackStop:
				stopHeld = true;
				stopUsed = false;
				e.Handled = true;
				break;

			case Shortcuts.StepForward or Shortcuts.StepBack:
				UIPlayback.StepFrame(e.Action == Shortcuts.StepForward ? 1 : -1);
				e.Handled = true;
				break;

			// not mid-gesture: a drag in flight is positioned from data that
			// would move under it
			case Shortcuts.Undo when !MouseDragging:
				project.History.Undo();
				e.Handled = true;
				break;

			case Shortcuts.Redo when !MouseDragging:
				project.History.Redo();
				e.Handled = true;
				break;
		}
	}

	static bool MouseDragging => InputManager.Singleton.Mouse.LeftButton.ClickState == MouseButtonClickState.Dragging;
}
