using System;
using EditSharp.Components.Clips;
using Godot;
using static UIClipsView;

namespace EditSharpGUI.Scripts.UI.Components;

public class UIClipInputHandler
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
							GD.Print($"{Clip.Name}: double clicked");
						}
						else
						{
							if (mb.IsCommandOrControlPressed())
							{
								ClipsView.DeselectClip(UIClip);
								GD.Print($"{Clip.Name}: ctrl clicked");
							}
							else if (mb.ShiftPressed)
							{
								ClipsView.SelectClip(UIClip, SelectionMode.Inclusive);
								GD.Print($"{Clip.Name}: shift clicked");
							}
							else
							{
								ClipsView.SelectClip(UIClip, SelectionMode.ExclusiveIfUnselected);
								GD.Print($"{Clip.Name}: click started");
							}
						}
						
						mouse.State = MouseState.Clicking;
					}
					else
					{
						if (mouse.State == MouseState.Dragging)
						{
							ClipsView.FinishDrag(UIClip, (mouse.LastClickPosition, mouse.DragDelta));
							GD.Print($"{Clip.Name}: drag finished");
						}
						else
						{
							if (mb.IsCommandOrControlPressed())
							{
								GD.Print($"{Clip.Name}: ctrl click finished");
							}
							else if (mb.ShiftPressed)
							{
								GD.Print($"{Clip.Name}: shift click finished");
							}
							else
							{
								ClipsView.SelectClip(UIClip, SelectionMode.Exclusive);
								GD.Print($"{Clip.Name}: click finished");
							}
						}
						
						mouse.State = MouseState.Released;
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
						GD.Print($"{Clip.Name}: drag started (original pos: {mouse.LastClickPosition}, current pos: {mouse.CurrentPosition}, diff: {mouse.DragDelta}, min: {Mouse.MIN_DRAG_PIXELS})");
					}
					
				}

				// luckily, controls still get mouse motion events as long as they are being held down
				// no matter where the mouse is
				if (mouse.State == MouseState.Dragging)
				{
					ClipsView.DragSelection(UIClip, (mouse.LastClickPosition, mouse.DragDelta));
					GD.Print($"{Clip.Name}: dragging");
				} 
				else
				{
					//GD.Print($"{Clip.Name}: being hovered over");
				} 
				
			}
			

		} 
    }
}
