using System;
using System.Collections.Generic;
using System.Linq;
using EditSharp.Components;
using EditSharp.Components.Media;
using EditSharpGUI.Scripts.Input;
using Godot;

namespace EditSharpGUI.Scripts.UI.DragDrop;

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
// drives this from its gesture, a ghost follows the cursor, and the target
// under the cursor is the deepest drop target whose rect holds the point.
// files from outside arrive through the same targets, from the window's
// drop event or, on windows, the native hover
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

	// the window the drag is in; only its targets take it
	static Window scope;

	// the targets in the tree when the drag began, deepest last
	static List<(Control Control, IDropTarget Target, int Depth)> targets = [];

	// begins a drag. the ghost is placed at the cursor, offset so the
	// cursor sits at `grab` inside it, in the window of `from`, the node the drag starts in
	public static void Begin(DragPayload payload, Control ghost, Vector2 grab, Node from = null, bool external = false)
	{
		Cancel();

		Payload = payload;
		Ghost = ghost;
		ghostGrab = grab;
		External = external;
		scope = from?.GetWindow();
		targets = CollectTargets();

		if (ghost is not null)
		{
			ghost.MouseFilter = Control.MouseFilterEnum.Ignore;
			ghost.TopLevel = true;
			ghost.ZIndex = 4000;
			(scope ?? Root).AddChild(ghost);
		}

		Update(InputManager.Singleton.Mouse.CurrentPosition);
	}

	// the cursor moved (or the view under it did): find the target and tell it
	public static void Update(Vector2 at)
	{
		if (!Active) return;

		if (Ghost is not null) Ghost.GlobalPosition = at - ghostGrab;

		IDropTarget target = Find(Payload, at);

		if (target != over)
		{
			over?.DragLeave();
			over = target;
		}

		if (Ghost is not null) Ghost.Visible = true;
		over?.DragOver(Payload, at);
	}

	// whether something under the point would take the payload right now
	public static bool CanDropAt(Vector2 at) => Active && Find(Payload, at) is not null;

	// the drag ended on whatever is under the cursor
	public static void Finish(Vector2 at)
	{
		if (!Active) return;

		DragPayload payload = Payload;
		IDropTarget target = Find(payload, at);

		Clear();

		target?.Drop(payload, at);
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
		scope = from?.GetWindow();
		targets = CollectTargets();
		IDropTarget target = Find(payload, at);
		targets = [];
		target?.Drop(payload, at);
	}

	static Window Root => ((SceneTree)Engine.GetMainLoop()).Root;

	// the deepest visible drop target whose rect holds the point and that
	// will take the payload. the ghost, if any, is never a target
	static IDropTarget Find(DragPayload payload, Vector2 at)
	{
		for (int i = targets.Count - 1; i >= 0; i--)
		{
			(Control control, IDropTarget target, int _) = targets[i];

			if (!GodotObject.IsInstanceValid(control) || !control.IsVisibleInTree()) continue;
			if (!control.GetGlobalRect().HasPoint(at)) continue;
			if (target.CanDrop(payload, at)) return target;
		}

		return null;
	}

	static List<(Control, IDropTarget, int)> CollectTargets()
	{
		List<(Control, IDropTarget, int)> found = [];
		Walk(scope ?? Root, 0, found);
		found.Sort((a, b) => a.Item3.CompareTo(b.Item3));
		return found;
	}

	static void Walk(Node node, int depth, List<(Control, IDropTarget, int)> into)
	{
		if (node is IDropTarget target && node is Control control && control != Ghost) into.Add((control, target, depth));
		foreach (Node child in node.GetChildren()) Walk(child, depth + 1, into);
	}
}
