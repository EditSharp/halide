using Godot;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// a small icon button: keyframe arrows and diamond, the reset arrow, the
// chain link. the icons are images assigned in the scene - one for the
// plain look, and for the diamond and the link the looks they take when
// something is keyed or linked. the button's theme variation switches to
// the active one for those states, so their tint is the theme's. until an
// icon is assigned the shape is drawn by hand, so nothing goes blank
public partial class InspectorGlyph : Button
{
	public enum Kind { Prev, Diamond, Next, Reset, ResetTrack, Link }

	[Export] public Kind Shape { get; set; }

	// the image for the plain state: an arrow, the reset, the hollow
	// diamond of an unkeyed property, the open link
	[ExportGroup("Icons")]
	[Export] public Texture2D IconImage { get; set; }

	// the diamond when the property is animated but not keyed here; the
	// link when it is on
	[Export] public Texture2D IconActive { get; set; }

	// the diamond when there is a keyframe under the playhead
	[Export] public Texture2D IconKeyed { get; set; }

	// the variation worn while active - a keyed diamond, a link that is on
	[Export] public string ActiveVariation { get; set; } = "InspectorKeyButtonActive";

	string baseVariation;
	KeyState state;

	// the diamond only: how the property is keyed right now
	public KeyState State
	{
		get => state;
		set { state = value; Apply(); }
	}

	public override void _Ready()
	{
		baseVariation = ThemeTypeVariation;
		IconAlignment = HorizontalAlignment.Center;
		ExpandIcon = false;
		Apply();
	}

	public override void _Toggled(bool toggledOn) => Apply();

	bool Active => Shape switch
	{
		Kind.Diamond => state is KeyState.Animated or KeyState.Keyed,
		Kind.Link => ButtonPressed,
		_ => false
	};

	void Apply()
	{
		baseVariation ??= ThemeTypeVariation;

		string variation = Active && !string.IsNullOrEmpty(ActiveVariation) ? ActiveVariation : baseVariation;
		if (ThemeTypeVariation != variation) ThemeTypeVariation = variation;

		Icon = Shape switch
		{
			Kind.Diamond => state switch
			{
				KeyState.Keyed => IconKeyed ?? IconActive ?? IconImage,
				KeyState.Animated => IconActive ?? IconImage,
				_ => IconImage
			},
			Kind.Link => ButtonPressed ? IconActive ?? IconImage : IconImage,
			_ => IconImage
		};

		QueueRedraw();
	}

	// ---- the fallback, for a glyph with no icon yet ----

	public override void _Draw()
	{
		if (Icon is not null) return;

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

				if (state == KeyState.Keyed) DrawColoredPolygon(points, accent);
				else DrawPolyline([.. points, points[0]], state == KeyState.Animated ? accent : dim, 1.5f, true);

				break;
			}

			case Kind.Prev:
				DrawColoredPolygon([c + new Vector2(2.5f, -4f), c + new Vector2(2.5f, 4f), c + new Vector2(-3f, 0f)], arrow);
				break;

			case Kind.Next:
				DrawColoredPolygon([c + new Vector2(-2.5f, -4f), c + new Vector2(-2.5f, 4f), c + new Vector2(3f, 0f)], arrow);
				break;

			case Kind.Reset:
			{
				const float r = 4.5f;
				DrawArc(c, r, Mathf.DegToRad(-60f), Mathf.DegToRad(210f), 20, reset, 1.5f, true);
				Vector2 tip = c + new Vector2(Mathf.Cos(Mathf.DegToRad(-60f)), Mathf.Sin(Mathf.DegToRad(-60f))) * r;
				DrawColoredPolygon([tip + new Vector2(-3f, -2.5f), tip + new Vector2(2f, -2f), tip + new Vector2(0.5f, 3f)], reset);
				break;
			}

			case Kind.ResetTrack:
			{
				const float r = 5f;
				Vector2[] points = [c + new Vector2(0f, -r), c + new Vector2(r, 0f), c + new Vector2(0f, r), c + new Vector2(-r, 0f)];
				DrawPolyline([.. points, points[0]], reset, 1.5f, true);
				DrawLine(c + new Vector2(-5f, 5f), c + new Vector2(5f, -5f), reset, 1.5f, true);
				break;
			}

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
}
