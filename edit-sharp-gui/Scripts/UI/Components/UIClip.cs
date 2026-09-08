using Godot;
using System;
using EditSharp;
using EditSharp.Components.Clips;

public partial class UIClip : Control
{
	[ExportGroup("Controls")]

	[Export] Control content;
	[Export] Control gap;
	[Export] Label clipName;
	[Export] Panel thumbnail;

	[ExportGroup("Styles")]

	[Export] StyleBox videoStyleBox;
	[Export] StyleBox audioStyleBox;


	public Clip clip;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// update gui based on provided clip
		clipName.Text = clip.Name;

		if (clip is VideoClip) content.AddThemeStyleboxOverride("panel", videoStyleBox);
		else content.AddThemeStyleboxOverride("panel", audioStyleBox);

		UpdateThumbnail();
	}

	public void SetLength(float length, float gap = 0)
	{
		content.Size = new(length, content.Size.Y);

		UpdateThumbnail();
	}

	void UpdateThumbnail()
	{
		
	}
}
