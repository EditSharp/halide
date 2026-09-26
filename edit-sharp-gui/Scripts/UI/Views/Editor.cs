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
	public override void _Ready()
	{
		Project project = ProjectManager.Singleton.CurrentProject;

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
		UITimeline.FilesDropped += (files, at) => UITimeline.PlaceMediaAt(mediaViewer.Import(files), at);
		GetWindow().FilesDropped += files => DragDrop.DropFiles(files, GetViewport().GetMousePosition());
		EditSharpGUI.Scripts.UI.DragDrop.Platform.Windows.OleDragHover.Install();

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

		Project project = ProjectManager.Singleton.CurrentProject;
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
		TimeSpan? natural = null;
		try { natural = await media.GetNaturalLengthAsync(); } catch (Exception) { }
		TimeSpan duration = natural is TimeSpan n && n > TimeSpan.Zero ? n : TimeSpan.FromSeconds(5);

		using IDisposable _ = Transaction.Suppress();

		Timeline timeline = new();
		timeline.AddChannel(new VideoChannel());
		timeline.AddChannel(new AudioChannel());

		switch (media)
		{
			case VideoMedia video:
				(VideoClip v, AudioClip a) = Clip.CreateClipsFromMedia(video, TimeSpan.Zero, duration);
				timeline.VideoChannels[0].AddClip(v);
				if (a is not null) timeline.AudioChannels[0].AddClip(a);
				break;

			case AudioMedia audio:
				timeline.AudioChannels[0].AddClip(AudioClip.CreateFromMedia(audio, TimeSpan.Zero, duration));
				break;

			default:
				return null;
		}

		return timeline;
	}

	public override void _ExitTree()
	{
		EditSharpGUI.Scripts.UI.DragDrop.Platform.Windows.OleDragHover.Uninstall();
		InputManager.Singleton.Keyboard.Unregister(this);
		if (History.Active == ProjectManager.Singleton.CurrentProject.History) History.Active = null;

		thumbnails?.Dispose();
		thumbnails = null;
		waveforms?.Dispose();
		waveforms = null;
		mediaThumbnails?.Dispose();
		mediaThumbnails = null;
	}

	void OnShortcut(ShortcutEventArgs e)
	{
		switch (e.Action)
		{
			case Shortcuts.PlaybackToggle:
				UIPlayback.TogglePlayback();
				e.Handled = true;
				break;

			// not mid-gesture: a drag in flight is positioned from data that
			// would move under it
			case Shortcuts.Undo when !MouseDragging:
				ProjectManager.Singleton.CurrentProject.History.Undo();
				e.Handled = true;
				break;

			case Shortcuts.Redo when !MouseDragging:
				ProjectManager.Singleton.CurrentProject.History.Redo();
				e.Handled = true;
				break;
		}
	}

	static bool MouseDragging => InputManager.Singleton.Mouse.LeftButton.ClickState == MouseButtonClickState.Dragging;
}
