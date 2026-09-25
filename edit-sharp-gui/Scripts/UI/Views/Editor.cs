using EditSharp.Components.Media;
using EditSharp.History;
using EditSharpGUI.Scripts.Input;
using EditSharpGUI.Scripts.UI.Thumbnails;
using Godot;
using System;

public partial class Editor : Control
{
	[ExportGroup("Views")]

	[Export] UITimeline UITimeline;
	[Export] UIPlayback UIPlayback;
	[Export] Inspector Inspector;
	[Export] TabsView TabsView;

	[ExportGroup("Packed Scenes")]

	[Export] PackedScene SourceViewer;

	// the clips' frames. it owns a playback of its own, apart from the one
	// the user watches, so the two never wait on each other
	ThumbnailCache thumbnails;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		Project project = ProjectManager.Singleton.CurrentProject;

		TabsView.AddTab(SourceViewer.Instantiate() as Control);

		thumbnails = new ThumbnailCache(project.Timeline, project.RenderSettings, project.History);
		UITimeline.Thumbnails = thumbnails;

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
		UITimeline.PlayheadDrag += (_, time) => { UIPlayback.ScrubTo(time); Inspector.Playhead = time; };
		UITimeline.PlayheadDragEnded += (_, _) => UIPlayback.EndScrub();

		UIPlayback.PositionChanged += (_, time) => { UITimeline.PlayheadTime = time; Inspector.Playhead = time; };

		// the inspector shows whatever the timeline has selected, records
		// into the project's history, and can ask for the playhead to be
		// moved to a keyframe
		Inspector.History = project.History;
	Inspector.Media = project.Media;
		Inspector.Framerate = project.RenderSettings.Framerate;
		Inspector.FrameSize = new((int)project.RenderSettings.Resolution.X, (int)project.RenderSettings.Resolution.Y);
		Inspector.Playhead = UITimeline.PlayheadTime;
		UITimeline.SelectionChanged += (_, _) => Inspector.ShowClips(UITimeline.SelectedClips);
		Inspector.SeekRequested += (_, time) =>
		{
			UIPlayback.BeginScrub();
			UIPlayback.ScrubTo(time);
			UIPlayback.EndScrub();
			UITimeline.PlayheadTime = time;
			Inspector.Playhead = time;
		};

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
		Inspector.Edited += (_, _) => UITimeline.Reconcile();

		// the preview is a still picture while stopped, so anything that
		// changes the timeline shows it again: every history entry (a drag,
		// a trim, an undo), and live inspector edits as they happen. and
		// once to begin with, so there's a picture before anything is played
		history.Changed += (_, _) => UIPlayback.RefreshFrame();
		Inspector.Edited += (_, _) => UIPlayback.RefreshFrame();
		UIPlayback.RefreshFrame();

		// page-wide shortcuts. every key that nothing closer wanted climbs up
		// to here, since this page is above every view in it
		InputManager.Singleton.Keyboard.Register(this, OnShortcut);
	}

	public override void _ExitTree()
	{
		InputManager.Singleton.Keyboard.Unregister(this);
		if (History.Active == ProjectManager.Singleton.CurrentProject.History) History.Active = null;

		thumbnails?.Dispose();
		thumbnails = null;
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
