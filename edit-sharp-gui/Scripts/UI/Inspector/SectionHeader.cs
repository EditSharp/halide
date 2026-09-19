using Godot;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// a section's header button, with the fold arrow and the accent strip
// drawn over whatever the theme gives the button
public partial class SectionHeader : Button
{
	public Color? Accent;

	public override void _Draw()
	{
		Color arrow = GetThemeColor("arrow", "Inspector");
		float h = Size.Y;
		Vector2 c = new(9f, h / 2f);

		Vector2[] points = ButtonPressed
			? [c + new Vector2(-4f, -2f), c + new Vector2(4f, -2f), c + new Vector2(0f, 3f)]
			: [c + new Vector2(-2f, -4f), c + new Vector2(3f, 0f), c + new Vector2(-2f, 4f)];

		DrawColoredPolygon(points, arrow);

		if (Accent is Color accent) DrawRect(new Rect2(0f, 0f, 3f, h), accent);
	}
}
