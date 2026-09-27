using Godot;
using static Godot.GodotObject;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EditSharpGUI.Scripts.UI.Dialogs.Platform;

// the dialog as a themed window of our own (Linux, or EDITSHARP_DIALOG_HANDLER=godot), built from Scenes/Dialog
public sealed class GodotDialogHandler : DialogHandler
{
	const string ViewScene = "res://Scenes/Dialog/GodotDialog.tscn";

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
		GodotDialogView view;
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

			view = GD.Load<PackedScene>(ViewScene).Instantiate<GodotDialogView>();
			window.AddChild(view);

			bool labels = dialog.Elements.Any(e => e is DialogTextField or DialogPathField or DialogDropdown or DialogNumber && e.Label.Length > 0);
			foreach (DialogElement element in dialog.Elements)
			{
				Control row = Build(element, labels);
				rows[element] = row;
				view.Elements.AddChild(row);
				Apply(element);
			}

			foreach (DialogButton b in dialog.Buttons)
			{
				Button button = view.Button.Instantiate<Button>();
				button.Text = b.Text;
				button.Pressed += () => Close(b.Id);
				buttons[b] = button;
				view.Buttons.AddChild(button);
			}

			dialog.Changed += Apply;
			dialog.Edited += OnEdited;
			Validate();

			(owner ?? (Node)((SceneTree)Engine.GetMainLoop()).Root).AddChild(window);
			window.Show();

			// the first thing to type into, or the Enter button
			if (rows.Values.Select(r => r.GetNodeOrNull<LineEdit>("%Field")).FirstOrDefault(f => f is not null) is { } first) first.GrabFocus();
			else if (dialog.Default is { } d && buttons.TryGetValue(d, out Button enter)) enter.GrabFocus();
		}

		// ---- building ----

		Control Build(DialogElement element, bool labels)
		{
			Control row = element switch
			{
				DialogText => view.Text.Instantiate<Control>(),
				DialogTextField => view.Field.Instantiate<Control>(),
				DialogPathField => view.Path.Instantiate<Control>(),
				DialogDropdown => view.Dropdown.Instantiate<Control>(),
				DialogNumber => view.Number.Instantiate<Control>(),
				DialogCheckbox => view.Checkbox.Instantiate<Control>(),
				DialogList => view.List.Instantiate<Control>(),
				_ => new Control(),
			};

			switch (element)
			{
				case DialogTextField field:
					LineEdit edit = row.GetNode<LineEdit>("%Field");
					edit.TextChanged += text => dialog.UserEdited(field, text);
					edit.TextSubmitted += _ => Enter();
					break;

				case DialogPathField path:
					row.GetNode<LineEdit>("%Field").TextChanged += text => dialog.UserEdited(path, text);
					row.GetNode<Button>("%Browse").Pressed += () => Browse(path);
					break;

				case DialogDropdown dropdown:
					row.GetNode<OptionButton>("%Dropdown").ItemSelected += i => dialog.UserEdited(dropdown, (int)i);
					break;

				case DialogNumber number:
					row.GetNode<SpinBox>("%Number").ValueChanged += v => dialog.UserEdited(number, v);
					break;

				case DialogCheckbox check:
					row.GetNode<Control>("%Indent").Visible = labels;
					row.GetNode<CheckBox>("%Box").Toggled += on => dialog.UserEdited(check, on);
					break;
			}

			return row;
		}

		void Browse(DialogPathField path)
		{
			DisplayServer.FileDialogMode mode = path.Mode switch
			{
				DialogPathField.PathMode.OpenFolder => DisplayServer.FileDialogMode.OpenDir,
				DialogPathField.PathMode.SaveFile => DisplayServer.FileDialogMode.SaveFile,
				_ => DisplayServer.FileDialogMode.OpenFile,
			};

			DisplayServer.FileDialogShow(path.Label, path.Value, "", false, mode, path.Filters, Callable.From((bool ok, string[] picked, long _) =>
			{
				if (!ok || picked.Length == 0) return;
				path.Value = picked[0];
				dialog.UserEdited(path, picked[0]);
			}));
		}

		// ---- following the elements ----

		// the element's value and state onto its row
		void Apply(DialogElement element)
		{
			if (!rows.TryGetValue(element, out Control row)) return;

			row.Visible = element.Visible;
			if (row.GetNodeOrNull<Label>("%Label") is Label label)
			{
				label.Text = element.Label;
				label.Visible = element.Label.Length > 0;
			}

			switch (element)
			{
				case DialogText text when row is Label words:
					words.Text = text.Text;
					words.ThemeTypeVariation = text.Style switch
					{
						DialogText.TextStyle.Heading => "HeaderLabel",
						DialogText.TextStyle.Error => "ErrorLabel",
						_ => "InspectorLabel",
					};
					break;

				case DialogTextField field:
					LineEdit edit = row.GetNode<LineEdit>("%Field");
					if (edit.Text != field.Value) edit.Text = field.Value;
					edit.PlaceholderText = field.Placeholder;
					edit.Editable = field.Enabled;
					break;

				case DialogPathField path:
					LineEdit pathEdit = row.GetNode<LineEdit>("%Field");
					if (pathEdit.Text != path.Value) pathEdit.Text = path.Value;
					pathEdit.Editable = path.Enabled;
					row.GetNode<Button>("%Browse").Disabled = !path.Enabled;
					break;

				case DialogDropdown dropdown:
					OptionButton option = row.GetNode<OptionButton>("%Dropdown");
					if (option.ItemCount != dropdown.Options.Length || Enumerable.Range(0, option.ItemCount).Any(i => option.GetItemText(i) != dropdown.Options[i]))
					{
						option.Clear();
						foreach (string text in dropdown.Options) option.AddItem(text);
					}
					if (option.Selected != dropdown.Selected) option.Selected = dropdown.Selected;
					option.Disabled = !dropdown.Enabled;
					break;

				case DialogNumber number:
					SpinBox spin = row.GetNode<SpinBox>("%Number");
					spin.MinValue = number.Min;
					spin.MaxValue = number.Max;
					spin.Step = number.Step;
					spin.Suffix = number.Suffix;
					if (spin.Value != number.Value) spin.SetValueNoSignal(number.Value);
					spin.Editable = number.Enabled;
					break;

				case DialogCheckbox check:
					CheckBox box = row.GetNode<CheckBox>("%Box");
					box.Text = check.Text;
					box.SetPressedNoSignal(check.Checked);
					box.Disabled = !check.Enabled;
					break;

				case DialogList list:
					FillList(list, row);
					break;
			}

			Validate();

			// fitted to what's shown now, smaller as well as larger
			if (window.Visible) Callable.From(() => { if (IsInstanceValid(window)) window.ResetSize(); }).CallDeferred();
		}

		// the rows, each with its button, then the list's own buttons
		void FillList(DialogList list, Control block)
		{
			VBoxContainer items = block.GetNode<VBoxContainer>("%Rows");
			HBoxContainer under = block.GetNode<HBoxContainer>("%Buttons");
			// out of the layout at once, so the window can fit what replaces them
			foreach (Node child in items.GetChildren().Concat(under.GetChildren()))
			{
				child.GetParent().RemoveChild(child);
				child.QueueFree();
			}

			foreach (DialogListRow row in list.Rows)
			{
				Control item = view.ListItem.Instantiate<Control>();
				item.GetNode<Label>("%Text").Text = row.Text;
				Label detail = item.GetNode<Label>("%Detail");
				detail.Text = row.Detail;
				detail.Visible = row.Detail.Length > 0;

				Button press = item.GetNode<Button>("%Press");
				press.Text = row.ButtonText;
				press.Visible = row.ButtonText.Length > 0;
				press.Disabled = !row.ButtonEnabled || !list.Enabled;
				DialogListRow pressed = row;
				press.Pressed += () => list.Press(pressed, null);
				items.AddChild(item);
			}

			foreach (DialogButton b in list.Buttons)
			{
				Button button = view.Button.Instantiate<Button>();
				button.Text = b.Text;
				DialogButton pressed = b;
				button.Pressed += () => list.Press(null, pressed);
				under.AddChild(button);
			}

			items.Visible = list.Rows.Count > 0;
			under.Visible = list.Buttons.Count > 0;
		}

		// the problem, if any, and the Enter button greyed while there is one
		void Validate()
		{
			if (view is null) return;

			string text = dialog.Problem();
			view.Problem.Text = text ?? "";
			view.Problem.Visible = text is not null;

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
	}
}
