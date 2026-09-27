using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EditSharpGUI.Scripts.UI.Dialogs.Platform;

// the dialog as a themed window of our own: linux, where there's no one
// native look to match, and anywhere EDITSHARP_DIALOG_HANDLER=godot. an OS
// window over its owner, which waits; the controls are the inspector's
public sealed class GodotDialogHandler : DialogHandler
{
	public override Task<string> ShowAsync(Dialog dialog, Window owner)
	{
		TaskCompletionSource<string> answer = new();
		new Showing(dialog, owner, answer).Open();
		return answer.Task;
	}

	// one dialog on screen
	sealed class Showing(Dialog dialog, Window owner, TaskCompletionSource<string> answer)
	{
		Window window;
		Label problem;
		readonly Dictionary<DialogElement, Control> rows = [];
		readonly Dictionary<DialogButton, Button> buttons = [];

		public void Open()
		{
			window = new Window
			{
				Visible = false,
				Title = dialog.Title,
				ForceNative = true,
				Transient = owner is not null,
				Exclusive = owner is not null,
				WrapControls = true,
				MinSize = new Vector2I(420, 0),
				// its dropdowns and tooltips stay in it, not in the window it belongs to
				GuiEmbedSubwindows = true,
			};
			Screens.Place(window);
			window.CloseRequested += () => Close(dialog.Cancel?.Id);
			window.WindowInput += OnKey;

			Panel background = new() { ThemeTypeVariation = "ViewBackground", MouseFilter = Control.MouseFilterEnum.Ignore };
			background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			window.AddChild(background);

			MarginContainer margin = new();
			foreach (string side in new[] { "left", "top", "right", "bottom" }) margin.AddThemeConstantOverride($"margin_{side}", 18);
			margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			window.AddChild(margin);

			VBoxContainer body = new();
			body.AddThemeConstantOverride("separation", 9);
			margin.AddChild(body);

			foreach (DialogElement element in dialog.Elements)
			{
				Control row = Build(element);
				rows[element] = row;
				body.AddChild(row);
				Apply(element);
			}

			problem = new Label { ThemeTypeVariation = "InspectorLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false, CustomMinimumSize = new Vector2(384, 0) };
			body.AddChild(problem);

			HBoxContainer bar = new() { Alignment = BoxContainer.AlignmentMode.End };
			bar.AddThemeConstantOverride("separation", 6);
			body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
			body.AddChild(bar);

			foreach (DialogButton b in dialog.Buttons)
			{
				Button button = new() { Text = b.Text, ThemeTypeVariation = "InspectorButton", CustomMinimumSize = new Vector2(90, 27) };
				button.Pressed += () => Close(b.Id);
				buttons[b] = button;
				bar.AddChild(button);
			}

			dialog.Changed += Apply;
			dialog.Edited += OnEdited;
			Validate();

			(owner ?? (Node)((SceneTree)Engine.GetMainLoop()).Root).AddChild(window);
			window.Show();

			// the first thing to type into, or the Enter button
			Control first = rows.Values.SelectMany(Descendants).OfType<LineEdit>().FirstOrDefault();
			if (first is not null) first.GrabFocus();
			else if (dialog.Default is { } d && buttons.TryGetValue(d, out Button enter)) enter.GrabFocus();
		}

		// ---- building ----

		Control Build(DialogElement element)
		{
			Control control = element switch
			{
				DialogText text => new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(384, 0), ThemeTypeVariation = text.Style == DialogText.TextStyle.Heading ? "HeaderLabel" : "InspectorLabel" },
				DialogTextField field => Field(field),
				DialogPathField path => PathField(path),
				DialogDropdown dropdown => Dropdown(dropdown),
				DialogNumber number => Number(number),
				DialogCheckbox check => Checkbox(check),
				DialogList list => new VBoxContainer(),
				_ => new Control(),
			};

			if (element is DialogText or DialogList or DialogCheckbox || string.IsNullOrEmpty(element.Label)) return control;

			// a labelled row: the words, then the control
			HBoxContainer row = new();
			row.AddThemeConstantOverride("separation", 6);
			row.AddChild(new Label { Text = element.Label, ThemeTypeVariation = "InspectorLabel", CustomMinimumSize = new Vector2(108, 24), VerticalAlignment = VerticalAlignment.Center });
			control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			row.AddChild(control);
			return row;
		}

		LineEdit Field(DialogTextField field)
		{
			LineEdit edit = new() { ThemeTypeVariation = "InspectorField", CustomMinimumSize = new Vector2(240, 24) };
			edit.TextChanged += text => dialog.UserEdited(field, text);
			edit.TextSubmitted += _ => Enter();
			return edit;
		}

		HBoxContainer PathField(DialogPathField path)
		{
			HBoxContainer box = new();
			box.AddThemeConstantOverride("separation", 6);
			LineEdit edit = new() { ThemeTypeVariation = "InspectorField", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(180, 24) };
			edit.TextChanged += text => dialog.UserEdited(path, text);
			Button browse = new() { Text = "Browse...", ThemeTypeVariation = "InspectorButton" };
			browse.Pressed += () => DisplayServer.FileDialogShow(path.Label, path.Value, "", false,
				path.Mode switch { DialogPathField.PathMode.OpenFolder => DisplayServer.FileDialogMode.OpenDir, DialogPathField.PathMode.SaveFile => DisplayServer.FileDialogMode.SaveFile, _ => DisplayServer.FileDialogMode.OpenFile },
				path.Filters, Callable.From((bool ok, string[] picked, long _) => { if (ok && picked.Length > 0) { path.Value = picked[0]; dialog.UserEdited(path, picked[0]); } }));
			box.AddChild(edit);
			box.AddChild(browse);
			return box;
		}

		OptionButton Dropdown(DialogDropdown dropdown)
		{
			OptionButton option = new() { CustomMinimumSize = new Vector2(0, 24) };
			option.ItemSelected += i => dialog.UserEdited(dropdown, (int)i);
			return option;
		}

		SpinBox Number(DialogNumber number)
		{
			SpinBox spin = new() { CustomMinimumSize = new Vector2(120, 24) };
			spin.ValueChanged += v => dialog.UserEdited(number, v);
			return spin;
		}

		CheckBox Checkbox(DialogCheckbox check)
		{
			CheckBox box = new();
			box.Toggled += on => dialog.UserEdited(check, on);
			return box;
		}

		// ---- following the elements ----

		// the element's value and state onto its control
		void Apply(DialogElement element)
		{
			if (!rows.TryGetValue(element, out Control row)) return;

			row.Visible = element.Visible;
			Control control = row is HBoxContainer labelled && labelled.GetChildCount() == 2 && labelled.GetChild(0) is Label && element is not DialogPathField ? (Control)labelled.GetChild(1) : row;

			switch (element)
			{
				case DialogText text when control is Label label:
					label.Text = text.Text;
					label.ThemeTypeVariation = text.Style == DialogText.TextStyle.Heading ? "HeaderLabel" : "InspectorLabel";
					break;

				case DialogTextField field when control is LineEdit edit:
					if (edit.Text != field.Value) edit.Text = field.Value;
					edit.PlaceholderText = field.Placeholder;
					edit.Editable = field.Enabled;
					break;

				case DialogPathField path:
					if (Descendants(row).OfType<LineEdit>().FirstOrDefault() is { } pathEdit && pathEdit.Text != path.Value) pathEdit.Text = path.Value;
					break;

				case DialogDropdown dropdown when control is OptionButton option:
					if (option.ItemCount != dropdown.Options.Length || Enumerable.Range(0, option.ItemCount).Any(i => option.GetItemText(i) != dropdown.Options[i]))
					{
						option.Clear();
						foreach (string text in dropdown.Options) option.AddItem(text);
					}
					if (option.Selected != dropdown.Selected) option.Selected = dropdown.Selected;
					option.Disabled = !dropdown.Enabled;
					break;

				case DialogNumber number when control is SpinBox spin:
					spin.MinValue = number.Min;
					spin.MaxValue = number.Max;
					spin.Step = number.Step;
					spin.Suffix = number.Suffix;
					if (spin.Value != number.Value) spin.SetValueNoSignal(number.Value);
					spin.Editable = number.Enabled;
					break;

				case DialogCheckbox check when control is CheckBox box:
					box.Text = check.Text;
					box.SetPressedNoSignal(check.Checked);
					box.Disabled = !check.Enabled;
					break;

				case DialogList list when control is VBoxContainer rowsBox:
					FillList(list, rowsBox);
					break;
			}

			Validate();
		}

		// the rows, each with its button, then the list's own buttons
		void FillList(DialogList list, VBoxContainer box)
		{
			foreach (Node child in box.GetChildren()) child.QueueFree();

			if (!string.IsNullOrEmpty(list.Label)) box.AddChild(new Label { Text = list.Label, ThemeTypeVariation = "InspectorLabel" });

			foreach (DialogListRow row in list.Rows)
			{
				HBoxContainer line = new();
				line.AddThemeConstantOverride("separation", 6);

				VBoxContainer words = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
				words.AddThemeConstantOverride("separation", 0);
				words.AddChild(new Label { Text = row.Text, ThemeTypeVariation = "InspectorLabel", ClipText = true });
				if (!string.IsNullOrEmpty(row.Detail)) words.AddChild(new Label { Text = row.Detail, ThemeTypeVariation = "SubheaderLabel", ClipText = true });
				line.AddChild(words);

				if (!string.IsNullOrEmpty(row.ButtonText))
				{
					Button button = new() { Text = row.ButtonText, ThemeTypeVariation = "InspectorButton", Disabled = !row.ButtonEnabled, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
					DialogListRow pressed = row;
					button.Pressed += () => list.Press(pressed, null);
					line.AddChild(button);
				}

				box.AddChild(line);
			}

			if (list.Buttons.Count > 0)
			{
				HBoxContainer under = new();
				under.AddThemeConstantOverride("separation", 6);
				foreach (DialogButton b in list.Buttons)
				{
					Button button = new() { Text = b.Text, ThemeTypeVariation = "InspectorButton" };
					DialogButton pressed = b;
					button.Pressed += () => list.Press(null, pressed);
					under.AddChild(button);
				}
				box.AddChild(under);
			}
		}

		// the problem, if any, and the Enter button greyed while there is one
		void Validate()
		{
			if (problem is null) return;

			string text = dialog.Problem();
			problem.Text = text ?? "";
			problem.Visible = text is not null;

			foreach ((DialogButton b, Button button) in buttons)
				if (b.Role == DialogButtonRole.Default) button.Disabled = text is not null;
		}

		void OnEdited(DialogElement _) => Validate();

		// ---- closing ----

		void OnKey(InputEvent e)
		{
			if (e is not InputEventKey { Pressed: true, Echo: false } key) return;

			if (key.Keycode == Key.Escape) { Close(dialog.Cancel?.Id); window.SetInputAsHandled(); }
			else if (key.Keycode is Key.Enter or Key.KpEnter && window.GuiGetFocusOwner() is not Button) { Enter(); window.SetInputAsHandled(); }
		}

		void Enter()
		{
			if (dialog.Default is { } d && dialog.Problem() is null) Close(d.Id);
		}

		void Close(string button)
		{
			if (answer.Task.IsCompleted) return;

			dialog.Changed -= Apply;
			dialog.Edited -= OnEdited;

			// out of the way now, so another dialog can take the owner this frame
			window.Exclusive = false;
			window.Hide();
			window.QueueFree();
			owner?.GrabFocus();
			answer.TrySetResult(button);
		}

		static IEnumerable<Node> Descendants(Node node)
		{
			foreach (Node child in node.GetChildren())
			{
				yield return child;
				foreach (Node below in Descendants(child)) yield return below;
			}
		}
	}
}
