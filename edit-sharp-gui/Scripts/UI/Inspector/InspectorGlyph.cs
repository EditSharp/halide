using Godot;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// a button that draws its own picture, so the inspector needs no icon
// textures: keyframe arrows and diamond, the reset arrow, a chain link.
// which picture is set in the scene; the diamond's state at runtime
public partial class InspectorGlyph : Button
{
	public enum Kind { Prev, Diamond, Next, Reset, ResetTrack, Link }

	[Export] public Kind Shape { get; set; }

	// the diamond only: how the property is keyed right now
	public KeyState State { get; set; }

	public override void _Draw()
	{
		Vector2 c = Size / 2f;
		Color accent = GetThemeColor("key", "Inspector");
		Color dim = GetThemeColor("key_dim", "Inspector");
		Color arrow = Disabled ? dim : GetThemeColor("arrow", "Inspector");
		Color reset = Disabled ? dim : GetThemeColor("reset", "Inspector");

		switch (Shape)
		{
			case Kind.Diamond:
			{
				const float r = 5f;
				Vector2[] points = [c + new Vector2(0f, -r), c + new Vector2(r, 0f), c + new Vector2(0f, r), c + new Vector2(-r, 0f)];

				if (State == KeyState.Keyed) DrawColoredPolygon(points, accent);
				else DrawPolyline([.. points, points[0]], State == KeyState.Animated ? accent : dim, 1.5f, true);

				break;
			}

			case Kind.Prev:
				DrawColoredPolygon([c + new Vector2(2.5f, -4f), c + new Vector2(2.5f, 4f), c + new Vector2(-3f, 0f)], arrow);
				break;

			case Kind.Next:
				DrawColoredPolygon([c + new Vector2(-2.5f, -4f), c + new Vector2(-2.5f, 4f), c + new Vector2(3f, 0f)], arrow);
				break;

			// an arrow curling back on itself
			case Kind.Reset:
			{
				const float r = 4.5f;
				DrawArc(c, r, Mathf.DegToRad(-60f), Mathf.DegToRad(210f), 20, reset, 1.5f, true);
				Vector2 tip = c + new Vector2(Mathf.Cos(Mathf.DegToRad(-60f)), Mathf.Sin(Mathf.DegToRad(-60f))) * r;
				DrawColoredPolygon([tip + new Vector2(-3f, -2.5f), tip + new Vector2(2f, -2f), tip + new Vector2(0.5f, 3f)], reset);
				break;
			}

			// a hollow diamond struck through
			case Kind.ResetTrack:
			{
				const float r = 5f;
				Vector2[] points = [c + new Vector2(0f, -r), c + new Vector2(r, 0f), c + new Vector2(0f, r), c + new Vector2(-r, 0f)];
				DrawPolyline([.. points, points[0]], reset, 1.5f, true);
				DrawLine(c + new Vector2(-5f, 5f), c + new Vector2(5f, -5f), reset, 1.5f, true);
				break;
			}

			// two rings, joined when pressed and apart when not
			case Kind.Link:
			{
				Color colour = ButtonPressed ? accent : dim;
				float gap = ButtonPressed ? 2.5f : 4f;
				DrawArc(c + new Vector2(-gap, 0f), 3.5f, 0f, Mathf.Tau, 16, colour, 1.5f, true);
				DrawArc(c + new Vector2(gap, 0f), 3.5f, 0f, Mathf.Tau, 16, colour, 1.5f, true);
				break;
			}
		}
	}

	public override void _Toggled(bool toggledOn) => QueueRedraw();
}
