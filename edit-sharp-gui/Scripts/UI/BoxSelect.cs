using EditSharpGUI.Scripts.Input;
using Godot;
using System.Collections.Generic;
using System.Linq;

namespace EditSharpGUI.Scripts.UI;

// what a rubber-band does to the selection it lands on
public enum BoxSelectMode
{
	// the boxed items become the selection
	Replace,
	// the boxed items join it
	Add,
	// the boxed items leave it
	Remove
}

// a drag-to-select rectangle over a Selection<T>. knows nothing about what
// the items are or how they are drawn: the host feeds it the anchor and the
// moving corner in whatever space its items are measured in, asks it for
// the rect to hit-test with, and hands back the hits. the selection is
// rewritten from what it was when the box began, so a box that shrinks
// lets go of what it no longer covers, and a cancelled box leaves the
// selection exactly as it found it
public sealed class BoxSelect<T>(Selection<T> selection)
{
	// the selection as it stood when the box began - the base every Apply
	// builds on
	readonly List<T> before = [];

	Vector2 anchor;
	Vector2 corner;

	public bool Active { get; private set; }
	public BoxSelectMode Mode { get; private set; }

	// the box as a rect, whichever way it was dragged
	public Rect2 Rect => new Rect2(anchor, Vector2.Zero).Expand(corner);

	// the usual meaning of the modifiers: shift adds, control removes, and
	// a plain drag starts over
	public static BoxSelectMode ModeFor(Modifiers modifiers)
		=> modifiers.Shift ? BoxSelectMode.Add
		: modifiers.Control ? BoxSelectMode.Remove
		: BoxSelectMode.Replace;

	public void Begin(Vector2 at, BoxSelectMode mode)
	{
		Active = true;
		Mode = mode;
		anchor = at;
		corner = at;

		before.Clear();
		before.AddRange(selection);
	}

	public void Update(Vector2 to) => corner = to;

	// an item that stopped existing under the box - it must not come back
	// when the base is re-applied
	public void Forget(T item) => before.Remove(item);

	// rewrite the selection from the base plus the hits, by mode. true when
	// the set actually changed, so the host only re-renders when it needs to
	public bool Apply(IEnumerable<T> hits)
	{
		List<T> next = Mode switch
		{
			BoxSelectMode.Add => [.. before.Concat(hits).Distinct()],
			BoxSelectMode.Remove => [.. before.Except(hits)],
			_ => [.. hits.Distinct()]
		};

		if (next.Count == selection.Count && next.All(selection.Contains)) return false;

		selection.Clear();
		selection.AddRange(next);
		return true;
	}

	// the box is done and whatever it applied last stands
	public void End()
	{
		Active = false;
		before.Clear();
	}

	// the box never got its release, so put the selection back. true when
	// that changed anything
	public bool Cancel()
	{
		Active = false;

		bool changed = before.Count != selection.Count || !before.All(selection.Contains);

		selection.Clear();
		selection.AddRange(before);
		before.Clear();

		return changed;
	}
}
