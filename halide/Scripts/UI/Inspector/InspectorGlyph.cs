using Halide.Scripts.UI.Theming;
using Godot;

namespace Halide.Scripts.UI.Inspecting;

// a small icon button: keyframe arrows and diamond, the reset arrow, the
// chain link. the icons are images assigned in the scene - one for the
// plain look, and for the diamond and the link the looks they take when
// something is keyed or linked - shown through the button's own icon. a
// glyph given no image gets one rasterised once and shared. the button's
// theme variation switches to the active one for active states, so their
// tint is the theme's
[Tool]
public partial class InspectorGlyph : Button
{
	public enum Kind { Prev, Diamond, Next, Reset, ResetTrack, Link }

	[Export] public Kind Shape { get; set; }

	[ExportGroup("Icons")]
	[Export] public Texture2D IconImage { get; set; }
	[Export] public Texture2D IconActive { get; set; }
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

		Texture2D icon = Shape switch
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

		Icon = icon ?? Fallback();
	}

	Texture2D Fallback() => IconRaster.Get(Shape switch
	{
		Kind.Diamond => state == KeyState.Keyed ? IconRaster.Shape.Diamond : IconRaster.Shape.DiamondOutline,
		Kind.Prev => IconRaster.Shape.ArrowLeft,
		Kind.Next => IconRaster.Shape.ArrowRight,
		Kind.Reset => IconRaster.Shape.Reset,
		Kind.ResetTrack => IconRaster.Shape.ResetTrack,
		Kind.Link => ButtonPressed ? IconRaster.Shape.LinkOn : IconRaster.Shape.LinkOff,
		_ => IconRaster.Shape.Dot
	});
}
