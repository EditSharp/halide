using Godot;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// a section's header button: the fold arrow as an icon assigned in the
// scene - one image expanded, one collapsed - and the accent strip drawn
// down its left. until the icons are assigned the arrow is drawn by hand
public partial class SectionHeader : Button
{
	[ExportGroup("Icons")]
	[Export] public Texture2D IconExpanded { get; set; }
	[Export] public Texture2D IconCollapsed { get; set; }

	public Color? Accent;

	public override void _Ready()
	{
		IconAlignment = HorizontalAlignment.Left;
		ExpandIcon = false;
		Apply();
	}

	public override void _Toggled(bool toggledOn) => Apply();

	void Apply()
	{
		Icon = ButtonPressed ? IconExpanded ?? IconCollapsed : IconCollapsed ?? IconExpanded;
		QueueRedraw();
	}

	public override void _Draw()
	{
		float h = Size.Y;

		if (Accent is Color accent) DrawRect(new Rect2(0f, 0f, 3f, h), accent);

		if (Icon is not null) return;

		Color arrow = GetThemeColor("arrow", "Inspector");
		Vector2 c = new(9f, h / 2f);

		Vector2[] points = ButtonPressed
			? [c + new Vector2(-4f, -2f), c + new Vector2(4f, -2f), c + new Vector2(0f, 3f)]
			: [c + new Vector2(-2f, -4f), c + new Vector2(3f, 0f), c + new Vector2(-2f, 4f)];

		DrawColoredPolygon(points, arrow);
	}
}
