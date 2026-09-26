using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Godot;

namespace EditSharpGUI.Scripts.UI.DragDrop.Platform.Windows;

// files dragged over the window from outside the app, on windows. godot
// only says when files are dropped; this takes over the window's OLE drop
// target so the app hears the drag enter, move and leave too, and feeds
// them into DragDrop like any other drag. the drop itself comes through
// here as well, since godot's own target is revoked
public static class OleDragHover
{
	static DropTarget target;
	static nint hwnd;

	public static bool Installed => target is not null;

	// takes the window's drop target. call once, on the main thread
	public static void Install()
	{
		if (Installed || OS.GetName() != "Windows") return;

		int window = (int)DisplayServer.MainWindowId;
		hwnd = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, window);
		if (hwnd == 0) return;

		// godot registered its own target for its FilesDropped signal
		RevokeDragDrop(hwnd);

		target = new DropTarget(window);
		int result = RegisterDragDrop(hwnd, target);

		if (result != 0)
		{
			GD.PushWarning($"Could not take the window's drop target (0x{result:X8}); files drop without hover.");
			target = null;
		}
	}

	public static void Uninstall()
	{
		if (!Installed) return;
		RevokeDragDrop(hwnd);
		target = null;
	}

	[ComVisible(true)]
	[ClassInterface(ClassInterfaceType.None)]
	sealed class DropTarget(int window) : IDropTarget
	{
		FilesPayload payload;

		public int DragEnter(IDataObject data, uint keyState, POINTL point, ref uint effect)
		{
			List<string> files = ReadFiles(data);

			if (files.Count == 0)
			{
				payload = null;
				effect = DROPEFFECT_NONE;
				return 0;
			}

			payload = new FilesPayload(files);
			DragDrop.Begin(payload, null, Vector2.Zero, external: true);
			DragDrop.Update(ToViewport(point));
			effect = DragDrop.CanDropAt(ToViewport(point)) ? DROPEFFECT_COPY : DROPEFFECT_NONE;
			return 0;
		}

		public int DragOver(uint keyState, POINTL point, ref uint effect)
		{
			if (payload is null) { effect = DROPEFFECT_NONE; return 0; }

			Vector2 at = ToViewport(point);
			DragDrop.Update(at);
			effect = DragDrop.CanDropAt(at) ? DROPEFFECT_COPY : DROPEFFECT_NONE;
			return 0;
		}

		public int DragLeave()
		{
			if (payload is not null) DragDrop.Cancel();
			payload = null;
			return 0;
		}

		public int Drop(IDataObject data, uint keyState, POINTL point, ref uint effect)
		{
			if (payload is null) { effect = DROPEFFECT_NONE; return 0; }

			Vector2 at = ToViewport(point);
			bool taken = DragDrop.CanDropAt(at);
			DragDrop.Finish(at);
			payload = null;
			effect = taken ? DROPEFFECT_COPY : DROPEFFECT_NONE;
			return 0;
		}

		// screen pixels to the main viewport's own space
		Vector2 ToViewport(POINTL point)
		{
			Vector2 inWindow = new Vector2(point.x, point.y) - DisplayServer.WindowGetPosition(window);
			Window root = ((SceneTree)Engine.GetMainLoop()).Root;
			return root.GetScreenTransform().AffineInverse() * inWindow;
		}

		static List<string> ReadFiles(IDataObject data)
		{
			List<string> files = [];

			FORMATETC format = new()
			{
				cfFormat = CF_HDROP,
				dwAspect = DVASPECT.DVASPECT_CONTENT,
				lindex = -1,
				tymed = TYMED.TYMED_HGLOBAL
			};

			try
			{
				if (data.QueryGetData(ref format) != 0) return files;

				data.GetData(ref format, out STGMEDIUM medium);

				try
				{
					nint drop = medium.unionmember;
					uint count = DragQueryFileW(drop, 0xFFFFFFFF, null, 0);

					for (uint i = 0; i < count; i++)
					{
						uint length = DragQueryFileW(drop, i, null, 0);
						StringBuilder buffer = new((int)length + 1);
						DragQueryFileW(drop, i, buffer, length + 1);
						files.Add(buffer.ToString());
					}
				}
				finally
				{
					ReleaseStgMedium(ref medium);
				}
			}
			catch (Exception e)
			{
				GD.PushWarning($"Could not read the dragged files: {e.Message}");
			}

			return files;
		}
	}

	// ---- win32 ----

	const short CF_HDROP = 15;
	const uint DROPEFFECT_NONE = 0;
	const uint DROPEFFECT_COPY = 1;

	[StructLayout(LayoutKind.Sequential)]
	public struct POINTL
	{
		public int x;
		public int y;
	}

	[ComImport]
	[Guid("00000122-0000-0000-C000-000000000046")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface IDropTarget
	{
		[PreserveSig] int DragEnter(IDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect);
		[PreserveSig] int DragOver(uint grfKeyState, POINTL pt, ref uint pdwEffect);
		[PreserveSig] int DragLeave();
		[PreserveSig] int Drop(IDataObject pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect);
	}

	[DllImport("ole32.dll")] static extern int RegisterDragDrop(nint hwnd, IDropTarget target);
	[DllImport("ole32.dll")] static extern int RevokeDragDrop(nint hwnd);
	[DllImport("ole32.dll")] static extern void ReleaseStgMedium(ref STGMEDIUM medium);
	[DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint DragQueryFileW(nint hDrop, uint iFile, StringBuilder lpszFile, uint cch);
}
