using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Halide.Scripts.UI.ContextMenu.Platform.Windows;
using static Halide.Scripts.UI.ContextMenu.Platform.Windows.Win32;

namespace Halide.Scripts.UI.Dialogs.Platform.Windows;

// the win32 a native dialog needs beyond what menus use
static class DialogWin32
{
	public const uint WS_POPUP = 0x80000000, WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CAPTION = 0xC00000, WS_SYSMENU = 0x80000;
	public const uint WS_TABSTOP = 0x10000, WS_GROUP = 0x20000, WS_VSCROLL = 0x200000, WS_CLIPCHILDREN = 0x2000000, WS_CLIPSIBLINGS = 0x4000000;
	public const uint WS_EX_DLGMODALFRAME = 0x1, WS_EX_CLIENTEDGE = 0x200, WS_EX_CONTROLPARENT = 0x10000;
	public const uint DS_MODALFRAME = 0x80;

	public const uint SS_LEFT = 0x0, SS_NOTIFY = 0x100, SS_CENTERIMAGE = 0x200, SS_NOPREFIX = 0x80, SS_ENDELLIPSIS = 0x4000, SS_PATHELLIPSIS = 0x8000, SS_EDITCONTROL = 0x2000;
	public const uint ES_AUTOHSCROLL = 0x80;
	public const uint BS_PUSHBUTTON = 0x0, BS_DEFPUSHBUTTON = 0x1, BS_AUTOCHECKBOX = 0x3;
	public const uint CBS_DROPDOWNLIST = 0x3, CBS_HASSTRINGS = 0x200;
	public const uint UDS_ALIGNRIGHT = 0x4, UDS_ARROWKEYS = 0x20, UDS_HOTTRACK = 0x100;

	public const uint WM_CLOSE = 0x10, WM_ERASEBKGND = 0x14, WM_SETFONT = 0x30, WM_GETFONT = 0x31, WM_NOTIFY = 0x4E, WM_SETTEXT = 0xC;
	public const uint WM_INITDIALOG = 0x110, WM_COMMAND = 0x111, WM_VSCROLL = 0x115, WM_MOUSEWHEEL = 0x20A, WM_DPICHANGED = 0x2E0, WM_NEXTDLGCTL = 0x28;
	public const uint WM_CTLCOLOREDIT = 0x133, WM_CTLCOLORLISTBOX = 0x134, WM_CTLCOLORBTN = 0x135, WM_CTLCOLORDLG = 0x136, WM_CTLCOLORSTATIC = 0x138;
	public const uint DM_SETDEFID = 0x401;
	public const uint BM_GETCHECK = 0xF0, BM_SETCHECK = 0xF1, BM_CLICK = 0xF5;
	public const uint CB_ADDSTRING = 0x143, CB_GETCURSEL = 0x147, CB_RESETCONTENT = 0x14B, CB_SETCURSEL = 0x14E, CB_SETMINVISIBLE = 0x1701;
	public const uint EM_SETCUEBANNER = 0x1501, CB_SETITEMHEIGHT = 0x153, UDM_SETRANGE32 = 0x46F, UDM_SETPOS32 = 0x471;
	public const uint GW_HWNDNEXT = 2, GW_CHILD = 5;
	public const uint WM_GETICON = 0x7F, WM_SETICON = 0x80;
	public const int ICON_SMALL = 0, ICON_SMALL2 = 2, GCLP_HICONSM = -34;
	public const int IDC_ARROW = 32512;

	public const int BN_CLICKED = 0, EN_CHANGE = 0x300, EN_KILLFOCUS = 0x200, CBN_SELCHANGE = 1, STN_CLICKED = 0;
	public const int UDN_DELTAPOS = -722;
	public const int IDOK = 1, IDCANCEL = 2;
	public const int DWLP_MSGRESULT = 0;

	public const int SB_VERT = 1, SB_LINEUP = 0, SB_LINEDOWN = 1, SB_PAGEUP = 2, SB_PAGEDOWN = 3, SB_THUMBTRACK = 5, SB_TOP = 6, SB_BOTTOM = 7;
	public const uint SIF_RANGE = 0x1, SIF_PAGE = 0x2, SIF_POS = 0x4, SIF_TRACKPOS = 0x10, SIF_ALL = 0x17;
	public const uint SW_SCROLLCHILDREN = 0x1, SW_INVALIDATE = 0x2, SW_ERASE = 0x4;

	public const int SW_HIDE = 0, SW_SHOW = 5, SW_SHOWNA = 8;
	public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
	public const uint DT_WORDBREAK = 0x10, DT_CALCRECT = 0x400, DT_EDITCONTROL = 0x2000;
	public const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWCP_ROUND = 2;
	public const uint DDC_DISABLE_ALL = 0x1, DCDC_DISABLE_FONTUPDATE = 0x1, DCDC_DISABLE_RELAYOUT = 0x2;
	public const uint MONITOR_DEFAULTTONEAREST = 2;
	public const uint PW_RENDERFULLCONTENT = 2;

	public delegate nint DialogProc(nint dialog, uint msg, nint wParam, nint lParam);
	public delegate nint WindowProc(nint window, uint msg, nint wParam, nint lParam);

	[StructLayout(LayoutKind.Sequential)]
	public struct NMHDR { public nint hwndFrom; public nuint idFrom; public int code; }

	[StructLayout(LayoutKind.Sequential)]
	public struct NMUPDOWN { public NMHDR hdr; public int iPos, iDelta; }

	[StructLayout(LayoutKind.Sequential)]
	public struct SCROLLINFO { public uint cbSize, fMask; public int nMin, nMax; public uint nPage; public int nPos, nTrackPos; }

	[StructLayout(LayoutKind.Sequential)]
	public struct MONITORINFO { public uint cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct WNDCLASSEXW
	{
		public uint cbSize, style;
		public nint lpfnWndProc;
		public int cbClsExtra, cbWndExtra;
		public nint hInstance, hIcon, hCursor, hbrBackground;
		public string lpszMenuName, lpszClassName;
		public nint hIconSm;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct ACTCTXW
	{
		public uint cbSize, dwFlags;
		public string lpSource;
		public ushort wProcessorArchitecture, wLangId;
		public string lpAssemblyDirectory;
		public nint lpResourceName;
		public string lpApplicationName;
		public nint hModule;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct INITCOMMONCONTROLSEX { public uint dwSize, dwICC; }

	[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint CreateDialogIndirectParamW(nint instance, nint template, nint parent, DialogProc proc, nint param);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern ushort RegisterClassExW(ref WNDCLASSEXW cls);
	[DllImport("user32.dll")] public static extern nint DefWindowProcW(nint window, uint msg, nint wParam, nint lParam);
	[DllImport("user32.dll")] public static extern bool DestroyWindow(nint window);
	[DllImport("user32.dll")] public static extern bool EnableWindow(nint window, bool enable);
	[DllImport("user32.dll")] public static extern bool IsWindowEnabled(nint window);
	[DllImport("user32.dll")] public static extern bool ShowWindow(nint window, int command);
	[DllImport("user32.dll")] public static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
	[DllImport("user32.dll")] public static extern bool MoveWindow(nint window, int x, int y, int width, int height, bool repaint);
	[DllImport("user32.dll")] public static extern bool GetClientRect(nint window, out RECT rect);
	[DllImport("user32.dll")] public static extern bool AdjustWindowRectExForDpi(ref RECT rect, uint style, bool menu, uint exStyle, uint dpi);
	[DllImport("user32.dll")] public static extern nint SendMessageW(nint window, uint msg, nint wParam, nint lParam);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint SendMessageW(nint window, uint msg, nint wParam, string lParam);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetWindowTextW(nint window, string text);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(nint window, StringBuilder text, int max);
	[DllImport("user32.dll")] public static extern int GetWindowTextLengthW(nint window);
	[DllImport("user32.dll")] public static extern bool IsDialogMessageW(nint dialog, ref MenuThread.MSG msg);
	[DllImport("user32.dll")] public static extern nint LoadCursorW(nint instance, nint name);
	[DllImport("user32.dll")] public static extern nint SetWindowLongPtrW(nint window, int index, nint value);
	[DllImport("user32.dll")] public static extern nint GetParent(nint window);
	[DllImport("user32.dll")] public static extern nint GetFocus();
	[DllImport("user32.dll")] public static extern nint GetDlgItem(nint dialog, int id);
	[DllImport("user32.dll")] public static extern nint GetWindow(nint window, uint command);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(nint window, StringBuilder name, int max);
	[DllImport("user32.dll")] public static extern nint GetDC(nint window);
	[DllImport("user32.dll")] public static extern int ReleaseDC(nint window, nint dc);
	[DllImport("user32.dll")] public static extern int SetScrollInfo(nint window, int bar, ref SCROLLINFO info, bool redraw);
	[DllImport("user32.dll")] public static extern bool GetScrollInfo(nint window, int bar, ref SCROLLINFO info);
	[DllImport("user32.dll")] public static extern int ScrollWindowEx(nint window, int dx, int dy, nint scroll, nint clip, nint update, nint updateRect, uint flags);
	[DllImport("user32.dll")] public static extern bool InvalidateRect(nint window, nint rect, bool erase);
	[DllImport("user32.dll")] public static extern nint MonitorFromWindow(nint window, uint flags);
	[DllImport("user32.dll")] public static extern bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);
	[DllImport("user32.dll")] public static extern int SetDialogDpiChangeBehavior(nint dialog, uint mask, uint values);
	[DllImport("user32.dll")] public static extern int SetDialogControlDpiChangeBehavior(nint control, uint mask, uint values);
	[DllImport("user32.dll")] public static extern bool PrintWindow(nint window, nint dc, uint flags);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindowW(string className, string windowName);
	[DllImport("user32.dll")] public static extern bool EnumChildWindows(nint parent, EnumProc proc, nint param);
	public delegate bool EnumProc(nint window, nint param);

	[DllImport("gdi32.dll")] public static extern uint SetBkColor(nint dc, uint color);
	[DllImport("gdi32.dll")] public static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
	[DllImport("gdi32.dll")] public static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);

	[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(nint window, string app, string ids);
	[DllImport("uxtheme.dll", EntryPoint = "#133")] public static extern bool AllowDarkModeForWindow(nint window, bool allow);

	[DllImport("comctl32.dll")] public static extern bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX init);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint CreateActCtxW(ref ACTCTXW context);
	[DllImport("kernel32.dll")] public static extern bool ActivateActCtx(nint context, out nuint cookie);
	[DllImport("kernel32.dll")] public static extern bool DeactivateActCtx(uint flags, nuint cookie);
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandleW(string module);

	public static int Low(nint value) => (short)((long)value & 0xFFFF);
	public static int High(nint value) => (short)(((long)value >> 16) & 0xFFFF);

	public static string TextOf(nint window)
	{
		StringBuilder text = new(GetWindowTextLengthW(window) + 1);
		GetWindowTextW(window, text, text.Capacity);
		return text.ToString();
	}

	public static string ClassOf(nint window)
	{
		StringBuilder name = new(64);
		GetClassNameW(window, name, name.Capacity);
		return name.ToString();
	}

	// themed common controls (v6) for whatever is created while it's active; the host exe's manifest may not ask for them
	static nint visualStyles = -2;

	public static nuint EnterVisualStyles()
	{
		if (visualStyles == -2)
		{
			string manifest = Path.Combine(Path.GetTempPath(), "editsharp-comctl6.manifest");
			File.WriteAllText(manifest, """
				<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
				<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
				  <dependency><dependentAssembly><assemblyIdentity type="win32" name="Microsoft.Windows.Common-Controls" version="6.0.0.0" processorArchitecture="*" publicKeyToken="6595b64144ccf1df" language="*"/></dependentAssembly></dependency>
				</assembly>
				""");
			ACTCTXW context = new() { cbSize = (uint)Marshal.SizeOf<ACTCTXW>(), lpSource = manifest };
			visualStyles = CreateActCtxW(ref context);
		}

		if (visualStyles == -1 || !ActivateActCtx(visualStyles, out nuint cookie)) return 0;

		INITCOMMONCONTROLSEX init = new() { dwSize = (uint)Marshal.SizeOf<INITCOMMONCONTROLSEX>(), dwICC = 0x10 | 0x4000 };
		InitCommonControlsEx(ref init);
		return cookie;
	}

	public static void LeaveVisualStyles(nuint cookie)
	{
		if (cookie != 0) DeactivateActCtx(0, cookie);
	}
}
