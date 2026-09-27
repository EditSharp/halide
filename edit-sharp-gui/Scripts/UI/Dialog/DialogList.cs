using Godot;
using Godot.Collections;
using System;

namespace EditSharpGUI.Scripts.UI.Dialogs;

// rows with a button each, and buttons under the list; pressing one keeps the dialog open
[GlobalClass]
public partial class DialogList : DialogElement
{
	[Export] public Array<DialogListRow> Rows { get; set { field = value; Touch(); } } = [];

	// under the list, for the whole of it
	[Export] public Array<DialogButton> Buttons = [];

	// a row's button (row set) or one under the list (row null)
	public event Action<DialogListRow, DialogButton> Pressed;

	internal void Press(DialogListRow row, DialogButton button) => Pressed?.Invoke(row, button);

	// the list changed shape or a row changed: the dialog shows it again
	public void Refresh() => Touch();
}
