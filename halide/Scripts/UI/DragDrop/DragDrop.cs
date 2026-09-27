using System;
using System.Collections.Generic;
using System.Linq;
using EditSharp.Components;
using EditSharp.Components.Media;
using Halide.Scripts.Input;
using Godot;

namespace Halide.Scripts.UI.DragDrop;

// what a drag carries. media from the viewer, timelines from its Timelines
// tab, or files from outside the app
public abstract class DragPayload { }

public sealed class MediaPayload(IReadOnlyList<IMedia> media) : DragPayload
{
	public IReadOnlyList<IMedia> Media { get; } = media;
}

public sealed class TimelinePayload(IReadOnlyList<Timeline> timelines) : DragPayload
{
	public IReadOnlyList<Timeline> Timelines { get; } = timelines;
}

public sealed class FilesPayload(IReadOnlyList<string> paths) : DragPayload
{
	public IReadOnlyList<string> Paths { get; } = paths;
}

// a control something can be dropped on. positions are global
public interface IDropTarget
{
	bool CanDrop(DragPayload payload, Vector2 at);

	// the payload is over the target; called every frame while it is
	void DragOver(DragPayload payload, Vector2 at) { }

	// the payload left, or the drag was cancelled
	void DragLeave() { }

	void Drop(DragPayload payload, Vector2 at);
}

// the app's own drag and drop: the source keeps the mouse capture and
// drives this from its gesture, a ghost follows the cursor into whichever of
// the source's family of windows (a project window and its floats) is under
// it, and the target there is the deepest drop target whose rect holds the
// point. files from outside arrive through the same targets, from the
// window's drop event or, on windows, the native hover
public static class DragDrop
{
	public static DragPayload Payload { get; private set; }
	public static bool Active => Payload is not null;

	// the ghost, when the drag has one: a control parented to the root that
	// follows the cursor. targets showing their own preview may hide it
	public static Control Ghost { get; private set; }

	// whether the drag is a native one from outside the app
	public static bool External { get; private set; }

	static IDropTarget over;
	static Vector2 ghostGrab;

	// the window the drag began in; positions handed in are its own
	static Window scope;

	// the windows whose targets may take it, frontmost first
	static List<Window> windows = [];

	// the targets in those windows when the drag began, deepest last
	static List<(Control Control, IDropTarget Target, int Depth, Window Window)> targets = [];

	// begins a drag. the ghost is placed at the cursor, offset so the
	// cursor sits at `grab` inside it, in the window of `from`, the node the drag starts in
	public static void Begin(DragPayload payload, Control ghost, Vector2 grab, Node from = null, bool external = false)
	{
		Cancel();

		Payload = payload;
		Ghost = ghost;
		ghostGrab = grab;
		External = external;
		scope = from?.GetWindow() ?? Root;
		windows = external || scope == Root ? [scope] : Family(scope);
		targets = CollectTargets();

		// followed every frame too, since the source may stop hearing the pointer over another window
		if (!external && scope != Root) ((SceneTree)Engine.GetMainLoop()).ProcessFrame += Follow;

		if (ghost is not null)
		{
			ghost.MouseFilter = Control.MouseFilterEnum.Ignore;
			ghost.TopLevel = true;
			ghost.ZIndex = 4000;
			scope.AddChild(ghost);
		}

		Update(InputManager.Singleton.Mouse.CurrentPosition);
	}

	// the cursor moved (or the view under it did): find the target and tell it
	public static void Update(Vector2 at)
	{
		if (!Active) return;

		(Window window, Vector2 local) = Locate(at);

		if (Ghost is not null)
		{
			if (window is not null && Ghost.GetParent() != window) Ghost.Reparent(window, false);
			Ghost.GlobalPosition = local - ghostGrab;
		}

		IDropTarget target = window is null ? null : Find(Payload, local, window);

		if (target != over)
		{
			over?.DragLeave();
			over = target;
		}

		// between windows there's nowhere to show it
		if (Ghost is not null) Ghost.Visible = window is not null;
		over?.DragOver(Payload, local);
	}

	// whether something under the point would take the payload right now
	public static bool CanDropAt(Vector2 at)
	{
		if (!Active) return false;
		(Window window, Vector2 local) = Locate(at);
		return window is not null && Find(Payload, local, window) is not null;
	}

	// the drag ended on whatever is under the cursor
	public static void Finish(Vector2 at)
	{
		if (!Active) return;

		DragPayload payload = Payload;
		(Window window, Vector2 local) = Locate(at);
		IDropTarget target = window is null ? null : Find(payload, local, window);

		Clear();

		target?.Drop(payload, local);
	}

	public static void Cancel()
	{
		if (!Active) return;
		Clear();
	}

	static void Clear()
	{
		over?.DragLeave();
		over = null;
		Payload = null;
		External = false;
		targets = [];
		windows = [];
		((SceneTree)Engine.GetMainLoop()).ProcessFrame -= Follow;

		if (Ghost is not null)
		{
			Ghost.QueueFree();
			Ghost = null;
		}
	}

	// files from outside the app, let go at a point in `from`'s window: the target under it takes them
	public static void DropFiles(string[] files, Vector2 at, Node from = null)
	{
		if (files is null || files.Length == 0) return;

		if (Active && External) { Finish(at); return; }

		FilesPayload payload = new([.. files]);
		scope = from?.GetWindow() ?? Root;
		windows = [scope];
		targets = CollectTargets();
		IDropTarget target = Find(payload, at, scope);
		targets = [];
		windows = [];
		target?.Drop(payload, at);
	}

	static Window Root => ((SceneTree)Engine.GetMainLoop()).Root;

	// the pointer followed on screen, and let go of wherever the OS says the button came up
	static void Follow()
	{
		if (!Held()) Finish(Vector2.Zero);
		else Update(Vector2.Zero);
	}

	// the pointer on screen and its button; a probe can steer its own instead
	public static Func<Vector2I> Pointer = () => OsPointer.Position;
	public static Func<bool> Held = () => OsPointer.LeftHeld;

	// the window under a point given in the scope's own coordinates, and the point in that window's.
	// a drag from inside the app goes by the pointer on screen, which the scope's events may not follow
	static (Window Window, Vector2 Local) Locate(Vector2 at)
	{
		if (windows.Count == 1 && windows[0] == scope && (External || scope == Root)) return (scope, at);

		Vector2I screen = External ? scope.Position + (Vector2I)(at * scope.ContentScaleFactor).Round() : Pointer();
		foreach (Window window in windows)
		{
			if (!GodotObject.IsInstanceValid(window) || !window.Visible || window.Mode == Window.ModeEnum.Minimized) continue;
			if (!new Rect2I(window.Position, window.Size).HasPoint(screen)) continue;
			return (window, (Vector2)(screen - window.Position) / window.ContentScaleFactor);
		}

		return (null, at);
	}

	// a window with the native windows it owns (a project window's floats), those on top first
	static List<Window> Family(Window window)
	{
		Window top = window;
		while (top.GetParent()?.GetWindow() is Window parent && parent != Root) top = parent;

		List<Window> family = [.. top.FindChildren("*", "Window", true, false).OfType<Window>().Where(w => !w.IsEmbedded())];
		family.Reverse();
		family.Add(top);
		return family;
	}

	// the deepest visible drop target in `window` whose rect holds the point and that
	// will take the payload. the ghost, if any, is never a target
	static IDropTarget Find(DragPayload payload, Vector2 at, Window window)
	{
		for (int i = targets.Count - 1; i >= 0; i--)
		{
			(Control control, IDropTarget target, int _, Window owner) = targets[i];

			if (owner != window) continue;
			if (!GodotObject.IsInstanceValid(control) || !control.IsVisibleInTree()) continue;
			if (!control.GetGlobalRect().HasPoint(at)) continue;
			if (target.CanDrop(payload, at)) return target;
		}

		return null;
	}

	static List<(Control, IDropTarget, int, Window)> CollectTargets()
	{
		List<(Control, IDropTarget, int, Window)> found = [];
		foreach (Window window in windows) Walk(window, window, 0, found);
		found.Sort((a, b) => a.Item3.CompareTo(b.Item3));
		return found;
	}

	// a window's own targets; a native window inside it is walked on its own
	static void Walk(Node node, Window window, int depth, List<(Control, IDropTarget, int, Window)> into)
	{
		if (node is IDropTarget target && node is Control control && control != Ghost) into.Add((control, target, depth, window));
		foreach (Node child in node.GetChildren())
		{
			if (child is Window inner && !inner.IsEmbedded()) continue;
			Walk(child, window, depth + 1, into);
		}
	}
}
