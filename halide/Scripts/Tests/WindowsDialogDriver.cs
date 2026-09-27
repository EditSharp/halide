using Halide.Scripts.UI.Dialogs.Platform.Windows;
using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using static Halide.Scripts.UI.ContextMenu.Platform.Windows.Win32;
using static Halide.Scripts.UI.Dialogs.Platform.Windows.DialogWin32;

// works a native Windows dialog by its title and captures it
static class WindowsDialogDriver
{
	static nint DialogOf(string title) => FindWindowW("#32770", title);

	static List<nint> Controls(nint dialog)
	{
		List<nint> all = [];
		EnumChildWindows(dialog, (child, _) => { all.Add(child); return true; }, 0);
		return all;
	}

	static nint Button(string title, string text) =>
		DialogOf(title) is var dialog and not 0 ? Controls(dialog).FirstOrDefault(c => ClassOf(c) == "Button" && TextOf(c) == text) : 0;

	// found and clicked, when it's enabled; false when there's no such dialog
	public static bool Press(string title, string text)
	{
		if (DialogOf(title) == 0) return false;

		nint button = Button(title, text);
		if (button != 0 && IsWindowEnabled(button))
		{
			int id = (int)GetDlgCtrlID(button);
			SendMessageW(GetParent(button), WM_COMMAND, (nint)(id & 0xFFFF), button);
		}
		return true;
	}

	public static bool Showing(string title) => DialogOf(title) is var dialog and not 0 && IsWindowVisible(dialog);

	public static bool? TextShown(string title, string text) =>
		DialogOf(title) is var dialog and not 0 ? Controls(dialog).Any(c => ClassOf(c) == "Static" && TextOf(c) == text && IsWindowVisible(c)) : null;

	// a key through the dialog's own queue, so the dialog manager sees it as it would a real one
	public static bool Key(string title, Key key)
	{
		nint dialog = DialogOf(title);
		if (dialog == 0) return false;

		nint vk = key switch { Godot.Key.Enter => 0x0D, Godot.Key.Escape => 0x1B, Godot.Key.Tab => 0x09, _ => 0 };
		PostMessageW(dialog, 0x100, vk, 0);
		PostMessageW(dialog, 0x101, vk, 0);
		return true;
	}

	public static bool? Enabled(string title, string text) => Button(title, text) is var button and not 0 ? IsWindowEnabled(button) : null;

	// the edit box after the label with this text
	public static bool Type(string title, string label, string text)
	{
		nint dialog = DialogOf(title);
		if (dialog == 0) return false;

		List<nint> controls = Controls(dialog);
		int at = controls.FindIndex(c => ClassOf(c) == "Static" && TextOf(c) == label);
		nint edit = at >= 0 ? controls.Skip(at + 1).FirstOrDefault(c => ClassOf(c) == "Edit") : 0;
		if (edit != 0) SetWindowTextW(edit, text);
		return true;
	}

	public static bool Shot(string title, string file)
	{
		nint dialog = DialogOf(title);
		if (dialog == 0) return false;

		GetWindowRect(dialog, out RECT rect);
		nint screen = GetDC(0);
		nint dc = CreateCompatibleDC(screen);
		nint bitmap = CreateCompatibleBitmap(screen, rect.Width, rect.Height);
		nint was = SelectObject(dc, bitmap);
		// captured from the screen, or from the window itself while the screen is locked
		byte[] bgra;
		if (!Locked())
		{
			BitBlt(dc, 0, 0, rect.Width, rect.Height, screen, rect.left, rect.top, 0x00CC0020);
			bgra = Bits(dc, bitmap, rect);
		}
		else
		{
			PrintWindow(dialog, dc, PW_RENDERFULLCONTENT);
			bgra = Bits(dc, bitmap, rect);
			GD.Print($"PROBE the screen is locked; {title} captured square, without composition");
		}
		SelectObject(dc, was);

		DeleteObject(bitmap);
		DeleteDC(dc);
		ReleaseDC(0, screen);

		for (int i = 0; i < bgra.Length; i += 4)
		{
			(bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);
			bgra[i + 3] = 255;
		}

		Image.CreateFromData(rect.Width, rect.Height, false, Image.Format.Rgba8, bgra).SavePng(file);
		return true;
	}

	static byte[] Bits(nint dc, nint bitmap, RECT rect)
	{
		BITMAPINFOHEADER header = new() { biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = rect.Width, biHeight = -rect.Height, biPlanes = 1, biBitCount = 32 };
		byte[] bits = new byte[rect.Width * rect.Height * 4];
		GetDIBits(dc, bitmap, 0, (uint)rect.Height, bits, ref header, DIB_RGB_COLORS);
		return bits;
	}

	// windows runs LogonUI only while the lock or sign-in screen is up
	static bool Locked() => System.Diagnostics.Process.GetProcessesByName("LogonUI").Length > 0;

	[DllImport("user32.dll")] static extern nint GetDlgCtrlID(nint control);
	[DllImport("gdi32.dll")] static extern bool BitBlt(nint dc, int x, int y, int width, int height, nint source, int sx, int sy, uint rop);
}
