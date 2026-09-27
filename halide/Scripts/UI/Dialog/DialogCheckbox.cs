using Godot;
using Godot.Collections;
using System;

namespace Halide.Scripts.UI.Dialogs;

// on or off, with its words beside the box
[GlobalClass]
public partial class DialogCheckbox : DialogElement
{
	[Export] public string Text { get; set { field = value; Touch(); } } = "";
	[Export] public bool Checked { get; set { field = value; Touch(); } }

	internal override void SetFromUser(Variant value) { Checked = value.AsBool(); }
}
