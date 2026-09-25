using EditSharp.Components;
using EditSharp.History;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// one property across one or more objects: its name, a reset to its
// default, an editor showing the value they share, and the key column.
// an edit writes to every object - to a keyframe at the playhead on the
// ones that are animated, to the static value on the rest - inside one
// history entry. Row.tscn lays it out
[Tool]
public partial class InspectorRow : HBoxContainer
{
	[Export] Label nameLabel;
	[Export] InspectorGlyph reset;
	[Export] Container editorSlot;
	[Export] Container trailing;
	[Export] KeyColumn keys;

	public string Label { get; private set; }
	public EditorSpec Spec { get; private set; }
	public IReadOnlyList<Binding> Bindings { get; private set; }

	public bool Animatable => Bindings.Any(b => b.Animatable is not null);

	Inspector inspector;
	ValueEditor editor;

	// half a millisecond: a keyframe set at this playhead is found again
	// at this playhead, whatever the floating point did on the way
	static readonly TimeSpan Tolerance = TimeSpan.FromTicks(5000);

	public void Configure(Inspector inspector, string label, EditorSpec spec, IReadOnlyList<Binding> bindings)
	{
		this.inspector = inspector;
		Label = label;
		Spec = spec;
		Bindings = bindings;
	}

	// the same property on other objects. the editor stays - it is the
	// same kind of value - and only the reading changes
	public void Rebind(IReadOnlyList<Binding> bindings)
	{
		// an edit still open belongs to the objects it began on
		if (Bindings is null || !bindings.SequenceEqual(Bindings, SameValue.Instance)) EndEdit();

		Bindings = bindings;
		Refresh();
	}

	// whether the choices this row offers are no longer what its object
	// offers - a font's weights, once the font has changed
	public bool ChoicesStale()
	{
		if (Spec?.Choices is null || Bindings is not [PropertyBinding first, ..]) return false;
		if (first.Target is not EditSharp.Editing.IChoiceProvider provider) return false;

		return provider.ChoicesFor(first.Descriptor.Name) is not { } now || !now.SequenceEqual(Spec.Choices);
	}

	// a row freed mid-edit (a picker left open while the selection changed)
	// would otherwise hold its history scope open, and with it every undo
	public override void _ExitTree() => EndEdit();

	// bindings are made anew for every plan; two reach the same value when
	// they read the same property of the same object
	sealed class SameValue : IEqualityComparer<Binding>
	{
		public static readonly SameValue Instance = new();

		public bool Equals(Binding a, Binding b) => (a, b) switch
		{
			(PropertyBinding x, PropertyBinding y) => x.Target == y.Target && x.Descriptor == y.Descriptor,
			_ => ReferenceEquals(a, b)
		};

		public int GetHashCode(Binding b) => 0;
	}

	// the key column, for a row placed by hand in a scene to show a state
	public KeyColumn Keys => keys;

	public override void _Ready()
	{
		// a row placed in a scene by hand, with no property behind it: it
		// shows as laid out, named after its node, with whatever editor was
		// put in its slot - a gallery, a mock-up
		if (Spec is null)
		{
			if (string.IsNullOrEmpty(nameLabel.Text)) nameLabel.Text = Name;
			foreach (Control c in pendingTrailing) trailing.AddChild(c);
			pendingTrailing.Clear();
			return;
		}

		nameLabel.Text = Label;
		nameLabel.TooltipText = Spec.Tooltip ?? "";

		editor = ValueEditor.Create(Spec, inspector);
		editor.EditBegan += BeginEdit;
		editor.ValueChanged += v => Write(v);
		editor.ValueCommitted += v => { Write(v); EndEdit(); };
		editor.EditEnded += EndEdit;
		editorSlot.AddChild(editor);

		foreach (Control c in pendingTrailing) trailing.AddChild(c);
		pendingTrailing.Clear();

		reset.Pressed += ResetToDefault;

		keys.KeyPressed += ToggleKeyframe;
		keys.PrevPressed += () => Seek(previous: true);
		keys.NextPressed += () => Seek(previous: false);
		keys.ResetTrackPressed += ResetTrack;

		Refresh();
	}

	// controls placed between the editor and the key column - a list
	// item's remove button
	readonly List<Control> pendingTrailing = [];

	public void AddTrailing(Control control)
	{
		if (trailing is not null && IsNodeReady()) trailing.AddChild(control);
		else pendingTrailing.Add(control);
	}

	// ---- reading ----

	// what an object's value is right now: the static value, or, when it is
	// keyed, the value at the playhead. with a single keyframe the track is
	// not yet in charge of rendering, so the keyframe itself is shown
	object Display(Binding binding)
	{
		IAnimatable a = binding.Animatable;

		if (a is null || a.Keyframes.Count == 0) return binding.Get();
		if (a.Keyframes.Count == 1) return a.Keyframes[0].Value;

		return a.Evaluate(binding.ContentTime(inspector.Playhead));
	}

	public void Refresh()
	{
		if (editor is null) return;

		Visible = Bindings.Any(b => b.IsVisible);
		if (!Visible) return;

		object first = null;
		bool mixed = false;

		for (int i = 0; i < Bindings.Count; i++)
		{
			object value = Display(Bindings[i]);

			if (i == 0) first = value;
			else if (!Equals(first, value)) { mixed = true; break; }
		}

		if (!editor.IsEditing) editor.Display(first, mixed);

		RefreshReset();
		RefreshKeys();
	}

	// only what the playhead moving can change
	public void RefreshForTime()
	{
		if (!Animatable || !Visible) return;
		Refresh();
	}

	// the reset shows when the value is not its default - and only when
	// every object has a default to go back to
	void RefreshReset()
	{
		bool canReset = !Spec.ReadOnly;
		bool allDefault = true;

		foreach (Binding b in Bindings)
		{
			if (!b.TryGetDefault(out object expected)) { canReset = false; break; }
			if (!Equals(Display(b), expected)) allDefault = false;
		}

		reset.Visible = canReset && !allDefault;
	}

	void RefreshKeys()
	{
		if (!Animatable)
		{
			keys.Set(KeyState.None, false, false);
			return;
		}

		bool anyKeys = false, allHere = true, anyPrev = false, anyNext = false;

		foreach (Binding b in Bindings)
		{
			IAnimatable a = b.Animatable;
			if (a is null || a.Keyframes.Count == 0) { allHere = false; continue; }

			anyKeys = true;
			TimeSpan t = b.ContentTime(inspector.Playhead);

			if (!a.Keyframes.Any(k => Near(k.Start, t))) allHere = false;
			if (a.Keyframes.Any(k => k.Start < t - Tolerance)) anyPrev = true;
			if (a.Keyframes.Any(k => k.Start > t + Tolerance)) anyNext = true;
		}

		keys.Set(!anyKeys ? KeyState.Static : allHere ? KeyState.Keyed : KeyState.Animated, anyPrev, anyNext);
	}

	static bool Near(TimeSpan a, TimeSpan b) => (a - b).Duration() <= Tolerance;

	// ---- writing ----

	Transaction.Scope scope;

	// what each binding held when the edit began, and the last value
	// written, so the commit can say what it replaced
	object[] before;
	object last;

	void BeginEdit()
	{
		if (scope is null) before = Snapshot();
		scope ??= inspector.BeginChange($"Set {Label}");
	}

	void EndEdit()
	{
		if (scope is null) return;

		scope.Commit();
		scope.Dispose();
		scope = null;

		if (before is not null) inspector.NotifyCommitted(new InspectorEditArgs(Label, Bindings, before, last));
		before = null;
	}

	object[] Snapshot() => [.. Bindings.Select(b => b.Get())];

	// write a value as if the user had entered it: to every object, as a
	// keyframe where the property is keyed, in one history entry
	public void Apply(object value) => Write(value);

	void Write(object value, string description = null)
	{
		if (Spec.ReadOnly) return;

		bool own = scope is null;
		if (own) { before = Snapshot(); scope = inspector.BeginChange(description ?? $"Set {Label}"); }
		last = value;

		try
		{
			foreach (Binding b in Bindings)
			{
				IAnimatable a = b.Animatable;

				if (a is not null && a.Keyframes.Count > 0)
				{
					a.SetKeyframe(b.ContentTime(inspector.Playhead), value);

					// one keyframe does not yet drive the render: keep the
					// static value in step so what is shown is what plays
					if (a.Keyframes.Count == 1) a.SetStaticValue(value);
				}
				else b.Set(value);
			}
		}
		catch (Exception e)
		{
			GD.PushWarning($"Could not set {Label}: {e.Message}");
		}

		if (own) EndEdit();

		Refresh();
		inspector.NotifyEdited();
	}

	// back to the default, written like any edit - onto the keyframe at
	// the playhead when the property is keyed
	public void ResetToDefault()
	{
		if (Bindings.Count == 0 || !Bindings[0].TryGetDefault(out object value)) return;
		Write(value, $"Reset {Label}");
	}

	// ---- keyframes ----

	// add a keyframe at the playhead on every object, or remove the ones
	// there when they all have one - the diamond
	public void ToggleKeyframe()
	{
		if (!Animatable || Spec.ReadOnly) return;

		bool remove = true;

		foreach (Binding b in Bindings)
		{
			IAnimatable a = b.Animatable;
			if (a is null) continue;
			if (!a.Keyframes.Any(k => Near(k.Start, b.ContentTime(inspector.Playhead)))) { remove = false; break; }
		}

		using (Transaction.Scope change = inspector.BeginChange(remove ? $"Remove {Label} keyframe" : $"Add {Label} keyframe"))
		{
			foreach (Binding b in Bindings)
			{
				IAnimatable a = b.Animatable;
				if (a is null) continue;

				TimeSpan t = b.ContentTime(inspector.Playhead);

				if (remove)
				{
					IKeyframe here = a.Keyframes.FirstOrDefault(k => Near(k.Start, t));
					if (here is not null) a.RemoveKeyframeAt(here.Start);
				}
				else a.SetKeyframe(t, Display(b));
			}

			change?.Commit();
		}

		inspector.NotifyEdited();
		Refresh();
	}

	// drop every keyframe and keep what is on screen: the value at the
	// playhead becomes the static value
	public void ResetTrack()
	{
		if (!Animatable || Spec.ReadOnly) return;

		using (Transaction.Scope change = inspector.BeginChange($"Reset {Label} keyframes"))
		{
			foreach (Binding b in Bindings)
			{
				IAnimatable a = b.Animatable;
				if (a is null || a.Keyframes.Count == 0) continue;

				object now = Display(b);
				a.ClearKeyframes();
				a.SetStaticValue(now);
			}

			change?.Commit();
		}

		inspector.NotifyEdited();
		Refresh();
	}

	// the nearest keyframe on this row's objects on that side of the
	// playhead, in timeline time
	void Seek(bool previous)
	{
		TimeSpan playhead = inspector.Playhead;
		TimeSpan? best = null;

		foreach (Binding b in Bindings)
		{
			IAnimatable a = b.Animatable;
			if (a is null) continue;

			foreach (IKeyframe k in a.Keyframes)
			{
				TimeSpan t = b.TimelineTime(k.Start);

				if (previous ? t >= playhead - Tolerance : t <= playhead + Tolerance) continue;
				if (best is null || (previous ? t > best : t < best)) best = t;
			}
		}

		if (best is TimeSpan target) inspector.RequestSeek(target);
	}
}
