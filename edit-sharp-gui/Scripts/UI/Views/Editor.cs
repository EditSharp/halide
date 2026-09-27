using EditSharp.Components;
using EditSharp.Components.Channels;
using EditSharp.Components.Clips;
using EditSharp.Components.Media;
using EditSharp.History;
using EditSharp.Playback;
using EditSharpGUI.Scripts.Input;
using EditSharpGUI.Scripts.App.Layouts;
using EditSharpGUI.Scripts.UI.Docking;
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
	[Export] DockManager Dock;

	[ExportGroup("Packed Scenes")]

	[Export] PackedScene MediaViewerScene;
	[Export] PackedScene InspectorScene;
	[Export] PackedScene GraphEditorScene;

	MediaViewer mediaViewer;
	Inspector inspector;
	Control graphEditor;

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

		Dock.Register("media", "Media", mediaViewer);
		Dock.Register("inspector", "Inspector", inspector);
		if (graphEditor is not null) Dock.Register("graph", "Graph Editor", graphEditor);
		Dock.Register("source", "Source", SourceViewer);
		Dock.Register("program", "Program", UIPlayback);
		Dock.Register("timeline", "Timeline", UITimeline);
		Dock.DefaultLayout = DefaultLayout;
		Layouts = new LayoutController(Dock);
		Dock.ResetOverride = Layouts.ResetActive;
		Dock.DefaultSpot = id => id == "source" ? ("program", DockSide.Left)
			: EditSharpGUI.Api.EditSharpApp.Instance.Views.FromExtensions.FirstOrDefault(v => v.Id == id) is { Beside: string beside } view ? (beside, view.Side)
			: null;

		// views extensions add, now and while this window is open
		foreach (EditSharpGUI.Api.ViewDefinition view in EditSharpGUI.Api.EditSharpApp.Instance.Views.FromExtensions) AddExtensionView(view);
		EditSharpGUI.Api.EditSharpApp.Instance.Views.Added += AddExtensionView;
		EditSharpGUI.Api.EditSharpApp.Instance.Views.Removed += RemoveExtensionView;
		Dock.FloatOpened += WireFloat;

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
		inspector.SeekRequested += (_, time) => Seek(time);

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
			["dock"] = Dock.Save(),
			["layout"] = Layouts.Active,
		};

		// a window never seen windowed has no size worth keeping; it reopens at the default
		if (GetWindow() is ProjectWindow window && window.RestoredRect is { } restored && restored.HasArea())
		{
			state["window"] = new System.Text.Json.Nodes.JsonObject
			{
				["x"] = restored.Position.X,
				["y"] = restored.Position.Y,
				["width"] = restored.Size.X,
				["height"] = restored.Size.Y,
				// a fullscreen window reopens windowed, at its size from before
				["maximized"] = window.Mode == Window.ModeEnum.Maximized,
			};
		}

		return state;
	}

	void RestoreState(System.Text.Json.Nodes.JsonObject state)
	{
		// the arrangement the project was left in, under the layout it was in; a new project opens in Editing as it's remembered
		if (state?["dock"] is System.Text.Json.Nodes.JsonObject dock)
		{
			Dock.Restore(dock);
			Layouts.Resume((string)state["layout"]);
		}
		else Layouts.Apply(LayoutPresets.Editing);

		if (state is null || state.Count == 0) return;

		if (state["window"] is System.Text.Json.Nodes.JsonObject w && GetWindow() is ProjectWindow window)
		{
			Rect2I saved = new(
				w["x"]?.GetValue<int>() ?? window.Position.X, w["y"]?.GetValue<int>() ?? window.Position.Y,
				w["width"]?.GetValue<int>() ?? window.Size.X, w["height"]?.GetValue<int>() ?? window.Size.Y);
			window.Restore(saved, w["maximized"]?.GetValue<bool>() == true);
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

	// ---- the layout ----

	// the window's layouts: the active one, applying others, remembering rearrangements
	public LayoutController Layouts { get; private set; }

	// the Editing preset as it ships
	static DockTree DefaultLayout() => new(DockNode.FromJson(LayoutPresets.State(LayoutPresets.Editing)["main"]));

	// a float takes the page's shortcuts and dropped files, and makes this project the one in use
	void WireFloat(DockFloatWindow window)
	{
		InputManager.Singleton.Keyboard.Register(window, OnShortcut);
		window.TreeExiting += () => InputManager.Singleton.Keyboard.Unregister(window);
		window.FocusEntered += session.Activate;
		window.FilesDropped += files => DragDrop.DropFiles(files, window.Area.GetViewport().GetMousePosition(), window.Area);
		EditSharpGUI.Scripts.UI.DragDrop.Platform.Windows.OleDragHover.Install(window);
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

		Dock.Open("source");
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
		Layouts?.Dispose();
		EditSharpGUI.Api.EditSharpApp.Instance.Views.Added -= AddExtensionView;
		EditSharpGUI.Api.EditSharpApp.Instance.Views.Removed -= RemoveExtensionView;
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
		// nothing closer took it: the timeline counts as focused until another view is clicked, so it's the timeline's to try
		if (FocusedView == "timeline" && Dock.IsOpen("timeline") && UITimeline.TakeShortcut(e)) return;

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

			case Shortcuts.GoToStart:
				Seek(Time.Zero);
				e.Handled = true;
				break;

			case Shortcuts.GoToEnd:
				Seek(UITimeline.Timeline.Duration);
				e.Handled = true;
				break;

			case Shortcuts.Loop:
				UIPlayback.Loop = !UIPlayback.Loop;
				e.Handled = true;
				break;

			// the media viewer imports when it has the keyboard; from anywhere else, here
			case Shortcuts.MediaImport:
				mediaViewer.ImportDialog();
				e.Handled = true;
				break;

			case Shortcuts.FloatFocused:
				if (FocusedView is string floated && Dock.IsOpen(floated)) Dock.Float(floated);
				e.Handled = true;
				break;

			case Shortcuts.ResetLayout:
				Layouts.ResetActive();
				e.Handled = true;
				break;

			case string layout when layout.StartsWith("layout.") && int.TryParse(layout["layout.".Length..], out int number):
				Layouts.ApplyNumber(number);
				e.Handled = true;
				break;

			case Shortcuts.Fullscreen:
				ToggleFullscreen();
				e.Handled = true;
				break;

			case Shortcuts.CloseProject:
				if (GetWindow() is ProjectWindow window) _ = window.CloseAsync();
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

	// the playhead, the picture and the inspector all at `time`
	void Seek(Time time)
	{
		UIPlayback.BeginScrub();
		UIPlayback.ScrubTo(time);
		UIPlayback.EndScrub();
		UITimeline.PlayheadTime = time;
		inspector.Playhead = time;
	}

	// the focused one of this project's windows in or out of fullscreen
	void ToggleFullscreen()
	{
		Window window = GetWindow().GetChildren().OfType<DockFloatWindow>().FirstOrDefault(f => f.HasFocus()) ?? GetWindow();
		window.Mode = window.Mode == Window.ModeEnum.Fullscreen ? Window.ModeEnum.Windowed : Window.ModeEnum.Fullscreen;
	}

	// an extension's view made for this window; one that fails to build is left out
	void AddExtensionView(EditSharpGUI.Api.ViewDefinition view)
	{
		if (GetWindow() is not ProjectWindow window) return;

		try
		{
			Control made = view.Create(EditSharpGUI.Api.EditSharpApp.Instance.Projects.Handle(window));
			if (made is null) return;
			made.Name = view.Title;
			Dock.Register(view.Id, view.Title, made);
		}
		catch (Exception e)
		{
			GD.PushError($"The view '{view.Id}' failed to build: {e.Message}");
		}
	}

	void RemoveExtensionView(EditSharpGUI.Api.ViewDefinition view) => Dock.Unregister(view.Id);

	// ---- for the API's services ----

	internal UITimeline TimelineView => UITimeline;
	internal UIPlayback ProgramView => UIPlayback;
	internal UIPlayback SourceView => SourceViewer;
	internal MediaViewer MediaView => mediaViewer;
	internal Inspector InspectorView => inspector;
	internal DockManager Docks => Dock;
	internal void SeekTo(Time time) => Seek(time);
	internal void OpenSource(object subject) => ShowSource(subject);

	// ---- for the menus: what applies right now ----

	// the view the keyboard is with, by id; the timeline when no view has claimed it
	public string FocusedView => Dock.ViewHolding(InputManager.Singleton.Keyboard.Captor) ?? "timeline";

	public bool Looping => UIPlayback.Loop;

	// whether an action would do anything from here, so a menu can grey it out
	public bool CanRun(string action)
	{
		string view = FocusedView;
		bool timeline = view == "timeline" && Dock.IsOpen("timeline");
		bool media = view == "media" && Dock.IsOpen("media");

		return action switch
		{
			Shortcuts.Undo => project.History.CanUndo,
			Shortcuts.Redo => project.History.CanRedo,
			Shortcuts.Cut or Shortcuts.Copy or Shortcuts.Duplicate => timeline && UITimeline.HasSelection,
			Shortcuts.Delete => timeline && UITimeline.HasSelection || media && mediaViewer.Selected.Count > 0,
			Shortcuts.Paste => timeline && EditSharpGUI.Scripts.Clipboard.Shared.TryGet(out EditSharpGUI.Scripts.ClipsItem _),
			Shortcuts.SelectAll or Shortcuts.Deselect => timeline || media,
			Shortcuts.ZoomIn or Shortcuts.ZoomOut or Shortcuts.ZoomFit => Dock.IsOpen("timeline"),
			Shortcuts.FloatFocused => Dock.IsOpen(view) && Dock.CanFloat(view),
			_ => true,
		};
	}

	// the name of what undo or redo would take back, for the menu
	public string UndoName => project.History.UndoDescription;
	public string RedoName => project.History.RedoDescription;

	static bool MouseDragging => InputManager.Singleton.Mouse.LeftButton.ClickState == MouseButtonClickState.Dragging;
}
