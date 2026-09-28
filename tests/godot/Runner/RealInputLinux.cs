using Godot;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Halide.Tests;

// drives the real X11 pointer via xdotool (XTest, the same mechanism a real mouse driver injects
// through), for windowed tests on Linux that want the actual input path rather than an injected value.
// the app's pointer hooks (DockDrag.Pointer/Held, DragDrop.Pointer/Held, OsPointer) default to the real
// OS reader, so a test using this and never overriding those hooks runs the production code path
public static class RealInputLinux
{
	// the scale comes from ContentScaleFactor, not GetViewport().GetScreenTransform() -- the Windows
	// counterpart (RealInput.cs) had the same GetScreenTransform()-based formula and it was found to
	// disagree with how godot reads a real click's position back by a few percent, landing clicks
	// several pixels short. ContentScaleFactor is what a real click gets divided by on the way back
	// into viewport space, so it round-trips correctly; unverified here since no Linux box has run this yet
	public static Vector2I ScreenCenterOf(Control c) => ScreenPointIn(c.GetWindow(), c.GetGlobalRect().GetCenter());

	public static Vector2I ScreenPointIn(Window window, Vector2 viewportPoint) =>
		DisplayServer.WindowGetPosition(window.GetWindowId()) + (Vector2I)(viewportPoint * window.ContentScaleFactor).Round();

	public static async Task MoveTo(Vector2I to, int steps = 8)
	{
		Vector2I from = CursorPosition();
		for (int i = 1; i <= steps; i++)
		{
			Vector2I at = from + (to - from) * i / steps;
			Xdotool($"mousemove {at.X} {at.Y}");
			await Frame();
		}
	}

	public static void MouseDown() => Xdotool("mousedown 1");
	public static void MouseUp() => Xdotool("mouseup 1");

	public static void Click()
	{
		MouseDown();
		MouseUp();
	}

	static Vector2I CursorPosition()
	{
		string output = XdotoolOutput("getmouselocation");
		// "x:123 y:456 screen:0 window:789"
		string[] parts = output.Split(' ');
		int x = int.Parse(parts[0][2..]);
		int y = int.Parse(parts[1][2..]);
		return new Vector2I(x, y);
	}

	static async Task Frame() => await ((SceneTree)Engine.GetMainLoop()).ToSignal((SceneTree)Engine.GetMainLoop(), SceneTree.SignalName.ProcessFrame);

	static void Xdotool(string args)
	{
		using Process p = Process.Start(new ProcessStartInfo("xdotool", args) { UseShellExecute = false });
		p.WaitForExit();
	}

	static string XdotoolOutput(string args)
	{
		using Process p = Process.Start(new ProcessStartInfo("xdotool", args) { UseShellExecute = false, RedirectStandardOutput = true });
		string output = p.StandardOutput.ReadToEnd();
		p.WaitForExit();
		return output.Trim();
	}
}
