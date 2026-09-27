using Godot;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static Halide.Scripts.UI.ContextMenu.Platform.Windows.Win32;

namespace Halide.Scripts.App.Chrome.Platform;

// keeps the native frame (snap, shadow, rounded corners) but gives its caption to the drawn bar
static class WindowsChrome
{
	const uint WM_NCCALCSIZE = 0x83, WM_NCHITTEST = 0x84, WM_NCMOUSEMOVE = 0xA0, WM_NCLBUTTONDOWN = 0xA1, WM_NCLBUTTONUP = 0xA2, WM_NCRBUTTONUP = 0xA5, WM_NCMOUSELEAVE = 0x2A2;
	const uint WM_SYSCOMMAND = 0x112, WM_CONTEXTMENU = 0x7B;
	const uint SC_SIZE = 0xF000, SC_MOVE = 0xF010, SC_MINIMIZE = 0xF020, SC_MAXIMIZE = 0xF030, SC_CLOSE = 0xF060, SC_KEYMENU = 0xF100, SC_RESTORE = 0xF120;
	const uint MF_ENABLED = 0x0, MF_GRAYED = 0x1, TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2;
	const int HTCLIENT = 1, HTCAPTION = 2, HTTOP = 12, HTMAXBUTTON = 9;
	const int SM_CYCAPTION = 4, SM_CYSIZEFRAME = 33, SM_CXPADDEDBORDER = 92;
	const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;
	const uint TME_LEAVE = 0x2, TME_NONCLIENT = 0x10;

	static readonly Dictionary<nint, (Window Window, UITopBar Bar)> windows = [];
	static readonly SubclassProc proc = Proc;

	// per window, how far the native caption and top border reach into what is now client area
	static readonly Dictionary<nint, int> captionInsets = [];

	// where the content really is on screen: Godot's Position keeps what was asked for until the window next moves
	public static Vector2I ContentPosition(Window window)
	{
		nint hwnd = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, window.GetWindowId());
		POINT corner = new();
		return hwnd != 0 && ClientToScreen(hwnd, ref corner) ? new Vector2I(corner.x, corner.y) : window.Position;
	}

	// Godot still places the window as if the caption sat above the content, so content lands this much higher than asked
	public static int CaptionInset(Window window) =>
		captionInsets.TryGetValue((nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, window.GetWindowId()), out int inset) ? inset : 0;

	public static void Attach(Window window, UITopBar bar)
	{
		nint hwnd = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, window.GetWindowId());
		if (hwnd == 0 || windows.ContainsKey(hwnd)) return;

		windows[hwnd] = (window, bar);
		SetWindowSubclass(hwnd, proc, 1, 0);

		// the frame is worked out again without the caption, keeping the content where it was
		bool normal = window.Mode == Window.ModeEnum.Windowed;
		GetWindowRect(hwnd, out RECT outer);
		SetWindowPos(hwnd, 0, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
		if (normal && captionInsets.TryGetValue(hwnd, out int inset) && inset > 0)
			SetWindowPos(hwnd, 0, outer.left, outer.top + inset, outer.right - outer.left, outer.bottom - outer.top - inset, SWP_NOZORDER | SWP_NOACTIVATE);

		window.TreeExiting += () =>
		{
			RemoveWindowSubclass(hwnd, proc, 1);
			windows.Remove(hwnd);
			captionInsets.Remove(hwnd);
		};
	}

	static nint Proc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint data)
	{
		if (!windows.TryGetValue(hwnd, out (Window Window, UITopBar Bar) entry) || !GodotObject.IsInstanceValid(entry.Bar))
			return DefSubclassProc(hwnd, msg, wParam, lParam);

		switch (msg)
		{
			// the client area takes the caption's place; a maximized window keeps its top inside the screen
			case WM_NCCALCSIZE when wParam != 0:
			{
				RECT before = Marshal.PtrToStructure<RECT>(lParam);
				nint result = DefSubclassProc(hwnd, msg, wParam, lParam);
				RECT after = Marshal.PtrToStructure<RECT>(lParam);
				if (!IsZoomed(hwnd)) captionInsets[hwnd] = after.top - before.top;
				after.top = before.top + (IsZoomed(hwnd) ? Frame(hwnd) : 0);
				Marshal.StructureToPtr(after, lParam, false);
				return result;
			}

			case WM_NCHITTEST:
				return Hit(hwnd, entry, lParam);

			// the maximize button is the OS's while the pointer is on it, for the Snap Layouts flyout
			case WM_NCMOUSEMOVE:
				entry.Bar.ShowMaximizeHot(wParam == HTMAXBUTTON);
				TRACKMOUSEEVENT track = new() { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = TME_LEAVE | TME_NONCLIENT, hwndTrack = hwnd };
				TrackMouseEvent(ref track);
				break;

			case WM_NCMOUSELEAVE:
				entry.Bar.ShowMaximizeHot(false);
				entry.Bar.ShowMaximizePressed(false);
				break;

			case WM_NCLBUTTONDOWN when wParam == HTMAXBUTTON:
				entry.Bar.ShowMaximizePressed(true);
				return 0;

			case WM_NCLBUTTONUP when wParam == HTMAXBUTTON:
				entry.Bar.ShowMaximizePressed(false);
				entry.Bar.ToggleMaximized();
				return 0;

			// the window menu, on a right click in the caption or Alt+Space, as a native window has it
			case WM_NCRBUTTONUP when wParam == HTCAPTION:
				ShowSystemMenu(hwnd, (short)((long)lParam & 0xFFFF), (short)(((long)lParam >> 16) & 0xFFFF));
				return 0;

			// a right click on the caption arrives as a context menu request, which godot would otherwise swallow
			case WM_CONTEXTMENU when (long)lParam != -1 && Hit(hwnd, entry, lParam) == HTCAPTION:
				ShowSystemMenu(hwnd, (short)((long)lParam & 0xFFFF), (short)(((long)lParam >> 16) & 0xFFFF));
				return 0;

			case WM_SYSCOMMAND when ((uint)wParam & 0xFFF0) == SC_KEYMENU && (long)lParam == ' ':
				POINT corner = new();
				ClientToScreen(hwnd, ref corner);
				ShowSystemMenu(hwnd, corner.x, corner.y);
				return 0;
		}

		return DefSubclassProc(hwnd, msg, wParam, lParam);
	}

	// the caption as Windows draws it restored, kept when maximized so the tabs and menus in it keep their room, and the 46-wide buttons; in 96-dpi units, drawn at the window's DPI
	public static (float Height, float CaptionWidth, float Scale) Metrics(Window window)
	{
		nint hwnd = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, window.GetWindowId());
		uint dpi = hwnd != 0 ? GetDpiForWindow(hwnd) : 96;
		if (dpi == 0) dpi = 96;

		int caption = GetSystemMetricsForDpi(SM_CYCAPTION, 96);
		int frame = GetSystemMetricsForDpi(SM_CYSIZEFRAME, 96) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, 96);

		// window units are the interface scale's; the bar answers to the monitor's alone
		float scale = dpi / 96f / window.ContentScaleFactor;
		return (caption + frame, 46f, scale);
	}

	// what's under a screen point: the OS's frame, our caption and maximize button, or the client
	static nint Hit(nint hwnd, (Window Window, UITopBar Bar) entry, nint lParam)
	{
		nint hit = DefSubclassProc(hwnd, WM_NCHITTEST, 0, lParam);
		if (hit != HTCLIENT) return hit;

		POINT point = new() { x = (short)((long)lParam & 0xFFFF), y = (short)(((long)lParam >> 16) & 0xFFFF) };
		ScreenToClient(hwnd, ref point);
		if (!IsZoomed(hwnd) && point.y < Frame(hwnd)) return HTTOP;

		Vector2 at = entry.Window.GetScreenTransform().AffineInverse() * new Vector2(point.x, point.y);
		return entry.Bar.RegionAt(at) switch
		{
			UITopBar.Region.Caption => HTCAPTION,
			UITopBar.Region.Maximize => HTMAXBUTTON,
			_ => HTCLIENT,
		};
	}

	static void ShowSystemMenu(nint hwnd, int x, int y)
	{
		nint menu = GetSystemMenu(hwnd, false);
		if (menu == 0) return;

		bool maximized = IsZoomed(hwnd);
		EnableMenuItem(menu, SC_RESTORE, maximized ? MF_ENABLED : MF_GRAYED);
		EnableMenuItem(menu, SC_MOVE, maximized ? MF_GRAYED : MF_ENABLED);
		EnableMenuItem(menu, SC_SIZE, maximized ? MF_GRAYED : MF_ENABLED);
		EnableMenuItem(menu, SC_MINIMIZE, MF_ENABLED);
		EnableMenuItem(menu, SC_MAXIMIZE, maximized ? MF_GRAYED : MF_ENABLED);
		EnableMenuItem(menu, SC_CLOSE, MF_ENABLED);
		SetMenuDefaultItem(menu, SC_CLOSE, 0);

		uint command = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, x, y, 0, hwnd, 0);
		if (command != 0) PostMessageW(hwnd, WM_SYSCOMMAND, (nint)command, 0);
	}

	// the resize border's thickness at the window's DPI
	static int Frame(nint hwnd)
	{
		uint dpi = GetDpiForWindow(hwnd);
		return GetSystemMetricsForDpi(SM_CYSIZEFRAME, dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi);
	}

	[StructLayout(LayoutKind.Sequential)]
	struct POINT { public int x, y; }

	[StructLayout(LayoutKind.Sequential)]
	struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public nint hwndTrack; public uint dwHoverTime; }

	[DllImport("user32.dll")] static extern bool IsZoomed(nint hwnd);
	[DllImport("user32.dll")] static extern bool GetWindowRect(nint hwnd, out RECT rect);
	[DllImport("user32.dll")] static extern bool ScreenToClient(nint hwnd, ref POINT point);
	[DllImport("user32.dll")] static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
	[DllImport("user32.dll")] static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT track);
	[DllImport("user32.dll")] static extern bool ClientToScreen(nint hwnd, ref POINT point);
	[DllImport("user32.dll")] static extern nint GetSystemMenu(nint hwnd, bool revert);
	[DllImport("user32.dll")] static extern int EnableMenuItem(nint menu, uint item, uint flags);
	[DllImport("user32.dll")] static extern bool SetMenuDefaultItem(nint menu, uint item, uint byPosition);
	[DllImport("user32.dll")] static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
}
