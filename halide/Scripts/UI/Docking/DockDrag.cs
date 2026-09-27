using Godot;
using System;
using System.Collections.Generic;

namespace Halide.Scripts.UI.Docking;

// a tab being dragged: follows the pointer across the project's windows in screen space,
// shows where the view would land, and reports the drop when the button comes up
public partial class DockDrag : Node
{
	// the view being moved, and its title for the tab that follows the pointer
	public string View { get; init; }
	public string Title { get; init; }

	// the areas it could land in, frontmost first
	public Func<IReadOnlyList<DockArea>> Areas { get; init; }

	// the size a float would open at, in screen pixels
	public Vector2I FloatSize { get; init; }

	// whether letting go outside every window floats it; a float's only view has nowhere new to go
	public bool Floatable { get; init; } = true;

	// let go over a pane; or outside every window, with the float's rect; or cancelled
	public event Action<DockDrop> Dropped;
	public event Action<Rect2I> Floated;
	public event Action Cancelled;

	// the pointer on screen and whether its button is down; a probe can steer its own instead
	public static Func<Vector2I> Pointer = () => Halide.Scripts.Input.OsPointer.Position;
	public static Func<bool> Held = () => Halide.Scripts.Input.OsPointer.LeftHeld;

	// where the pointer sits in a new float's bar
	[Export] Vector2I floatGrab = new(60, 15);

	DockArea over;
	DockDrop drop;
	DockOutlineWindow outline;
	DockTabGhost ghost;
	bool outside;

	public override void _Ready()
	{
		outline = DockOutlineWindow.Create(this);
		ghost = DockTabGhost.Create(Title);
		Godot.Input.SetDefaultCursorShape(Godot.Input.CursorShape.Move);
		Track();
	}

	public override void _Process(double delta)
	{
		if (Godot.Input.IsKeyPressed(Key.Escape))
		{
			End();
			Cancelled?.Invoke();
			return;
		}

		if (Held())
		{
			Track();
			return;
		}

		DockDrop landed = drop;
		bool floated = outside;
		Rect2I rect = FloatRect;
		End();

		if (landed is not null) Dropped?.Invoke(landed);
		else if (floated) Floated?.Invoke(rect);
		else Cancelled?.Invoke();
	}

	Rect2I FloatRect => new(Pointer() - floatGrab, FloatSize);

	void Track()
	{
		Vector2I screen = Pointer();
		DockArea hit = null;

		foreach (DockArea area in Areas())
		{
			Window window = area.GetWindow();
			if (!window.Visible || window.Mode == Window.ModeEnum.Minimized) continue;
			if (!new Rect2I(window.Position, window.Size).HasPoint(screen)) continue;
			hit = area;
			break;
		}

		if (over != hit) over?.Untrack();
		over = hit;
		outside = hit is null;

		if (hit is null)
		{
			ghost.Visible = false;
			drop = null;
			outside = Floatable;
			if (Floatable) outline.ShowAt(FloatRect);
			else outline.Hide();
			return;
		}

		outline.Hide();
		Window at = hit.GetWindow();
		Vector2 local = (Vector2)(screen - at.Position) / at.ContentScaleFactor;
		drop = hit.Track(local);
		ghost.Follow(at, local);
	}

	void End()
	{
		SetProcess(false);
		over?.Untrack();
		ghost.QueueFree();
		Godot.Input.SetDefaultCursorShape();
		QueueFree();
	}
}
