using Halide.Scripts.UI.Theming;
using Godot;

namespace Halide.Scripts.App.Chrome;

// resize edges and an outline over a borderless window, sized in screen pixels; laid out in ResizeGrips.tscn
[GlobalClass]
public partial class ResizeGrips : Control
{
	public const string ScenePath = "res://Scenes/App/ResizeGrips.tscn";

	// the frame edge libadwaita draws in each OS mode
	static readonly Color DarkOutline = new(1, 1, 1, 0.1f);
	static readonly Color LightOutline = new(0, 0, 0, 0.23f);

	[Export] Panel outline;

	Window window;

	// grips over the whole of `window`, drawn above everything already in it
	public static ResizeGrips Cover(Window window)
	{
		ResizeGrips grips = GD.Load<PackedScene>(ScenePath).Instantiate<ResizeGrips>();
		window.AddChild(grips);
		return grips;
	}

	public override void _Ready()
	{
		window = GetWindow();
		window.SizeChanged += Fit;
		AppSettings.Changed += FitLater;
		EditSharpTheme.SystemModeChanged += Recolour;
		Recolour();
		Fit();
	}

	public override void _ExitTree()
	{
		AppSettings.Changed -= FitLater;
		EditSharpTheme.SystemModeChanged -= Recolour;
		if (IsInstanceValid(window)) window.SizeChanged -= Fit;
	}

	// counter-scaled so the grips keep their screen size, and gone while the window fills the screen
	void Fit()
	{
		if (!IsInsideTree()) return;

		float scale = window.ContentScaleFactor;
		Scale = new Vector2(1 / scale, 1 / scale);
		Position = Vector2.Zero;
		Size = window.Size;
		Visible = window.Mode is Window.ModeEnum.Windowed or Window.ModeEnum.Minimized;
	}

	void FitLater() => Callable.From(() => { if (IsInstanceValid(this)) Fit(); }).CallDeferred();

	void Recolour()
	{
		StyleBox box = outline.GetThemeStylebox("panel");
		if (box is StyleBoxFlat flat) flat.BorderColor = DisplayServer.IsDarkMode() ? DarkOutline : LightOutline;
	}
}
