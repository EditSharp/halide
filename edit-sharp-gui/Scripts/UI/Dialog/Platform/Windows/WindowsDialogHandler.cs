using EditSharpGUI.Scripts.UI.ContextMenu;
using EditSharpGUI.Scripts.UI.Theming;
using Godot;
using System.Threading.Tasks;

namespace EditSharpGUI.Scripts.UI.Dialogs.Platform.Windows;

// windows dialogs: real dialog boxes built on the menu thread (NativeDialog), dark or
// light with the app. the window that asked is disabled while one is up, and godot keeps drawing
public sealed class WindowsDialogHandler : DialogHandler
{
	public override Task<string> ShowAsync(Dialog dialog, Window owner)
	{
		Window native = ContextMenus.NativeWindowOf(owner);
		nint ownerWindow = native is not null ? (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, native.GetWindowId()) : 0;
		bool dark = (ThemeDB.GetProjectTheme() as EditSharpTheme)?.Palette?.Dark ?? false;

		// the window whose icon the dialog's title bar shows: its owner, or the app's main window
		nint iconWindow = ownerWindow != 0 ? ownerWindow : (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, (int)DisplayServer.MainWindowId);

		NativeDialog box = new(ownerWindow, dark, iconWindow);
		DialogSession session = new(dialog, box.Refresh, box.SetProblem);

		box.Edited += session.Edited;
		box.ListPressed += session.ListPressed;
		box.BrowseRequested += session.Browse;
		box.Closed += session.Closed;

		box.Open(DialogSnapshot.Of(dialog));
		return session.Answer;
	}
}
