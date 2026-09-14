using Godot;
using System;

public partial class FitToChildren : Control
{
	[Export] bool Horizontal;
	[Export] bool Vertical;


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

		CustomMinimumSize = new(
			Horizontal ? width : CustomMinimumSize.X,
			Vertical ? height : CustomMinimumSize.Y
		);
	}
}
