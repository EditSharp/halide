using Godot;
using Godot.Collections;
using System;

namespace Halide.Scripts.UI.Dialogs;

[GlobalClass]
public partial class DialogListRow : Resource
{
	[Export] public string Id = "";
	[Export] public string Text = "";
	[Export] public string Detail = "";
	[Export] public string ButtonText = "";
	[Export] public bool ButtonEnabled = true;
}
