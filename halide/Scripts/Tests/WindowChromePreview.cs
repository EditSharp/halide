using Halide.Scripts.App.Platform;
using Halide.Scripts.UI.Dialogs;
using Halide.Scripts.UI.Theming;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

// a project window with its drawn top bar, held open while Tools/window_chrome_preview.py captures it;
// reports the window's rect on screen, frame included, in the OS's own pixels
//   -- [--out=rect.txt] [--hold=3000] [--palette=res://Themes/Light.tres] [--hover=close|maximize]
public partial class WindowChromePreview : Node
{
	public override async void _Ready()
	{
		string[] args = OS.GetCmdlineUserArgs();
		string Arg(string name) => args.FirstOrDefault(a => a.StartsWith($"--{name}="))?[$"--{name}=".Length..];
		string outPath = Arg("out");
		int hold = int.TryParse(Arg("hold"), out int h) ? h : 3000;

		GetTree().Root.GuiEmbedSubwindows = false;
		HostWindow.Hide(GetTree().Root);
		string folder = Path.Combine(Path.GetTempPath(), $"editsharp-chrome-{Guid.NewGuid():N}");
		AppSettings.UseFile(Path.Combine(folder, "settings.json"));
		AppSettings.Current.OnLastWindowClosed = LastWindowAction.StayInTray;
		RecentProjects.UseFile(Path.Combine(folder, "projects.json"));
		EditSharpTheme.SettingsFile = Path.Combine(folder, "theme.json");
		Dialogs.Answering = d => d.Buttons.FirstOrDefault(b => b.Role == DialogButtonRole.Destructive)?.Id ?? d.Cancel?.Id;
		if (Arg("palette") is string palette && ThemeDB.GetProjectTheme() is EditSharpTheme theme) theme.ChoosePalette(palette);
		await Frames(3);

		ProjectWindow window = ProjectManager.Singleton.CreateProject(folder, "Chrome Preview", new EditSharp.Rendering.RenderSettings());
		// fits the screen, even a small CI one, and sits in its middle
		Rect2I usable = DisplayServer.ScreenGetUsableRect(window.CurrentScreen);
		window.Size = new Vector2I(Math.Min(1200, usable.Size.X - 96), Math.Min(720, usable.Size.Y - 96));
		window.Position = usable.Position + (usable.Size - window.Size) / 2;

		// held in front for the capture, whatever else is open
		window.AlwaysOnTop = true;
		window.GrabFocus();
		await Wait(1.5);

		// the pointer over a caption button, to see its hover look
		if (Arg("hover") is string hover)
		{
			Button button = FindButtons(window).FirstOrDefault(b => hover == "close" ? b.TooltipText == "Close" : b.TooltipText is "Maximize" or "Restore");
			if (button is not null)
			{
				Vector2 centre = button.GetGlobalTransform() * (button.Size / 2f);
				window.WarpMouse(centre);
				window.PushInput(new InputEventMouseMotion { Position = centre, GlobalPosition = centre });
				await Wait(0.5);
			}
		}

		// what the window itself drew, beside the screen capture
		if (Arg("selfshot") is string selfshot) window.GetTexture().GetImage().SavePng(selfshot);

		string report = Rect(window);
		GD.Print($"CHROME rect {report}");
		if (outPath is not null) File.WriteAllText(outPath, report);

		await Wait(hold / 1000.0);
		foreach (ProjectWindow w in ProjectManager.Singleton.OpenProjects.ToArray()) await w.CloseAsync();
		try { Directory.Delete(folder, true); } catch (IOException) { }
		GetTree().Quit();
	}

	static System.Collections.Generic.IEnumerable<Button> FindButtons(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child is Button b) yield return b;
			foreach (Button below in FindButtons(child)) yield return below;
		}
	}

	// the window as it appears on screen, with a margin for its shadow
	static string Rect(Window window)
	{
		const int margin = 24;

		if (OS.GetName() == "Windows")
		{
			nint hwnd = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, window.GetWindowId());
			DwmGetWindowAttribute(hwnd, 9, out RECT r, Marshal.SizeOf<RECT>());
			// the handle last, for a PrintWindow capture
			return $"{r.left - margin} {r.top - margin} {r.right - r.left + 2 * margin} {r.bottom - r.top + 2 * margin} {hwnd}";
		}

		Vector2I position = DisplayServer.WindowGetPositionWithDecorations(window.GetWindowId());
		Vector2I size = DisplayServer.WindowGetSizeWithDecorations(window.GetWindowId());

		// macOS captures in points
		float scale = OS.GetName() == "macOS" ? DisplayServer.ScreenGetScale(window.CurrentScreen) : 1f;
		int m = OS.GetName() == "macOS" ? margin : 0;
		return $"{(int)(position.X / scale) - m} {(int)(position.Y / scale) - m} {(int)(size.X / scale) + 2 * m} {(int)(size.Y / scale) + 2 * m}";
	}

	async System.Threading.Tasks.Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

	async System.Threading.Tasks.Task Frames(int count)
	{
		for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	[StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
	[DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out RECT value, int size);
}
