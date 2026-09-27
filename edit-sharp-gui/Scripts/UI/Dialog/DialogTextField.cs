using Godot;
using Godot.Collections;
using System;

namespace EditSharpGUI.Scripts.UI.Dialogs;

// one line of text
[GlobalClass]
public partial class DialogTextField : DialogElement
{
	[Export] public string Value { get; set { field = value; Touch(); } } = "";
	[Export] public string Placeholder { get; set { field = value; Touch(); } } = "";

	internal override void SetFromUser(Variant value) { Value = value.AsString(); }
}
