using EditSharpGUI.Scripts.App.Chrome.Platform;
using Godot;

namespace EditSharpGUI.Scripts.App.Chrome;

// gives a window's title bar to a drawn UITopBar: Windows keeps its frame, macOS extends into its title, Linux goes borderless
public static class WindowChrome
{
	// the bar's height and caption button width in its own units, and the scale it's drawn at
	public static (float Height, float CaptionWidth, float Scale) Metrics(Window window) => OS.GetName() switch
	{
		"Windows" => WindowsChrome.Metrics(window),
		_ => (36f, 48f, 1f),
	};

	public static void Attach(Window window, UITopBar bar)
	{
		switch (OS.GetName())
		{
			case "Windows":
				bar.ShowsCaptionButtons = true;
				bar.UseSystemGlyphs = true;
				WindowsChrome.Attach(window, bar);
				break;

			case "macOS":
				// the real traffic lights stay; the bar leaves room for them
				bar.ShowsCaptionButtons = false;
				window.ExtendToTitle = true;
				DragByBar(window, bar);
				Callable.From(() =>
				{
					if (!GodotObject.IsInstanceValid(window)) return;
					Vector3I margins = DisplayServer.WindowGetSafeTitleMargins(window.GetWindowId());
					bar.LeadingInset = margins.X / window.ContentScaleFactor;
				}).CallDeferred();
				break;

			default:
				bar.ShowsCaptionButtons = true;
				window.Borderless = true;
				DragByBar(window, bar);
				ResizeGrips.Cover(window);
				break;
		}
	}

	// the bar's empty space moves the window, and a double click maximizes it
	static void DragByBar(Window window, UITopBar bar)
	{
		bar.GuiInput += e =>
		{
			if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press) return;
			if (bar.RegionAt(bar.GetGlobalMousePosition()) != UITopBar.Region.Caption) return;

			if (press.DoubleClick) bar.ToggleMaximized();
			else DisplayServer.WindowStartDrag(window.GetWindowId());
			bar.AcceptEvent();
		};
	}
}
