using Godot;
using System;

public partial class FitToChildren : Control
{
	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		float furthestPoint = 0f;
		foreach (var child in GetChildren())
		{
			if (child is Control c)
			{
				furthestPoint = c.GetRect().End.X > furthestPoint ? c.GetRect().End.X : furthestPoint;
			}
		}

		CustomMinimumSize = new(
			furthestPoint,
			CustomMinimumSize.Y
		);
	}
}
