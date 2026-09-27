using Godot;

namespace EditSharpGUI.Scripts.UI.Dialogs;

// what a button does to the dialog
public enum DialogButtonRole
{
	// closes the dialog with this button as the answer
	Normal,
	// the answer Enter gives; greyed out while Validate has a problem
	Default,
	// the answer Esc and the close box give
	Cancel,
	// closes it, and is marked as the one that can't be undone
	Destructive,
}

[GlobalClass]
public partial class DialogButton : Resource
{
	[Export] public string Id = "";
	[Export] public string Text = "";
	[Export] public DialogButtonRole Role = DialogButtonRole.Normal;
}
