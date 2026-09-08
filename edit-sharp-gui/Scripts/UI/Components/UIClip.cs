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

	public void SetLength(float length, float gapSize = 0)
	{
		content.CustomMinimumSize = new(length, content.CustomMinimumSize.Y);

		gap.CustomMinimumSize = new(gapSize, gap.CustomMinimumSize.Y);

		CustomMinimumSize = new(
			content.Size.X + gap.Size.X,
			CustomMinimumSize.Y
		);

		UpdateThumbnail();
	}

	void UpdateThumbnail()
	{
		
	}
}
