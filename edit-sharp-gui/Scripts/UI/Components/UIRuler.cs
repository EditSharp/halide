using Godot;
using System;
using System.Diagnostics;

public partial class UIRuler : Control
{
	[Export] float markWidth;

	[ExportGroup("Controls")]

	[Export] BoxContainer marksContainer;

	[ExportGroup("Styles")]

	[Export] StyleBox markStyleBox;

	public void Update(double pixelsPerSecond, int framerate)
	{
		Debug.WriteLine("updating ruler");
		// clear out any old nodes
		foreach (var child in marksContainer.GetChildren()) child.QueueFree();

		// the size of gaps between each mark
		// accounting for the space the mark itself already takes up
		float gapWidth = (float)((pixelsPerSecond / framerate) - markWidth);
		// the size needed to place a mark and its gap
		float requiredWidth = markWidth + gapWidth;

		//place marks and gaps for as long as there is space to do so
		for (float width = Size.X; width > requiredWidth; width -= requiredWidth)
		{
			// add mark
            Panel mark = new()
            {
                CustomMinimumSize = new(
                    1f,
                    Size.Y / 2f
                )
            };
			mark.AddThemeStyleboxOverride("panel", markStyleBox);

            marksContainer.AddChild(mark);

			// add gap
			Control gap = new()
			{
				CustomMinimumSize = new(
					(float)((pixelsPerSecond / framerate) - mark.Size.X),
					0f
				)
			};

			marksContainer.AddChild(gap);
		}
	}
}
