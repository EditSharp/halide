using Godot;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace EditSharpGUI.Tests;

// drives the real OS pointer via SendInput, for windowed tests that want to exercise the actual input
// path end to end rather than an injected value. windows only; the app's own pointer hooks (DockDrag.Pointer,
// DragDrop.Pointer, OsPointer) default to the real OS reader, so a test using this and never overriding
// those hooks runs the production code path exactly as a hand on the mouse would.
// SendInput, not the legacy SetCursorPos/mouse_event pair: over an unattended RDP session (nobody moving
// the client's own mouse), a position set with SetCursorPos doesn't reliably stick -- it can silently snap
// back to (0, 0) the moment the RDP virtual input channel next syncs. SendInput goes through the same
// injection pipeline real hardware and RDP's own redirected input use, and does stick.
public static class RealInput
{
	// the control's centre, in real win32 screen pixels
	public static Vector2I ScreenCenterOf(Control c) => ScreenPointIn(c.GetWindow(), c.GetGlobalRect().GetCenter());

	// a point in `window`'s viewport, in real win32 screen pixels. godot's screen space starts at the top-left
	// of all screens together; win32's at the primary's, so the window's real rect (GetWindowRect) against
	// godot's reported position (WindowGetPosition) gives the offset between the two -- the same fix the app's
	// own native menu handler needed (see WindowsHandler.ToWin32Offset): using the current mouse position as
	// the reference instead is unreliable, since a test moves the mouse itself and godot's cached position can
	// be stale for a window it hasn't seen a real motion event over yet
	public static Vector2I ScreenPointIn(Window window, Vector2 viewportPoint)
	{
		Vector2I godotPoint = DisplayServer.WindowGetPosition(window.GetWindowId()) + (Vector2I)(window.GetViewport().GetScreenTransform() * viewportPoint).Round();
		return godotPoint + WindowOffset(window);
	}

	// a window's own top-left, in real win32 screen pixels -- for arithmetic outside any Control's rect (e.g. a point past the window's edge)
	public static Vector2I ScreenPositionOf(Window window) => DisplayServer.WindowGetPosition(window.GetWindowId()) + WindowOffset(window);

	static Vector2I WindowOffset(Window window)
	{
		nint hwnd = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, window.GetWindowId());
		if (hwnd == 0 || !GetWindowRect(hwnd, out RECT rect)) return Vector2I.Zero;
		return new Vector2I(rect.left, rect.top) - DisplayServer.WindowGetPosition(window.GetWindowId());
	}

	public static Vector2I CursorPosition
	{
		get { GetCursorPos(out POINT p); return new Vector2I(p.x, p.y); }
		set => MoveAbsolute(value);
	}

	// several steps rather than one jump, so the OS and anything watching WM_MOUSEMOVE sees real intermediate positions
	public static async Task MoveTo(Vector2I to, int steps = 8)
	{
		Vector2I from = CursorPosition;
		for (int i = 1; i <= steps; i++)
		{
			MoveAbsolute(from + (to - from) * i / steps);
			await Frame();
		}
	}

	public static void MouseDown() => SendMouse(MOUSEEVENTF_LEFTDOWN);
	public static void MouseUp() => SendMouse(MOUSEEVENTF_LEFTUP);

	public static void Click()
	{
		MouseDown();
		MouseUp();
	}

	// SendInput's absolute coordinates are normalized 0-65535 across the virtual desktop (every monitor combined)
	static void MoveAbsolute(Vector2I screen)
	{
		int vLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
		int vTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
		int vWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
		int vHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);
		if (vWidth <= 0 || vHeight <= 0) return;

		int nx = (int)(((long)(screen.X - vLeft) * 65536 + (screen.X >= vLeft ? vWidth / 2 : -vWidth / 2)) / vWidth);
		int ny = (int)(((long)(screen.Y - vTop) * 65536 + (screen.Y >= vTop ? vHeight / 2 : -vHeight / 2)) / vHeight);

		SendMouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, nx, ny);
	}

	static void SendMouse(uint flags, int dx = 0, int dy = 0)
	{
		INPUT[] inputs = [new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags } }];
		SendInput(1, inputs, Marshal.SizeOf<INPUT>());
	}

	static async Task Frame() => await ((SceneTree)Engine.GetMainLoop()).ToSignal((SceneTree)Engine.GetMainLoop(), SceneTree.SignalName.ProcessFrame);

	const uint INPUT_MOUSE = 0;
	const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004, MOUSEEVENTF_ABSOLUTE = 0x8000, MOUSEEVENTF_VIRTUALDESK = 0x4000;
	const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

	[StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
	[StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }

	[StructLayout(LayoutKind.Sequential)]
	struct MOUSEINPUT
	{
		public int dx, dy;
		public uint mouseData;
		public uint dwFlags;
		public uint time;
		public nint dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct INPUT
	{
		public uint type;
		public MOUSEINPUT mi;
	}

	[DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
	[DllImport("user32.dll")] static extern bool GetWindowRect(nint hwnd, out RECT rect);
	[DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
	[DllImport("user32.dll")] static extern uint SendInput(uint numInputs, INPUT[] inputs, int size);
}
