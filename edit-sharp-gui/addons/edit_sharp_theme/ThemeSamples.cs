using EditSharpGUI.Scripts.UI.Theming;
using Godot;

namespace EditSharpGUI.Addons.Theme;

// a live instance of what a theme type styles, for the mock page: a real
// button for Button, a panel wearing a variation. null for types with
// nothing to instantiate - the named colour groups
public static class ThemeSamples
{
	public static Control For(string type, EditSharpTheme theme)
	{
		Control sample = Base(type, theme);

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
			case "ColorPickerButton": return new ColorPickerButton { Text = "Colour", Color = theme.Resolve(ThemeDefinition.AccentColor), CustomMinimumSize = new(120f, 0f) };
			case "LinkButton": return new LinkButton { Text = "A link" };
			case "LineEdit": return new LineEdit { Text = "Line edit", CustomMinimumSize = new(200f, 0f) };
			case "TextEdit": return new TextEdit { Text = "Text edit\nwith two lines", CustomMinimumSize = new(220f, 72f) };
			case "CodeEdit": return new CodeEdit { Text = "code = edit()\nreturn code", CustomMinimumSize = new(220f, 72f) };
			case "HSlider": return new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, Value = 0.4, CustomMinimumSize = new(200f, 0f) };
			case "VSlider": return new VSlider { MinValue = 0, MaxValue = 1, Step = 0.01, Value = 0.4, CustomMinimumSize = new(0f, 100f) };
			case "HScrollBar": return new HScrollBar { MaxValue = 100, Page = 30, Value = 20, CustomMinimumSize = new(200f, 0f) };
			case "VScrollBar": return new VScrollBar { MaxValue = 100, Page = 30, Value = 20, CustomMinimumSize = new(0f, 100f) };
			case "Panel": return new Panel { CustomMinimumSize = new(220f, 64f) };
			case "PanelContainer":
			{
				PanelContainer panel = new() { CustomMinimumSize = new(220f, 0f) };
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
			case "LineEditReadOnly": return new LineEdit { Text = "Read only", Editable = false, CustomMinimumSize = new(200f, 0f) };
			case "SplitContainer":
			case "HSplitContainer":
			case "VSplitContainer":
			{
				SplitContainer split = kind == "VSplitContainer" ? new VSplitContainer() : new HSplitContainer();
				split.CustomMinimumSize = new(220f, 64f);
				split.AddChild(new Panel { CustomMinimumSize = new(48f, 24f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill });
				split.AddChild(new Panel { CustomMinimumSize = new(48f, 24f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill });
				return split;
			}
			case "HBoxContainer":
			case "VBoxContainer":
			{
				BoxContainer box = kind == "VBoxContainer" ? new VBoxContainer() : new HBoxContainer();
				for (int i = 0; i < 3; i++) box.AddChild(new Panel { CustomMinimumSize = new(40f, 20f), ThemeTypeVariation = "ClipContent" });
				return box;
			}
			case "MarginContainer":
			{
				Panel outer = new() { CustomMinimumSize = new(180f, 56f), ThemeTypeVariation = "ToolbarBackground" };
				MarginContainer margin = new();
				outer.AddChild(margin);
				margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
				margin.AddChild(new Panel { ThemeTypeVariation = "ClipContent" });
				return outer;
			}
			default: return null;
		}
	}
}
