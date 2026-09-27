using Godot;
using System;
using System.Runtime.InteropServices;
using EditSharpGUI.Scripts.UI.ContextMenu.Platform.MacOS;

namespace EditSharpGUI.Scripts.App.Platform;

// the root window hosts the app and is never shown: godot won't hide its main
// window itself, so each OS is asked directly. wayland has no way to hide a
// window, so there it becomes a one pixel transparent window that can't take focus
public static class HostWindow
{
	public static void Hide(Window root)
	{
		switch (OS.GetName())
		{
			case "Windows":
				ShowWindow(Handle(DisplayServer.HandleType.WindowHandle), 0);
				return;

			case "macOS":
				ObjC.Send(Handle(DisplayServer.HandleType.WindowHandle), ObjC.Sel("orderOut:"), (nint)0);
				return;

			case "Linux" or "FreeBSD" when DisplayServer.GetName() == "X11":
				XUnmapWindow(Handle(DisplayServer.HandleType.DisplayHandle), Handle(DisplayServer.HandleType.WindowHandle));
				XFlush(Handle(DisplayServer.HandleType.DisplayHandle));
				return;

			default:
				Shrink(root);
				return;
		}
	}

	// whether the right mouse button is down anywhere on screen; windows only, false elsewhere
	public static bool RightButtonHeld => OS.GetName() == "Windows" && (GetAsyncKeyState(0x02) & 0x8000) != 0;

	[DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);

	// what's left when a window can't be hidden: too small and clear to see, out of the way
	static void Shrink(Window root)
	{
		root.Borderless = true;
		root.Transparent = true;
		root.TransparentBg = true;
		root.Unfocusable = true;
		root.MinSize = Vector2I.One;
		root.Size = Vector2I.One;
		root.Position = new Vector2I(-10000, -10000);
	}

	static nint Handle(DisplayServer.HandleType type) => (nint)DisplayServer.WindowGetNativeHandle(type, 0);

	[DllImport("user32.dll")]
	static extern bool ShowWindow(nint hwnd, int command);

	[DllImport("libX11.so.6")]
	static extern int XUnmapWindow(nint display, nint window);

	[DllImport("libX11.so.6")]
	static extern int XFlush(nint display);
}
