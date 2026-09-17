using Godot;
using System;

public partial class FitToChildren : Control
{
	[Export] bool Horizontal;
	[Export] bool Vertical;

	// while set, this may grow to fit its children but never shrink.
	//
	// something being dragged moves its own bounds every frame, so letting the
	// fit shrink underneath it closes a loop: the content gets smaller, the
	// scroll container clamps the scroll down to the smaller maximum, whatever
	// is being dragged is positioned from that scroll so it moves further in,
	// and the content gets smaller again. that runs away at a constant rate
	// until the scroll bottoms out. clearing this once the drag is over lets the
	// size settle in a single step instead
	public bool GrowOnly;

	public override void _Process(double delta)
	{
		float width = 0f;
		float height = 0f;
		foreach (var child in GetChildren())
		{
			if (child is Control c)
			{
				width = c.GetRect().End.X > width ? c.GetRect().End.X : width;
				height = c.GetRect().End.Y > height ? c.GetRect().End.Y : height;
			}
		}

		if (GrowOnly)
		{
			width = Mathf.Max(width, CustomMinimumSize.X);
			height = Mathf.Max(height, CustomMinimumSize.Y);
		}

		CustomMinimumSize = new(
			Horizontal ? width : CustomMinimumSize.X,
			Vertical ? height : CustomMinimumSize.Y
		);
	}
}
