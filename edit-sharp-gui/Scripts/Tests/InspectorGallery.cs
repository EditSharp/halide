using EditSharp.Components;
using EditSharp.Components.Clips;
using EditSharp.Components.Nodes.Effects;
using EditSharp.Editing;
using EditSharp.History;
using EditSharpGUI.Scripts.UI.Inspecting;
using Godot;
using SkiaSharp;
using System;
using System.Collections.Generic;
using Vector2 = System.Numerics.Vector2;

// a page showing every part of the inspector, for looking at and tuning
// its looks. the left column is every component scene placed by hand -
// each key column state, every glyph, spin sliders in each mode, knobs, a
// section with a row per editor, a subsection, a list row - so they are
// all in the scene tree to select and inspect. the right is a real
// inspector fed a sample object with a property for every editor, a group,
// a nested object, lists of values and of objects, keyframes in every
// state, a mixed value and a value away from its default. run the scene to
// see it all live:
//
//   Scenes/Tests/InspectorGallery.tscn
public partial class InspectorGallery : Control
{
	[ExportGroup("Live")]
	[Export] Inspector inspector;

	[ExportGroup("Parts")]
	[Export] KeyColumn[] keyColumns;
	[Export] SpinSlider[] spins;
	[Export] AngleKnob[] knobs;
	[Export] InspectorRow[] rows;
	[Export] ValueEditor[] previews;

	public override void _Ready()
	{
		DressParts();
		FeedInspector();
	}

	// ---- the static column: give the self-drawn parts something to show ----

	void DressParts()
	{
		KeyState[] states = [KeyState.None, KeyState.Static, KeyState.Animated, KeyState.Keyed];

		for (int i = 0; i < keyColumns.Length && i < states.Length; i++)
			keyColumns[i].Set(states[i], hasPrev: i >= 2, hasNext: i == 2);

		if (spins.Length > 0) { spins[0].Min = 0; spins[0].Max = 1; spins[0].Step = 0.01; spins[0].Decimals = 2; spins[0].Display(0.35); }
		if (spins.Length > 1) { spins[1].Step = 0.5; spins[1].Decimals = 1; spins[1].Unit = "px"; spins[1].Display(42.5); }
		if (spins.Length > 2) { spins[2].Min = 0; spins[2].Max = 2; spins[2].Step = 0.01; spins[2].Scale = 100; spins[2].Unit = "%"; spins[2].Decimals = 0; spins[2].Display(1.5); }
		if (spins.Length > 3) { spins[3].Display(null); }
		if (spins.Length > 4) { spins[4].ReadOnly = true; spins[4].Display(3); }

		if (knobs.Length > 0) knobs[0].Display(45);
		if (knobs.Length > 1) knobs[1].Display(null);

		foreach (InspectorRow row in rows) row.Keys.Set(KeyState.Static, false, false);

		foreach (ValueEditor editor in previews)
		{
			switch (editor)
			{
				case ToggleEditor t: t.Display(true, false); break;
				case NumberEditor n: n.Display(42.5, false); break;
				case AngleEditor a: a.Display(45.0, false); break;
				case PathEditor p: p.Display("C:/media/clip.mp4", false); break;
				case MultilineEditor m: m.Display("Several\nlines of text", false); break;
				case TextEditor t: t.Display("Hello", false); break;
				case DropdownEditor d: d.Display(null, true); break;
				case ColorEditor c: c.Display(new SKColor(255, 128, 0), false); break;
				case VectorEditor v: v.Display(new Vector2(0.5f, 0.25f), false); break;
				case TimeEditor t: t.Display(TimeSpan.FromSeconds(83.5), false); break;
				case LabelEditor l: l.Display("Shown, not edited", false); break;
			}
		}
	}

	// ---- the live inspector: a sample object with a property of every kind ----

	void FeedInspector()
	{
		if (inspector is null) return;

		inspector.History = new History();
		inspector.Framerate = 30;

		GallerySample sample = new();
		GallerySample other = new() { Number = 7, Text = "Different", Toggle = false, Dropdown = ShapeType.Polygon };

		// keyframes in every state, against a playhead at half a second
		sample.Keyed.SetKeyframe(TimeSpan.FromSeconds(0.5), 20f);
		sample.Keyed.SetKeyframe(TimeSpan.FromSeconds(2), 80f);
		sample.Animated.SetKeyframe(TimeSpan.FromSeconds(1), 0.2f);
		sample.Animated.SetKeyframe(TimeSpan.FromSeconds(3), 0.9f);

		// away from its default, so the reset shows
		sample.Percent = 1.5;

		inspector.Show(
		[
			new InspectorSectionSpec("Every editor", [new InspectorTarget(sample)], GetThemeColor("video", "Clip")),
			new InspectorSectionSpec("Two objects at once", [new InspectorTarget(sample), new InspectorTarget(other)], GetThemeColor("effect", "Node")),
		]);

		inspector.Playhead = TimeSpan.FromSeconds(0.5);

		GD.Print($"GALLERY live inspector: {Count<InspectorRow>(inspector)} rows, {Count<InspectorSection>(inspector)} sections, {Count<ListRow>(inspector)} lists; static parts: {rows.Length} rows, {previews.Length} editors");
	}

	static int Count<T>(Node node) where T : Node
	{
		int n = node is T ? 1 : 0;
		foreach (Node child in node.GetChildren()) n += Count<T>(child);
		return n;
	}
}

// one property per editor the inspector has, with defaults a reset can go
// back to
public sealed class GallerySample
{
	[Editable("Toggle", Order = 0)]
	public bool Toggle { get; set; } = true;

	[Editable("Number", Order = 1, Unit = "px")]
	public double Number { get; set; } = 42.5;

	[Editable("Integer", Order = 2, Step = 1)]
	public int Integer { get; set; } = 7;

	[Editable("Slider", Order = 3, Min = 0, Max = 1, Step = 0.01)]
	public float Slider { get; set; } = 0.35f;

	[Editable("Percent", Order = 4, Min = 0, Max = 2, Step = 0.01, Editor = PropertyEditor.Percent, Default = 1.0)]
	public double Percent { get; set; } = 1.0;

	[Editable("Angle", Order = 5, Editor = PropertyEditor.Angle)]
	public float Angle { get; set; } = 45f;

	[Editable("Keyed here", Order = 6, Min = 0, Max = 100, Step = 0.5, Unit = "px")]
	public Animatable<float> Keyed { get; set; } = new(10f);

	[Editable("Animated elsewhere", Order = 7, Min = 0, Max = 1, Step = 0.01)]
	public Animatable<float> Animated { get; set; } = new(0.5f);

	[Editable("Static animatable", Order = 8, Min = -10, Max = 10, Step = 0.1)]
	public Animatable<float> StaticAnimatable { get; set; } = new(1f);

	[Editable("Text", Order = 9)]
	public string Text { get; set; } = "Hello";

	[Editable("Multiline", Order = 10, Editor = PropertyEditor.Multiline)]
	public string Multiline { get; set; } = "Several\nlines of text";

	[Editable("Path", Order = 11, Editor = PropertyEditor.Path)]
	public string Path { get; set; } = "C:/media/clip.mp4";

	[Editable("Dropdown", Order = 12)]
	public ShapeType Dropdown { get; set; } = ShapeType.Ellipse;

	[Editable("Color", Order = 13)]
	public Animatable<SKColor> Color { get; set; } = new(new SKColor(255, 128, 0));

	[Editable("Vector", Order = 14, Step = 0.01)]
	public Animatable<Vector2> Vector { get; set; } = new(new Vector2(0.5f, 0.25f));

	[Editable("Time", Order = 15)]
	public TimeSpan Time { get; set; } = TimeSpan.FromSeconds(83.5);

	[Editable("Optional time", Order = 16)]
	public TimeSpan? OptionalTime { get; set; }

	[Editable("Read only", Order = 17, ReadOnly = true)]
	public string ReadOnly { get; set; } = "Shown, not edited";

	[Editable("Timeline", Order = 18)]
	public Timeline Timeline { get; set; } = new();

	[Editable("Grouped number", Order = 19, Group = "A group", Min = 0, Max = 10, Step = 0.1)]
	public float GroupedNumber { get; set; } = 3f;

	[Editable("Grouped toggle", Order = 20, Group = "A group")]
	public bool GroupedToggle { get; set; }

	[Editable("Media", Order = 21)]
	public Source Media { get; set; } = new() { Type = SourceType.Video, Path = "C:/media/clip.mp4", Start = TimeSpan.FromSeconds(1), Duration = TimeSpan.FromSeconds(10) };

	[Editable("Nested object", Order = 22)]
	public ClipTransform Nested { get; set; } = new();

	[Editable("Values", Order = 23, Min = 0, Max = 1, Step = 0.01)]
	public List<Animatable<float>> Values { get; set; } = [new(0.25f), new(0.75f)];

	[Editable("Objects", Order = 24)]
	public List<EQBand> Objects { get; set; } = [new(), new()];
}
