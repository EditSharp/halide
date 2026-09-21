using EditSharpGUI.Scripts.UI.Theming;
using Godot;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// a section's header button: the fold arrow as an icon assigned in the
// scene - one image expanded, one collapsed - shown through the button's
// own icon, and an accent strip, a ColorRect in the scene down its left,
// coloured here and shown only when the section has an accent. a header
// given no images gets arrows rasterised once and shared
[Tool]
public partial class SectionHeader : Button
{
	[ExportGroup("Icons")]
	[Export] public Texture2D IconExpanded { get; set; }
	[Export] public Texture2D IconCollapsed { get; set; }

	[ExportGroup("Parts")]
	[Export] ColorRect accentStrip;

	public string Title
	{
		get => Text;
		set => Text = value;
	}

	Color? accent;

	public Color? Accent
	{
		get => accent;
		set
		{
			accent = value;
			if (accentStrip is null) return;
			accentStrip.Visible = value.HasValue;
			if (value is Color colour) accentStrip.Color = colour;
		}
	}

	public override void _Ready()
	{
		IconAlignment = HorizontalAlignment.Left;
		ExpandIcon = false;
		Accent = accent;
		Apply();
	}

	public override void _Toggled(bool toggledOn) => Apply();

	void Apply()
	{
		Icon = ButtonPressed
			? IconExpanded ?? IconRaster.Get(IconRaster.Shape.ArrowDown, 14)
			: IconCollapsed ?? IconRaster.Get(IconRaster.Shape.ArrowRight, 14);
	}
}
