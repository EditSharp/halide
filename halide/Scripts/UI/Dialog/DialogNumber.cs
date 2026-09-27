using Godot;
using Godot.Collections;
using System;

namespace Halide.Scripts.UI.Dialogs;

// a number in a range
[GlobalClass]
public partial class DialogNumber : DialogElement
{
	[Export] public double Value { get; set { field = value; Touch(); } }
	[Export] public double Min = 0d;
	[Export] public double Max = 100d;
	[Export] public double Step = 1d;
	[Export] public string Suffix = "";

	internal override void SetFromUser(Variant value) { Value = value.AsDouble(); }
}
