using EditSharpGUI.Scripts.App.Chrome.Platform;
using Godot;

namespace EditSharpGUI.Scripts.App.Chrome;

// gives a window's title bar to a drawn UITopBar: Windows keeps its frame, macOS extends into its title, Linux goes borderless
public static class WindowChrome
{
	// the bar's height and caption button width in its own units, and the scale it's drawn at: the system's, never the interface scale
	public static (float Height, float CaptionWidth, float Scale) Metrics(Window window) => OS.GetName() switch
	{
		"Windows" => WindowsChrome.Metrics(window),
		"macOS" => (28f, 48f, AppSettings.SystemScale(window) / window.ContentScaleFactor),
		_ => (36f, 48f, AppSettings.SystemScale(window) / window.ContentScaleFactor),
	};

	// where a window's content is on screen and how big, as the OS has it
	public static Rect2I ContentRect(Window window) =>
		new(OS.GetName() == "Windows" ? WindowsChrome.ContentPosition(window) : window.Position, window.Size);

	// puts a window's content at `rect`, allowing for how the OS's chrome skews the position Godot sets
	public static void Place(Window window, Rect2I rect)
	{
		int inset = OS.GetName() == "Windows" ? WindowsChrome.CaptionInset(window) : 0;
		window.Position = rect.Position + new Vector2I(0, inset);
		window.Size = rect.Size;
	}

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
					bar.LeadingInset = margins.X / AppSettings.SystemScale(window);
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
