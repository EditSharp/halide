using Godot;
using System;
using EditSharp;
using EditSharp.Components.Clips;

public partial class UIClip : Control
{
	[ExportGroup("Controls")]

	[Export] Control content;
	[Export] Label clipName;
	[Export] Panel thumbnail;

	[ExportGroup("Styles")]

	[Export] StyleBox videoStyleBox;
	[Export] StyleBox audioStyleBox;


	public Clip Clip;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// update gui based on provided clip
		clipName.Text = Clip.Name;

		if (Clip is VideoClip) content.AddThemeStyleboxOverride("panel", videoStyleBox);
		else content.AddThemeStyleboxOverride("panel", audioStyleBox);

		UpdateThumbnail();
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

    public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb)
		{
			if (mb.ButtonIndex == MouseButton.Left && mb.Pressed)
			{
				GD.Print($"{Clip.Name}: I've been clicked D:");
			}
		}
	}

}
