using Godot;
using Godot.Collections;
using System;

namespace Halide.Scripts.UI.Dialogs;

// words on their own: the question, an explanation, an error
[GlobalClass]
public partial class DialogText : DialogElement
{
	public enum TextStyle { Body, Heading, Error }

	[Export] public string Text { get; set { field = value; Touch(); } } = "";
	[Export] public TextStyle Style { get; set { field = value; Touch(); } } = TextStyle.Body;
}
