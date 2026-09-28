using Halide.Scripts.Input;
using Godot;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Halide.Tests;

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

	// a point in `window`'s viewport, in real win32 screen pixels. NOT corrected against the window's real
	// win32 rect (GetWindowRect): a click landed 8px off target with that correction applied, because godot
	// reads an OS click's position back through its OWN WindowGetPosition, not the real win32 rect -- the two
	// can disagree (found here by ~11px) the same way the native menu handler's ToWin32Offset bug did, but
	// applying that same fix here corrects for a discrepancy godot's own read-back never sees, so it just
	// pushes the physical click point off by the same amount. staying purely in godot's own coordinate space
	// (WindowGetPosition + local * ContentScaleFactor) is what actually round-trips correctly
	public static Vector2I ScreenPointIn(Window window, Vector2 viewportPoint)
		=> DisplayServer.WindowGetPosition(window.GetWindowId()) + (Vector2I)(viewportPoint * window.ContentScaleFactor).Round();

	// a window's own top-left, in real win32 screen pixels -- for arithmetic outside any Control's rect (e.g. a point past the window's edge)
	public static Vector2I ScreenPositionOf(Window window) => DisplayServer.WindowGetPosition(window.GetWindowId());

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

	// presses (and releases) a whole combo through SendInput: modifiers down, the key, modifiers up --
	// same injection path the mouse side uses, so a shortcut test exercises Keyboard.Dispatch for real
	// rather than calling KeyCombo/the handler directly
	public static async Task PressCombo(KeyCombo combo)
	{
		if (combo.Control) SendKey(VK_CONTROL, true);
		if (combo.Shift) SendKey(VK_SHIFT, true);
		if (combo.Alt) SendKey(VK_MENU, true);
		await Frame();

		ushort vk = VkOf(combo.Key);
		SendKey(vk, true);
		await Frame();
		SendKey(vk, false);
		await Frame();

		if (combo.Alt) SendKey(VK_MENU, false);
		if (combo.Shift) SendKey(VK_SHIFT, false);
		if (combo.Control) SendKey(VK_CONTROL, false);
		await Frame();
	}

	static void SendKey(ushort vk, bool down)
	{
		INPUT_KBD input = new() { type = INPUT_KEYBOARD, wVk = vk, dwFlags = down ? 0 : KEYEVENTF_KEYUP };
		INPUT_KBD[] inputs = [input];
		uint sent = SendInput(1, inputs, INPUT_KBD.Size);
		if (sent != 1) Godot.GD.PrintErr($"[RealInput] SendInput (key) reported {sent} sent, error={Marshal.GetLastWin32Error()}");
	}

	// godot's Key enum matches ASCII for letters, digits and most punctuation, but not for the
	// non-printable keys (arrows, backspace, delete, home/end, function keys) -- those get an explicit
	// win32 VK mapping here rather than an algorithmic conversion
	static readonly Dictionary<Key, ushort> SpecialVks = new()
	{
		[Key.Space] = 0x20,
		[Key.Right] = 0x27,
		[Key.Left] = 0x25,
		[Key.Up] = 0x26,
		[Key.Down] = 0x28,
		[Key.Backspace] = 0x08,
		[Key.Delete] = 0x2E,
		[Key.Tab] = 0x09,
		[Key.Enter] = 0x0D,
		[Key.Escape] = 0x1B,
		[Key.Home] = 0x24,
		[Key.End] = 0x23,
		[Key.Pageup] = 0x21,
		[Key.Pagedown] = 0x22,
		[Key.F1] = 0x70,
		[Key.F2] = 0x71,
		[Key.F3] = 0x72,
		[Key.F4] = 0x73,
		[Key.F5] = 0x74,
		[Key.F6] = 0x75,
		[Key.F7] = 0x76,
		[Key.F8] = 0x77,
		[Key.F9] = 0x78,
		[Key.F10] = 0x79,
		[Key.F11] = 0x7A,
		[Key.F12] = 0x7B,
		[Key.Equal] = 0xBB, // VK_OEM_PLUS -- the unshifted '=' on a US keyboard
		[Key.Minus] = 0xBD, // VK_OEM_MINUS
		[Key.Comma] = 0xBC, // VK_OEM_COMMA
		[Key.Period] = 0xBE, // VK_OEM_PERIOD
		[Key.Slash] = 0xBF, // VK_OEM_2
		[Key.Backslash] = 0xDC, // VK_OEM_5
		[Key.Semicolon] = 0xBA, // VK_OEM_1
	};

	static ushort VkOf(Key key)
	{
		if (SpecialVks.TryGetValue(key, out ushort vk)) return vk;

		// letters (Key.A..Z) and digits (Key.Key0..Key9) sit at the same codepoints as their VK
		// constants (ASCII 'A'-'Z' and '0'-'9'), so no table entry is needed for those
		return (ushort)key;
	}

	static async Task Frame() => await ((SceneTree)Engine.GetMainLoop()).ToSignal((SceneTree)Engine.GetMainLoop(), SceneTree.SignalName.ProcessFrame);

	const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
	const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004, MOUSEEVENTF_ABSOLUTE = 0x8000, MOUSEEVENTF_VIRTUALDESK = 0x4000;
	const uint KEYEVENTF_KEYUP = 0x0002;
	const ushort VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
	const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

	[StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }

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

	// win32's INPUT is a union of MOUSEINPUT/KEYBDINPUT/HARDWAREINPUT, so on x64 it is always 40 bytes --
	// the size of its largest member (MOUSEINPUT), not whichever variant is actually filled in. a plain
	// sequential struct with just the KEYBDINPUT fields comes out to 32 bytes, and SendInput validates its
	// cbSize parameter against its own real 40-byte struct and silently rejects (returns 0, sends nothing)
	// anything that doesn't match -- explicit offsets matching the real struct layout are needed instead
	[StructLayout(LayoutKind.Explicit, Size = 40)]
	struct INPUT_KBD
	{
		public const int Size = 40;

		[FieldOffset(0)] public uint type;
		[FieldOffset(8)] public ushort wVk;
		[FieldOffset(10)] public ushort wScan;
		[FieldOffset(12)] public uint dwFlags;
		[FieldOffset(16)] public uint time;
		[FieldOffset(24)] public nint dwExtraInfo;
	}

	[DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
	[DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
	[DllImport("user32.dll")] static extern uint SendInput(uint numInputs, INPUT[] inputs, int size);
	[DllImport("user32.dll", EntryPoint = "SendInput")] static extern uint SendInput(uint numInputs, INPUT_KBD[] inputs, int size);
}
