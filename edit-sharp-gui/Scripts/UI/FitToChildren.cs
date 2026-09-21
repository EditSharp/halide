using EditSharpGUI.Scripts.UI;
using Godot;
using System;

public partial class FitToChildren : Control
{
	[Export] bool Horizontal;
	[Export] bool Vertical;

	// room kept past the last child on each axis. the clips view needs it so
	// the drag handles hung off the end of the last clip, which sit outside its
	// rect, are still inside the scrollable area and can actually be reached
	[Export] public Vector2 Padding;

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
			// overlays sit on the content without being part of it
			if (child is Control c && child is not IContentOverlay)
			{
				width = c.GetRect().End.X > width ? c.GetRect().End.X : width;
				height = c.GetRect().End.Y > height ? c.GetRect().End.Y : height;
			}
		}

		width += Padding.X;
		height += Padding.Y;

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
