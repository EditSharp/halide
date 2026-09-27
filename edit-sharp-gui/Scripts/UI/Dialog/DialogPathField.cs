using Godot;
using Godot.Collections;
using System;

namespace EditSharpGUI.Scripts.UI.Dialogs;

// a path, with a Browse... button that opens the OS picker
[GlobalClass]
public partial class DialogPathField : DialogElement
{
	public enum PathMode { OpenFile, OpenFolder, SaveFile }

	[Export] public string Value { get; set { field = value; Touch(); } } = "";
	[Export] public PathMode Mode = PathMode.OpenFile;

	// "*.esproj;EditSharp projects", as godot's file dialogs take them
	[Export] public string[] Filters = [];

	internal override void SetFromUser(Variant value) { Value = value.AsString(); }
}
