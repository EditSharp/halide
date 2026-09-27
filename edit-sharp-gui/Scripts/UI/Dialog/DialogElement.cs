using Godot;
using Godot.Collections;
using System;

namespace EditSharpGUI.Scripts.UI.Dialogs;

// one part of a dialog, found by Id; changing it while shown updates the dialog
[GlobalClass]
public partial class DialogElement : Resource
{
	[Export] public string Id = "";

	// the words beside it; empty for none
	[Export] public string Label { get; set { field = value; Touch(); } } = "";

	[Export] public bool Visible { get; set { field = value; Touch(); } } = true;
	[Export] public bool Enabled { get; set { field = value; Touch(); } } = true;

	// fires when code changes the element, or the user does
	public event Action<DialogElement> Changed;

	protected void Touch() => Changed?.Invoke(this);

	// a value the user changed: set without echoing it back to the dialog showing it
	internal virtual void SetFromUser(Variant value) { }
}
