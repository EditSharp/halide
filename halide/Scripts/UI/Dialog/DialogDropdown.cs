using Godot;
using Godot.Collections;
using System;

namespace Halide.Scripts.UI.Dialogs;

// a pick from a list
[GlobalClass]
public partial class DialogDropdown : DialogElement
{
	[Export] public string[] Options { get; set { field = value; Touch(); } } = [];
	[Export] public int Selected { get; set { field = value; Touch(); } }

	internal override void SetFromUser(Variant value) { Selected = value.AsInt32(); }
}
