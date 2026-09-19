using EditSharp.History;
using EditSharpGUI.Scripts.Input;
using Godot;
using System;

public partial class Editor : Control
{
	[ExportGroup("Views")]

	[Export] UITimeline UITimeline;
	[Export] UIPlayback UIPlayback;
	[Export] Inspector Inspector;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		UITimeline.SetTimeline(ProjectManager.Singleton.CurrentProject.Timeline);
		UIPlayback.SetPlayback(new()
		{
			Timeline = ProjectManager.Singleton.CurrentProject.Timeline,
			RenderSettings = ProjectManager.Singleton.CurrentProject.RenderSettings with { Resolution = new(1280, 720), Framerate = 60 }
		});

		// the timeline and playback never see each other - this is the only
		// place the two are joined. the playhead drives playback: grabbing it
		// scrubs, letting go picks playback back up if it was running. playback
		// drives the playhead back - but only the line, never the view. the
		// view stays wherever the user put it
		UITimeline.PlayheadDragStarted += (_, _) => UIPlayback.BeginScrub();
		UITimeline.PlayheadDrag += (_, time) => UIPlayback.ScrubTo(time);
		UITimeline.PlayheadDragEnded += (_, _) => UIPlayback.EndScrub();

		UIPlayback.PositionChanged += (_, time) => UITimeline.PlayheadTime = time;

		// the project's history is the one stray writes fall into, and an undo
		// or redo moves the data with no view watching - so the timeline
		// re-reads it. a commit needs nothing: the view that made the change
		// already updated itself
		History history = ProjectManager.Singleton.CurrentProject.History;
		History.Active = history;
		history.Changed += (_, e) => { if (e.Action != HistoryAction.Commit) UITimeline.Reconcile(); };

		// page-wide shortcuts. every key that nothing closer wanted climbs up
		// to here, since this page is above every view in it
		InputManager.Singleton.Keyboard.Register(this, OnShortcut);
	}

	public override void _ExitTree()
	{
		InputManager.Singleton.Keyboard.Unregister(this);
		if (History.Active == ProjectManager.Singleton.CurrentProject.History) History.Active = null;
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
