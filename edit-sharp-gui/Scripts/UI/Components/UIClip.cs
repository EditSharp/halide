using Godot;
using System;
using EditSharp;
using EditSharp.Components.Clips;
using System.Net.Sockets;

public partial class UIClip : PanelContainer
{
	[ExportGroup("Controls")]

	[Export] Control content;
	[Export] Label clipName;
	[Export] Panel thumbnail;
	[Export] Panel outline;

	[ExportGroup("Styles")]

	[Export] StyleBox outlinedStyleBox;
	[Export] StyleBox videoStyleBox;
	[Export] StyleBox audioStyleBox;

	public Clip Clip;

	public UIChannelClipsView ClipsView;

	// whether this clip is selected
	// updated by timeline
	public bool Selected = false;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// update gui based on provided clip
		clipName.Text = Clip.Name;

		if (Clip is VideoClip) content.AddThemeStyleboxOverride("panel", videoStyleBox);
		else content.AddThemeStyleboxOverride("panel", audioStyleBox);

		UpdateThumbnail();
	}

	public void SetOutlined(bool outlined)
	{
		if (outlined) outline.AddThemeStyleboxOverride("panel", outlinedStyleBox);
		else outline.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
	}

	public void SetTransparency(float alpha)
	{
		Color transparency = Modulate;
		transparency.A = alpha;
		Modulate = transparency;
	}

	public void SetLength(float length)
	{
		content.CustomMinimumSize = new(length, content.CustomMinimumSize.Y);

		CustomMinimumSize = new(
			content.Size.X,
			CustomMinimumSize.Y
		);

		UpdateThumbnail();
	}

	void UpdateThumbnail()
	{
		
	}

	Mouse mouse = new();
    public override void _GuiInput(InputEvent @event)
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
								ClipsView.UITimeline.DeselectClip(this);
								GD.Print($"{Clip.Name}: ctrl clicked");
							}
							else if (mb.ShiftPressed)
							{
								ClipsView.UITimeline.SelectClip(this, UITimeline.SelectionMode.Inclusive);
								GD.Print($"{Clip.Name}: shift clicked");
							}
							else
							{
								ClipsView.UITimeline.SelectClip(this, UITimeline.SelectionMode.ExclusiveIfUnselected);
								GD.Print($"{Clip.Name}: click started");
							}
						}
						
						mouse.State = MouseState.Clicking;
					}
					else
					{
						if (mouse.State == MouseState.Dragging)
						{
							ClipsView.UITimeline.FinishDrag(mouse.DragDelta);
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
								ClipsView.UITimeline.SelectClip(this, UITimeline.SelectionMode.Exclusive);
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
						ClipsView.UITimeline.DragSelection(mouse.DragDelta);
						GD.Print($"{Clip.Name}: drag started (original pos: {mouse.LastClickPosition}, current pos: {mouse.CurrentPosition}, diff: {mouse.DragDelta}, min: {Mouse.MIN_DRAG_PIXELS})");
					}
					
				}

				// luckily, controls still get mouse motion events as long as they are being held down
				// no matter where the mouse is
				if (mouse.State == MouseState.Dragging)
				{
					ClipsView.UITimeline.DragSelection(mouse.DragDelta);
					GD.Print($"{Clip.Name}: dragging");
				} 
				else
				{
					GD.Print($"{Clip.Name}: being hovered over");
				} 
				
			}
			

		}
	}

	class Mouse
	{
		public MouseState State = MouseState.Released;

		public Vector2 CurrentPosition, LastClickPosition;

		public const int MIN_DRAG_PIXELS = 2;
		public Vector2 DragDelta => LastClickPosition - CurrentPosition;
		public bool IsDragging => Mathf.Abs(DragDelta.X) > MIN_DRAG_PIXELS || Mathf.Abs(DragDelta.Y) > MIN_DRAG_PIXELS;
	}
	
	enum MouseState
	{
		Released,
		Clicking,
		Dragging
	}
}
