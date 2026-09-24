using Godot;
using System;

public partial class TestPopup : Control
{
	[Export] PopupMenu popup;
	public override void _Ready()
	{
		popup.Popup();
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}
}
