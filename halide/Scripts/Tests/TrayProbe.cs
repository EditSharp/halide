using Halide.Scripts.App.Platform;
using Halide.Scripts.UI.ContextMenu;
using Halide.Scripts.UI.ContextMenu.Platform.Windows;
using Godot;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

// the tray menu with the root hidden and another app in front; prints TRAY OK
public partial class TrayProbe : Node
{
	bool ok = true;

	void Check(bool condition, string what)
	{
		ok &= condition;
		GD.Print($"PROBE {(condition ? "ok  " : "BAD ")} {what}");
	}

	public override async void _Ready()
	{
		Window root = GetTree().Root;
		root.GuiEmbedSubwindows = false;
		HostWindow.Hide(root);

		// somewhere harmless to click that isn't the menu
		Window target = new() { Title = "Tray probe target", Size = new Vector2I(360, 240), Position = new Vector2I(120, 120), Unfocusable = true };
		AddChild(target);
		await Frames(10);

		string picked = null;
		ContextMenu Menu()
		{
			ContextButton home = new() { Id = "tray.home", Text = new("Home") };
			home.Pressed += () => picked = "home";
			ContextButton quit = new() { Id = "tray.quit", Text = new("Quit") };
			quit.Pressed += () => picked = "quit";
			return new ContextMenu { Elements = [home, new ContextDivider(), quit] };
		}

		// a row picked with the mouse
		await Open(Menu());
		Rect2I? menu = ContextMenus.Handler.OpenMenuRect();
		Check(menu is not null, "the menu opens with another app in front");
		Check(GetForegroundWindow() == MenuThread.Helper, "and the menu thread has the foreground, so the menu owns the mouse");
		if (menu is Rect2I rect) await Click(rect.Position.X + 40, rect.Position.Y + 14);
		await Frames(20);
		Check(picked == "home" && ContextMenus.Handler.OpenMenuRect() is null, $"a click on a row picks it and closes the menu: {picked ?? "nothing"}");

		// a click elsewhere closes it, and the click isn't swallowed
		picked = null;
		await Open(Menu());
		Check(ContextMenus.Handler.OpenMenuRect() is not null, "it opens again");
		Vector2I inside = target.Position + target.Size / 2;
		await Click(inside.X, inside.Y);
		double until = Godot.Time.GetTicksMsec() + 3000;
		while (ContextMenus.Handler.OpenMenuRect() is not null && Godot.Time.GetTicksMsec() < until) await Frames(1);
		Check(ContextMenus.Handler.OpenMenuRect() is null && picked is null, "a click elsewhere closes it without a pick");

		// leftovers that would hold the mouse
		if (ContextMenus.Handler.OpenMenuRect() is not null) ContextMenus.Handler.Dismiss();
		await Frames(10);

		GD.Print(ok ? "TRAY OK" : "TRAY FAILED");
		GetTree().Quit();
	}

	// as the tray has it: the taskbar in front, the cursor near it, then the menu
	async Task Open(ContextMenu menu)
	{
		nint taskbar = FindWindowW("Shell_TrayWnd", null);
		SetForegroundWindow(taskbar);
		await Frames(5);

		Rect2I usable = DisplayServer.ScreenGetUsableRect(DisplayServer.GetPrimaryScreen());
		SetCursorPos(usable.End.X - 200, usable.End.Y - 20);
		ContextMenus.ShowContextMenu(menu, from: null);

		double until = Godot.Time.GetTicksMsec() + 5000;
		while (ContextMenus.Handler.OpenMenuRect() is null && Godot.Time.GetTicksMsec() < until) await Frames(1);
		await Frames(10);
	}

	async Task Click(int x, int y)
	{
		SetCursorPos(x, y);
		await Frames(3);
		mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
		await Frames(3);
		mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
	}

	async Task Frames(int count)
	{
		for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
	[DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
	[DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, nuint extra);
	[DllImport("user32.dll")] static extern nint GetForegroundWindow();
	[DllImport("user32.dll")] static extern bool SetForegroundWindow(nint window);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint FindWindowW(string className, string windowName);
}
