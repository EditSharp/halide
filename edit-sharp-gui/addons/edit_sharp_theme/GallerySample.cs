using EditSharp.Components;
using EditSharp.Components.Clips;
using EditSharp.Components.Nodes.Effects;
using EditSharp.Editing;
using SkiaSharp;
using System;
using System.Collections.Generic;
using Vector2 = System.Numerics.Vector2;

namespace EditSharpGUI.Addons.Theme;

// one property per editor the inspector has, with defaults a reset can go
// back to: what the tool's mock inspector shows, so every editor, fold,
// list and keyframe state is on the page at once
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

	// a node-like object: its Enabled goes on the section header as a switch
	[Editable("Enabled", Order = -100)]
	public bool Enabled { get; set; } = true;
}
