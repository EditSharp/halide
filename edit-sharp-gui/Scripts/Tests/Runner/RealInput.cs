using Godot;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace EditSharpGUI.Tests;

// drives the real OS pointer (SetCursorPos/mouse_event), for windowed tests that want to exercise the
// actual input path end to end rather than an injected value. windows only; the app's own pointer hooks
// (DockDrag.Pointer, DragDrop.Pointer, OsPointer) default to the real OS reader, so a test using this and
// never overriding those hooks runs the production code path exactly as a hand on the mouse would
public static class RealInput
{
	// the control's centre, in real OS screen pixels
	public static Vector2I ScreenCenterOf(Control c) =>
		DisplayServer.WindowGetPosition(c.GetWindow().GetWindowId()) + (Vector2I)(c.GetViewport().GetScreenTransform() * c.GetGlobalRect().GetCenter()).Round();

	// a point in `window`'s viewport, in real OS screen pixels
	public static Vector2I ScreenPointIn(Window window, Vector2 viewportPoint) =>
		DisplayServer.WindowGetPosition(window.GetWindowId()) + (Vector2I)(window.GetViewport().GetScreenTransform() * viewportPoint).Round();

	public static Vector2I CursorPosition
	{
		get { GetCursorPos(out POINT p); return new Vector2I(p.x, p.y); }
		set => SetCursorPos(value.X, value.Y);
	}

	// several steps rather than one jump, so the OS and anything watching WM_MOUSEMOVE sees real intermediate positions
	public static async Task MoveTo(Vector2I to, int steps = 8)
	{
		Vector2I from = CursorPosition;
		for (int i = 1; i <= steps; i++)
		{
			CursorPosition = from + (to - from) * i / steps;
			await Frame();
		}
	}

	public static void MouseDown() => mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
	public static void MouseUp() => mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);

	public static void Click()
	{
		MouseDown();
		MouseUp();
	}

	static async Task Frame() => await ((SceneTree)Engine.GetMainLoop()).ToSignal((SceneTree)Engine.GetMainLoop(), SceneTree.SignalName.ProcessFrame);

	const uint MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4;

	[StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }

	[DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
	[DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
	[DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, nuint extra);
}
