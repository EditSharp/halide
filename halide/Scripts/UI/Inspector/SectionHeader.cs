using Halide.Scripts.UI.Theming;
using Godot;

namespace Halide.Scripts.UI.Inspecting;

// a section's header button: the fold arrow as an icon assigned in the
// scene - one image expanded, one collapsed - shown through the button's
// own icon, and an accent strip, a ColorRect in the scene down its left,
// coloured here and shown only when the section has an accent. a header
// given no images gets arrows rasterised once and shared
[Tool]
public partial class SectionHeader : Button
{
	[ExportGroup("Icons")]
	[Export] public TextureRect ExpandedIcon { get; set; }
	[Export] public Label titleLabel { get; set; }

	[ExportGroup("Parts")]
	[Export] ColorRect accentStrip;

	// the title label when the scene has one, else the button's own text
	public string Title
	{
		get => (titleLabel is not null ? titleLabel.Text : Text) ?? string.Empty;
		set
		{
			if (titleLabel is not null) titleLabel.Text = value;
			else Text = value;
		}
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
		Accent = accent;
		Apply();
	}

	public override void _Toggled(bool toggledOn) => Apply();

	void Apply()
	{
		if (ExpandedIcon is null) return;
		ExpandedIcon.RotationDegrees = ButtonPressed ? 0 : 270;
	}
}
