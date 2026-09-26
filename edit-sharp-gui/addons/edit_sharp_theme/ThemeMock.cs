using EditSharp.Components;
using EditSharp.Components.Nodes.Effects;
using EditSharpGUI.Scripts.UI.Theming;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EditSharpGUI.Addons.Theme;

// the mock page the tool previews on: the real inspector over a sample
// object with every editor, fold, list and keyframe state; a board of the
// base controls in their states; a board of the view variations; and a
// strip of the definitions. all real and live, so every edit shows in
// context
public static class ThemeMock
{
	public static void Build(VBoxContainer into, EditSharpTheme theme, PackedScene inspectorScene, List<(ColorRect Tile, ThemeDefinition Definition)> palette)
	{
		// ---- definitions ----
		into.AddChild(Heading("Definitions"));
		HFlowContainer strip = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		into.AddChild(strip);

		foreach (ThemeDefinition definition in Enum.GetValues<ThemeDefinition>())
		{
			if (definition is ThemeDefinition.None or ThemeDefinition.Transparent) continue;

			VBoxContainer cell = new() { ThemeTypeVariation = "TightVBox" };
			ColorRect tile = new() { CustomMinimumSize = new(72f, 28f), Color = theme.Resolve(definition), TooltipText = definition.ToString() };
			cell.AddChild(tile);
			cell.AddChild(new Label { Text = Short(definition.ToString()), ThemeTypeVariation = "InspectorAxis", ClipText = true, CustomMinimumSize = new(72f, 0f) });
			strip.AddChild(cell);
			palette.Add((tile, definition));
		}

		// ---- the inspector ----
		into.AddChild(Heading("Inspector"));

		if (inspectorScene?.Instantiate() is Inspector inspector)
		{
			inspector.CustomMinimumSize = new(0f, 1100f);
			inspector.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			into.AddChild(inspector);
			Feed(inspector, theme);
		}

		// ---- controls ----
		into.AddChild(Heading("Controls"));
		GridContainer controls = new() { Columns = 3, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		into.AddChild(controls);

		foreach (string type in theme.GetTypeList().OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
		{
			if (theme.GetTypeVariationBase(type) != "" || type.StartsWith("Inspector", StringComparison.Ordinal)) continue;

			Control sample = ThemeSamples.For(type, theme);
			if (sample is null) continue;

			controls.AddChild(Cell(type, sample));

			// the states a control has, as real controls in those states
			switch (type)
			{
				case "Button":
					controls.AddChild(Cell("Button, pressed", new Button { Text = "Pressed", ToggleMode = true, ButtonPressed = true, CustomMinimumSize = new(120f, 0f) }));
					controls.AddChild(Cell("Button, disabled", new Button { Text = "Disabled", Disabled = true, CustomMinimumSize = new(120f, 0f) }));
					break;
				case "LineEdit":
					controls.AddChild(Cell("LineEdit, read only", new LineEdit { Text = "Read only", Editable = false, CustomMinimumSize = new(200f, 0f) }));
					controls.AddChild(Cell("LineEdit, placeholder", new LineEdit { PlaceholderText = "Placeholder", CustomMinimumSize = new(200f, 0f) }));
					break;
				case "CheckBox":
					controls.AddChild(Cell("CheckBox, off", new CheckBox { Text = "Off" }));
					controls.AddChild(Cell("CheckBox, disabled", new CheckBox { Text = "Disabled", Disabled = true, ButtonPressed = true }));
					break;
				case "CheckButton":
					controls.AddChild(Cell("CheckButton, off", new CheckButton { Text = "Off" }));
					break;
				case "OptionButton":
					controls.AddChild(Cell("OptionButton, disabled", new OptionButton { Text = "Disabled", Disabled = true, CustomMinimumSize = new(140f, 0f) }));
					break;
			}
		}

		// ---- views ----
		into.AddChild(Heading("Views"));
		GridContainer views = new() { Columns = 3, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		into.AddChild(views);

		foreach (string type in theme.GetTypeList().OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
		{
			if (theme.GetTypeVariationBase(type) == "" || type.StartsWith("Inspector", StringComparison.Ordinal)) continue;

			Control sample = ThemeSamples.For(type, theme);
			if (sample is not null) views.AddChild(Cell(type, sample));
		}
	}

	// the inspector over the sample: keyframes in every state against a
	// playhead at half a second, a value away from its default, and two
	// objects at once for mixed values
	static void Feed(Inspector inspector, EditSharpTheme theme)
	{
		inspector.Framerate = 30;

		GallerySample sample = new();
		GallerySample other = new() { Number = 7, Text = "Different", Toggle = false, Dropdown = ShapeType.Polygon, Enabled = false };

		sample.Keyed.SetKeyframe(Time.FromSeconds(0.5), 20f);
		sample.Keyed.SetKeyframe(Time.FromSeconds(2), 80f);
		sample.Animated.SetKeyframe(Time.FromSeconds(1), 0.2f);
		sample.Animated.SetKeyframe(Time.FromSeconds(3), 0.9f);
		sample.Percent = 1.5;

		inspector.Show(
		[
			new InspectorSectionSpec("Every editor", [new InspectorTarget(sample)], theme.GetColor("video", "Clip")),
			new InspectorSectionSpec("Two objects at once", [new InspectorTarget(sample), new InspectorTarget(other)], theme.GetColor("effect", "Node")),
		]);

		inspector.Playhead = Time.FromSeconds(0.5);
	}

	static Control Cell(string title, Control sample)
	{
		VBoxContainer cell = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		cell.AddChild(new Label { Text = title, ThemeTypeVariation = "InspectorLabel" });
		CenterContainer centre = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new(0f, 36f) };
		centre.AddChild(sample);
		cell.AddChild(centre);
		return cell;
	}

	static Label Heading(string text) => new() { Text = text, ThemeTypeVariation = "ClipName" };

	// "GeneratorVideoClipColor" -> "Generator video clip"
	static string Short(string name)
	{
		if (name.EndsWith("Color")) name = name[..^5];

		System.Text.StringBuilder text = new(name.Length + 4);

		for (int i = 0; i < name.Length; i++)
		{
			char c = name[i];

			if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]) && !char.IsDigit(name[i - 1]))
			{
				text.Append(' ');
				text.Append(char.ToLowerInvariant(c));
			}
			else text.Append(c);
		}

		return text.ToString();
	}
}
