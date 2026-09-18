using EditSharpGUI.Scripts.Input;
using Godot;
using System;

public partial class UIPlayhead : VBoxContainer
{
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
	}

	public override void _GuiInput(InputEvent _)
	{
		MouseButtonState left = InputManager.Singleton.Mouse.LeftButton;

        switch (left.Action)
        {
            case MouseAction.Press:
            case MouseAction.DoubleClick:
                // claim the press, so whatever drag follows is unambiguously ours
                if (!left.Capture(this)) break;
                GD.Print("Playhead clicked");
                break;
			case MouseAction.DragStart:
			case MouseAction.DragMove:
				if (!left.HasCapture(this)) break;

				break;
        }
	}

	public void Move()
	{
		
	}
}
