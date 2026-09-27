using Godot;

namespace Halide.Scripts.App.Chrome;

// a window's layout: the drawn top bar over its content; laid out in WindowFrame.tscn
[GlobalClass]
public partial class WindowFrame : VBoxContainer
{
	public const string ScenePath = "res://Scenes/App/WindowFrame.tscn";

	[Export] public UITopBar Bar;
	[Export] public Control Content;

	// holds the bar at its own scale, which on Windows is the monitor's, not the interface scale
	[Export] Control barHolder;

	// a frame holding `content` under a bar, ready to add to a window
	public static WindowFrame Wrap(Control content)
	{
		WindowFrame frame = GD.Load<PackedScene>(ScenePath).Instantiate<WindowFrame>();
		frame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		frame.Content.AddChild(content);
		return frame;
	}

	Window window;

	public override void _Ready()
	{
		window = GetWindow();
		barHolder.Resized += Fit;
		window.SizeChanged += Fit;
		window.FocusEntered += Activate;
		window.FocusExited += Activate;
		AppSettings.Changed += RefitLater;

		// takes the window's title bar once the frame is in it
		Callable.From(() =>
		{
			if (!IsInstanceValid(this)) return;
			WindowChrome.Attach(window, Bar);
			Fit();
		}).CallDeferred();
	}

	public override void _ExitTree()
	{
		AppSettings.Changed -= RefitLater;
		if (!IsInstanceValid(window)) return;
		window.SizeChanged -= Fit;
		window.FocusEntered -= Activate;
		window.FocusExited -= Activate;
	}

	void Activate() => Bar.SetActive(window.HasFocus());

	// the bar laid out in the chrome's units and drawn at the chrome's scale
	void Fit()
	{
		if (!IsInsideTree()) return;

		(float height, float captionWidth, float scale) = WindowChrome.Metrics(window);
		Bar.SetMetrics(height, captionWidth);
		Bar.Scale = new Vector2(scale, scale);
		Bar.Position = Vector2.Zero;
		Bar.Size = new Vector2(barHolder.Size.X / scale, height);
		barHolder.CustomMinimumSize = new Vector2(0, height * scale);
	}

	// the interface scale may have changed; the bar keeps its own, once the window has taken the new one
	void RefitLater() => Callable.From(() => { if (IsInstanceValid(this)) Fit(); }).CallDeferred();
}
