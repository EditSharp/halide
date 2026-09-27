using System;
using System.Runtime.InteropServices;

namespace EditSharpGUI.Scripts.UI.ContextMenu.Platform.Windows;

// the win32 surface the windows handler needs, and nothing more
static class Win32
{
    public const int SM_CXSMICON = 49;

    public const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100, TPM_NOANIMATION = 0x4000;

    public const uint MIIM_STATE = 0x1, MIIM_ID = 0x2, MIIM_SUBMENU = 0x4, MIIM_DATA = 0x20, MIIM_FTYPE = 0x100;
    public const uint MFT_OWNERDRAW = 0x100, MFT_RADIOCHECK = 0x200, MFT_SEPARATOR = 0x800;
    public const uint MFS_DISABLED = 0x3, MFS_CHECKED = 0x8;

    public const uint WM_CANCELMODE = 0x1F, WM_DRAWITEM = 0x2B, WM_MEASUREITEM = 0x2C, WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_INITMENUPOPUP = 0x117;
    public const int GCL_STYLE = -26;
    public const uint CS_DROPSHADOW = 0x20000;
    public const int VK_DOWN = 0x28;
    public const uint ODT_MENU = 1;
    public const uint ODS_SELECTED = 0x1, ODS_DISABLED = 0x4;
    public const uint ODA_DRAWENTIRE = 0x1;

    public const uint DT_LEFT = 0x0, DT_CENTER = 0x1, DT_RIGHT = 0x2, DT_VCENTER = 0x4, DT_SINGLELINE = 0x20, DT_NOPREFIX = 0x800;
    public const int TRANSPARENT = 1;
    public const int PS_SOLID = 0;
    public const uint DIB_RGB_COLORS = 0;
    public const uint SPI_GETNONCLIENTMETRICS = 0x29;
    public const byte CLEARTYPE_QUALITY = 5;
    public const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;

    public const int COLOR_MENU = 4, COLOR_GRAYTEXT = 17, COLOR_MENUTEXT = 7, COLOR_HIGHLIGHT = 13, COLOR_HIGHLIGHTTEXT = 14, COLOR_3DSHADOW = 16;

    // uxtheme MENU parts, states and properties
    public const int MENU_POPUPBACKGROUND = 9, MENU_POPUPBORDERS = 10, MENU_POPUPCHECK = 11, MENU_POPUPITEM = 14, MENU_POPUPSEPARATOR = 15, MENU_POPUPSUBMENU = 16;
    public const int MPI_NORMAL = 1, MPI_HOT = 2, MPI_DISABLED = 3, MPI_DISABLEDHOT = 4;
    public const int MC_CHECKMARKNORMAL = 1, MC_CHECKMARKDISABLED = 2, MC_BULLETNORMAL = 3, MC_BULLETDISABLED = 4;
    public const int MSM_NORMAL = 1, MSM_DISABLED = 2;
    public const int TMT_FILLCOLOR = 3802, TMT_TEXTCOLOR = 3803;
    public const int TS_TRUE = 1;

    public enum PreferredAppMode { Default, AllowDark, ForceDark, ForceLight }

    public static uint Rgb(byte r, byte g, byte b) => r | (uint)g << 8 | (uint)b << 16;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left, top, right, bottom;
        public RECT(int left, int top, int right, int bottom) { this.left = left; this.top = top; this.right = right; this.bottom = bottom; }
        public readonly int Width => right - left;
        public readonly int Height => bottom - top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MENUITEMINFO
    {
        public uint cbSize, fMask, fType, fState, wID;
        public nint hSubMenu, hbmpChecked, hbmpUnchecked;
        public nuint dwItemData;
        public nint dwTypeData;
        public uint cch;
        public nint hbmpItem;
    }

    public const uint MIM_BACKGROUND = 0x2, MIM_APPLYTOSUBMENUS = 0x80000000;

    [StructLayout(LayoutKind.Sequential)]
    public struct MENUINFO
    {
        public uint cbSize, fMask, dwStyle, cyMax;
        public nint hbrBack;
        public uint dwContextHelpID;
        public nuint dwMenuData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MEASUREITEMSTRUCT
    {
        public uint CtlType, CtlID, itemID, itemWidth, itemHeight;
        public nuint itemData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DRAWITEMSTRUCT
    {
        public uint CtlType, CtlID, itemID, itemAction, itemState;
        public nint hwndItem, hDC;
        public RECT rcItem;
        public nuint itemData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct LOGFONTW
    {
        public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
        public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet, lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string lfFaceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NONCLIENTMETRICSW
    {
        public uint cbSize;
        public int iBorderWidth, iScrollWidth, iScrollHeight, iCaptionWidth, iCaptionHeight;
        public LOGFONTW lfCaptionFont;
        public int iSmCaptionWidth, iSmCaptionHeight;
        public LOGFONTW lfSmCaptionFont;
        public int iMenuWidth, iMenuHeight;
        public LOGFONTW lfMenuFont, lfStatusFont, lfMessageFont;
        public int iPaddedBorderWidth;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate nint SubclassProc(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData);

    // ---- menus and windows ----

    [DllImport("user32.dll")] public static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] public static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] public static extern bool InsertMenuItemW(nint menu, uint item, bool byPosition, ref MENUITEMINFO info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetMenuItemInfoW(nint menu, uint item, bool byPosition, ref MENUITEMINFO info);
    [DllImport("user32.dll")] public static extern bool SetMenuInfo(nint menu, ref MENUINFO info);
    [DllImport("user32.dll")] public static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint owner, nint parameters);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] public static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfoForDpi(uint action, uint param, ref NONCLIENTMETRICSW metrics, uint winIni, uint dpi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindowExW(nint parent, nint after, string className, string windowName);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint window, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PostMessageW(nint window, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] public static extern uint GetSysColor(int index);

    // ---- drawing ----

    [DllImport("user32.dll")] public static extern int FillRect(nint dc, ref RECT rect, nint brush);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int DrawTextW(nint dc, string text, int count, ref RECT rect, uint format);

    [DllImport("gdi32.dll")] public static extern nint CreateDIBSection(nint dc, ref BITMAPINFOHEADER info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(nint handle);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern nint CreateFontIndirectW(ref LOGFONTW font);
    [DllImport("gdi32.dll")] public static extern nint SelectObject(nint dc, nint handle);
    [DllImport("gdi32.dll")] public static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32.dll")] public static extern int SetBkMode(nint dc, int mode);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern bool GetTextExtentPoint32W(nint dc, string text, int count, out SIZE size);
    [DllImport("gdi32.dll")] public static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] public static extern nint CreatePen(int style, int width, uint color);
    [DllImport("gdi32.dll")] public static extern bool RoundRect(nint dc, int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] public static extern bool Ellipse(nint dc, int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] public static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] public static extern uint GetPixel(nint dc, int x, int y);
    [DllImport("gdi32.dll")] public static extern int ExcludeClipRect(nint dc, int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] public static extern bool GdiFlush();

    [DllImport("msimg32.dll")] public static extern bool AlphaBlend(nint dc, int x, int y, int width, int height, nint source, int sx, int sy, int swidth, int sheight, BLENDFUNCTION blend);

    // ---- themes ----

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern nint OpenThemeData(nint window, string classes);
    [DllImport("uxtheme.dll")] public static extern int CloseThemeData(nint theme);
    [DllImport("uxtheme.dll")] public static extern int DrawThemeBackground(nint theme, nint dc, int part, int state, ref RECT rect, nint clip);
    [DllImport("uxtheme.dll")] public static extern int GetThemeColor(nint theme, int part, int state, int property, out uint color);
    [DllImport("uxtheme.dll")] public static extern int GetThemePartSize(nint theme, nint dc, int part, int state, nint rect, int sizeType, out SIZE size);

    // undocumented, by ordinal: the switch that lets classic menus go dark (windows 10 1903+)
    [DllImport("uxtheme.dll", EntryPoint = "#135")] public static extern int SetPreferredAppMode(PreferredAppMode mode);
    [DllImport("uxtheme.dll", EntryPoint = "#136")] public static extern void FlushMenuThemes();

    // ---- dwm (windows 11) ----

    public const uint DWMWA_TRANSITIONS_FORCEDISABLED = 3, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34;
    public const uint DWMWCP_ROUNDSMALL = 3;

    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(nint window, uint attribute, ref uint value, uint size);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint window, nint processId);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern nuint GetClassLongPtrW(nint window, int index);
    [DllImport("user32.dll")] public static extern nuint SetClassLongPtrW(nint window, int index, nuint value);

    // ---- subclassing ----

    [DllImport("comctl32.dll")] public static extern bool SetWindowSubclass(nint window, SubclassProc proc, nuint id, nuint refData);
    [DllImport("comctl32.dll")] public static extern bool RemoveWindowSubclass(nint window, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] public static extern nint DefSubclassProc(nint window, uint msg, nint wParam, nint lParam);
}
