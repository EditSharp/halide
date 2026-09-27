using Godot;
using System;
using System.Threading.Tasks;

namespace Halide.Scripts.UI.Dialogs.Platform;

// connects a shown native dialog to its resource, both ways
public sealed class DialogSession
{
	readonly Dialog dialog;
	readonly Action<DialogSnapshot> refresh;
	readonly Action<string> setProblem;
	readonly TaskCompletionSource<string> answer = new();

	public Task<string> Answer => answer.Task;

	// refresh: the dialog changed in code. setProblem: Validate's answer after the user changed something
	public DialogSession(Dialog dialog, Action<DialogSnapshot> refresh, Action<string> setProblem)
	{
		this.dialog = dialog;
		this.refresh = refresh;
		this.setProblem = setProblem;
		dialog.Changed += OnChanged;
		dialog.Edited += OnEdited;
	}

	void OnChanged(DialogElement _) => refresh(DialogSnapshot.Of(dialog));
	void OnEdited(DialogElement _) => setProblem(dialog.Problem());

	// the user changed element `index`; the value is whatever the box had: text, a number or a bool
	public void Edited(int index, object value)
	{
		if (index < 0 || index >= dialog.Elements.Count) return;

		DialogElement element = dialog.Elements[index];
		Variant converted = element switch
		{
			DialogDropdown => Convert.ToInt32(value),
			DialogNumber => Convert.ToDouble(value),
			DialogCheckbox => Convert.ToBoolean(value),
			_ => value?.ToString() ?? "",
		};
		dialog.UserEdited(element, converted);
	}

	// a row's button (row set) or one under the list (button set); -1 for neither
	public void ListPressed(int index, int row, int button)
	{
		if (index < 0 || index >= dialog.Elements.Count || dialog.Elements[index] is not DialogList list) return;
		list.Press(row >= 0 && row < list.Rows.Count ? list.Rows[row] : null, button >= 0 && button < list.Buttons.Count ? list.Buttons[button] : null);
	}

	// the OS file picker for a path element; the pick goes into it, and on to whoever listens
	public void Browse(int index)
	{
		if (index < 0 || index >= dialog.Elements.Count || dialog.Elements[index] is not DialogPathField path) return;

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

	// the box is gone: with a button's id, or null when dismissed
	public void Closed(string button)
	{
		dialog.Changed -= OnChanged;
		dialog.Edited -= OnEdited;
		answer.TrySetResult(button);
	}
}
