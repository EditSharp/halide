using EditSharpGUI.Scripts.UI.Inspecting;
using EditSharpGUI.Scripts.UI.Theming;
using Godot;

namespace EditSharpGUI.Scripts.Tools.ThemeEditing;

// a live, interactive instance of whatever a theme type styles: a real
// button for Button, a panel wearing a variation, an inspector part from
// its scene. null for types with nothing to instantiate - the named
// colour groups - which the swatches cover
public static class ThemeSamples
{
	const string Inspector = "res://Scenes/Inspector/";

	public static Control For(string type, EditSharpTheme theme)
	{
		Control sample = InspectorPart(type) ?? Base(type, theme);

		if (sample is not null && theme.GetTypeVariationBase(type) != "" && sample.ThemeTypeVariation == "")
			sample.ThemeTypeVariation = type;

		return sample;
	}

	static Control Base(string type, EditSharpTheme theme)
	{
		string baseType = theme.GetTypeVariationBase(type);
		string kind = baseType != "" ? baseType : type;

		switch (kind)
		{
			case "Button": return new Button { Text = "Button", CustomMinimumSize = new(120f, 0f) };
			case "OptionButton":
			{
				OptionButton options = new() { CustomMinimumSize = new(140f, 0f) };
				options.AddItem("First");
				options.AddItem("Second");
				options.AddItem("Third");
				return options;
			}
			case "CheckBox": return new CheckBox { Text = "Check box", ButtonPressed = true };
			case "CheckButton": return new CheckButton { Text = "Check button", ButtonPressed = true };
			case "MenuButton":
			{
				MenuButton menu = new() { Text = "Menu" };
				menu.GetPopup().AddItem("First");
				menu.GetPopup().AddItem("Second");
				menu.GetPopup().AddSeparator();
				menu.GetPopup().AddItem("Third");
				return menu;
			}
			case "ColorPickerButton": return new ColorPickerButton { Text = "Colour", Color = theme.AccentColor, CustomMinimumSize = new(120f, 0f) };
			case "LinkButton": return new LinkButton { Text = "A link" };
			case "LineEdit": return new LineEdit { Text = "Line edit", CustomMinimumSize = new(220f, 0f) };
			case "TextEdit": return new TextEdit { Text = "Text edit\nwith two lines", CustomMinimumSize = new(260f, 80f) };
			case "CodeEdit": return new CodeEdit { Text = "code = edit()\nreturn code", CustomMinimumSize = new(260f, 80f) };
			case "HSlider": return new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, Value = 0.4, CustomMinimumSize = new(220f, 0f) };
			case "VSlider": return new VSlider { MinValue = 0, MaxValue = 1, Step = 0.01, Value = 0.4, CustomMinimumSize = new(0f, 120f) };
			case "HScrollBar": return new HScrollBar { MaxValue = 100, Page = 30, Value = 20, CustomMinimumSize = new(220f, 0f) };
			case "VScrollBar": return new VScrollBar { MaxValue = 100, Page = 30, Value = 20, CustomMinimumSize = new(0f, 120f) };
			case "Panel": return new Panel { CustomMinimumSize = new(260f, 80f) };
			case "PanelContainer":
			{
				PanelContainer panel = new() { CustomMinimumSize = new(260f, 0f) };
				panel.AddChild(new Label { Text = "Panel container" });
				return panel;
			}
			case "PopupPanel":
			{
				Button open = new() { Text = "Open a popup panel" };
				PopupPanel popup = new();
				popup.AddChild(new Label { Text = "A popup panel" });
				open.AddChild(popup);
				open.Pressed += () => popup.Popup(new Rect2I((Vector2I)(open.GlobalPosition + new Vector2(0f, open.Size.Y + 2f)), Vector2I.Zero));
				return open;
			}
			case "PopupMenu":
			{
				MenuButton menu = new() { Text = "Open a popup menu" };
				menu.GetPopup().AddItem("First");
				menu.GetPopup().AddItem("Second");
				menu.GetPopup().AddSeparator();
				menu.GetPopup().AddItem("Disabled");
				menu.GetPopup().SetItemDisabled(3, true);
				return menu;
			}
			case "TooltipPanel":
			case "TooltipLabel":
				return new Button { Text = "Hover for a tooltip", TooltipText = "A tooltip" };
			case "Label": return new Label { Text = "Label" };
			case "SplitContainer":
			case "HSplitContainer":
			case "VSplitContainer":
			{
				SplitContainer split = kind == "VSplitContainer" ? new VSplitContainer() : new HSplitContainer();
				split.CustomMinimumSize = new(260f, 80f);
				split.AddChild(new Panel { CustomMinimumSize = new(60f, 30f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill });
				split.AddChild(new Panel { CustomMinimumSize = new(60f, 30f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill });
				return split;
			}
			case "HBoxContainer":
			case "VBoxContainer":
			{
				BoxContainer box = kind == "VBoxContainer" ? new VBoxContainer() : new HBoxContainer();
				for (int i = 0; i < 3; i++) box.AddChild(new Panel { CustomMinimumSize = new(48f, 24f), ThemeTypeVariation = "ClipContent" });
				return box;
			}
			case "MarginContainer":
			{
				Panel outer = new() { CustomMinimumSize = new(200f, 60f), ThemeTypeVariation = "ToolbarBackground" };
				MarginContainer margin = new();
				outer.AddChild(margin);
				margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
				margin.AddChild(new Panel { ThemeTypeVariation = "ClipContent" });
				return outer;
			}
			default: return null;
		}
	}

	// the inspector's parts come from their scenes, dressed with a value
	static Control InspectorPart(string type)
	{
		switch (type)
		{
			case "InspectorSection":
			case "InspectorSubsection":
			{
				InspectorSection section = Load<InspectorSection>(type == "InspectorSection" ? "Section.tscn" : "Subsection.tscn");
				section.Title = type == "InspectorSection" ? "Section" : "Subsection";
				section.Body.AddChild(Row("Number", "Editors/Number.tscn", 42.5));
				section.CustomMinimumSize = new(320f, 0f);
				return section;
			}

			case "InspectorRow":
			case "InspectorRows":
			case "InspectorLabel":
			case "InspectorValue":
			case "InspectorEditor":
			case "InspectorEditorSlot":
			{
				Control row = Row("Number", "Editors/Number.tscn", 42.5);
				row.CustomMinimumSize = new(320f, 0f);
				return row;
			}

			case "InspectorAxis":
				return Row("Vector", "Editors/Vector.tscn", null);

			case "InspectorKeyButton":
			case "Inspector":
			{
				KeyColumn keys = Load<KeyColumn>("KeyColumn.tscn");
				keys.Ready += () => keys.Set(KeyState.Keyed, true, true);
				return keys;
			}

			case "InspectorButton":
			{
				HBoxContainer box = new() { ThemeTypeVariation = "InspectorEditor" };
				box.AddChild(Load<Button>("RemoveButton.tscn"));
				Button add = new() { Text = "+", ThemeTypeVariation = "InspectorButton", CustomMinimumSize = new(22f, 0f) };
				box.AddChild(add);
				Button frames = new() { Text = "f", ThemeTypeVariation = "InspectorButton", CustomMinimumSize = new(22f, 0f) };
				box.AddChild(frames);
				return box;
			}

			case "InspectorSwatch": return Row("Color", "Editors/Color.tscn", new SkiaSharp.SKColor(255, 128, 0));
			case "InspectorField": return Row("Text", "Editors/Text.tscn", "Hello");
			case "InspectorMultiline": return Row("Multiline", "Editors/Multiline.tscn", "Several\nlines");

			case "SpinSlider":
			{
				SpinSlider spin = Load<SpinSlider>("SpinSlider.tscn");
				spin.Min = 0; spin.Max = 1; spin.Step = 0.01; spin.Decimals = 2;
				spin.CustomMinimumSize = new(200f, 24f);
				spin.Ready += () => spin.Display(0.35);
				return spin;
			}

			case "AngleKnob":
			{
				AngleKnob knob = Load<AngleKnob>("AngleKnob.tscn");
				knob.CustomMinimumSize = new(48f, 48f);
				knob.Ready += () => knob.Display(45);
				return knob;
			}

			default: return null;
		}
	}

	static T Load<T>(string scene) where T : Node => GD.Load<PackedScene>(Inspector + scene).Instantiate<T>();

	// a row from its scene with an editor from its scene in the slot,
	// showing a value - the row's own preview mode
	static Control Row(string label, string editorScene, object value)
	{
		InspectorRow row = Load<InspectorRow>("Row.tscn");
		row.Name = label;

		ValueEditor editor = Load<ValueEditor>(editorScene);
		row.GetNode("Editor").AddChild(editor);

		row.Ready += () =>
		{
			row.Keys.Set(KeyState.Animated, true, true);
			if (value is not null) editor.Display(value, false);
			else if (editor is VectorEditor vector) vector.Display(new System.Numerics.Vector2(0.5f, 0.25f), false);
		};

		return row;
	}
}
