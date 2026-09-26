using Godot;
using System;

namespace EditSharpGUI.Scripts.UI.Inspecting;

public enum KeyState
{
	// the value cannot be animated: the column is empty, but keeps its width
	None,
	// no keyframes: edits write the static value
	Static,
	// keyframes, none at the playhead: edits write a new one there
	Animated,
	// a keyframe at the playhead: edits update it
	Keyed
}

// the column at the right of every row, the same width whether or not the
// row can be animated: arrows to the neighbouring keyframes, a diamond that
// adds or removes one at the playhead, and a reset that drops the track.
// laid out in KeyColumn.tscn
[Tool]
public partial class KeyColumn : HBoxContainer
{
	public event Action PrevPressed;
	public event Action KeyPressed;
	public event Action NextPressed;
	public event Action ResetTrackPressed;

	[Export] InspectorGlyph prev;
	[Export] InspectorGlyph key;
	[Export] InspectorGlyph next;
	[Export] InspectorGlyph resetTrack;

	public override void _Ready()
	{
		prev.Pressed += () => PrevPressed?.Invoke();
		key.Pressed += () => KeyPressed?.Invoke();
		next.Pressed += () => NextPressed?.Invoke();
		resetTrack.Pressed += () => ResetTrackPressed?.Invoke();

		Set(KeyState.None, false, false);
	}

	public void Set(KeyState state, bool hasPrev, bool hasNext)
	{
		bool shown = state != KeyState.None;
		bool keyed = state is KeyState.Animated or KeyState.Keyed;

		prev.Visible = shown;
		key.Visible = shown;
		next.Visible = shown;
		resetTrack.Visible = shown;

		key.State = state;
		ShowGlyph(prev, hasPrev);
		ShowGlyph(next, hasNext);
		ShowGlyph(resetTrack, keyed);

		prev.QueueRedraw();
		key.QueueRedraw();
		next.QueueRedraw();
		resetTrack.QueueRedraw();
	}

	// a glyph with nothing to do vanishes but keeps its slot, so the diamond
	// stays put
	static void ShowGlyph(InspectorGlyph glyph, bool shown)
	{
		glyph.Disabled = !shown;
		glyph.Modulate = shown ? Colors.White : Colors.Transparent;
		glyph.MouseFilter = shown ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
	}
}
