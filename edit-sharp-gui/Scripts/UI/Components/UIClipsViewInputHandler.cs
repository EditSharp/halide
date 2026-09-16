using System;
using EditSharp.Components.Clips;
using Godot;
using static UIClipsView;

namespace EditSharpGUI.Scripts.UI.Components;

public class UIClipsViewInputHandler
{
    class Mouse
	{
		public MouseState State = MouseState.Released;

		public Vector2 CurrentPosition, LastClickPosition;

		public const int MIN_DRAG_PIXELS = 2;
		public Vector2 DragDelta => -(LastClickPosition - CurrentPosition);
		public bool IsDragging => Mathf.Abs(DragDelta.X) > MIN_DRAG_PIXELS || Mathf.Abs(DragDelta.Y) > MIN_DRAG_PIXELS;
	}
	
	enum MouseState
	{
		Released,
		Clicking,
		Dragging
	}

    Mouse mouse = new();

    required public UIClipsView ClipsView;
    required public UIClip UIClip;
    required public Clip Clip;

    public void Input(InputEvent @event)
	{
		if (@event is InputEventMouse m)
		{
			if (m is InputEventMouseButton mb)
			{
				if (mb.ButtonIndex == MouseButton.Left)
				{
					if (mb.Pressed)
					{
						mouse.LastClickPosition = mb.GlobalPosition;

						if (mb.DoubleClick)
						{
							GD.Print($"clips view: double clicked");
						}
						else
						{
							if (mb.IsCommandOrControlPressed())
							{
								ClipsView.DeselectClip(UIClip);
								GD.Print($"clips view: ctrl clicked");
							}
							else if (mb.ShiftPressed)
							{
								ClipsView.SelectClip(UIClip, SelectionMode.Inclusive);
								GD.Print($"clips view: shift clicked");
							}
							else
							{
								ClipsView.SelectClip(UIClip, SelectionMode.ExclusiveIfUnselected);
								GD.Print($"clips view: click started");
							}
						}
						
						mouse.State = MouseState.Clicking;
					}
					else
					{
						// release the mouse before handling it - a handler that throws
						// must not leave this clip stuck in a drag it can never end
						bool wasDragging = mouse.State == MouseState.Dragging;
						mouse.State = MouseState.Released;

						if (wasDragging)
						{
							ClipsView.FinishDrag(UIClip, (mouse.LastClickPosition, mouse.DragDelta));
							GD.Print($"clips view: drag finished");
						}
						else
						{
							if (mb.IsCommandOrControlPressed())
							{
								GD.Print($"clips view: ctrl click finished");
							}
							else if (mb.ShiftPressed)
							{
								GD.Print($"clips view: shift click finished");
							}
							else
							{
								ClipsView.SelectClip(UIClip, SelectionMode.Exclusive);
								GD.Print($"clips view: click finished");
							}
						}
					}
				}
			}
			else if (m is InputEventMouseMotion mm)
			{
				mouse.CurrentPosition = mm.GlobalPosition;

				if (mouse.State == MouseState.Clicking)
				{
					if (mouse.IsDragging)
					{
						mouse.State = MouseState.Dragging;
						ClipsView.DragSelection(UIClip, (mouse.LastClickPosition, mouse.DragDelta));
						GD.Print($"clips view: drag started (original pos: {mouse.LastClickPosition}, current pos: {mouse.CurrentPosition}, diff: {mouse.DragDelta}, min: {Mouse.MIN_DRAG_PIXELS})");
					}
					
				}

				// luckily, controls still get mouse motion events as long as they are being held down
				// no matter where the mouse is
				if (mouse.State == MouseState.Dragging)
				{
					ClipsView.DragSelection(UIClip, (mouse.LastClickPosition, mouse.DragDelta));
					//GD.Print($"clips view: dragging");
				} 
				else
				{
					//GD.Print($"clips view: being hovered over");
				} 
				
			}
			

		} 
    }
}
