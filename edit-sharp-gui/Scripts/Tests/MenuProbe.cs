using EditSharpGUI.Scripts.UI.ContextMenu;
using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// opens the media viewer's filter menu through this OS's native handler,
// checks godot keeps running frames while it is up, picks its first check
// item from the keyboard, and reports whether the viewer followed the pick
// while the menu stayed open
//
//   godot --path . res://Tools/Scenes/Tests/MenuProbe.tscn
public partial class MenuProbe : Node
{
	int frames;

	public override void _Process(double delta) => frames++;

	public override async void _Ready()
	{
		GetWindow().Size = new Vector2I(1600, 900);
		Node editor = GD.Load<PackedScene>("res://Scenes/Views/Editor.tscn").Instantiate();
		AddChild(editor);
		await Frames(60);

		// the frame rate this machine manages with nothing open, to judge the one with a menu open by
		int baselineFrom = frames;
		double baselineStart = Godot.Time.GetTicksMsec();
		while (Godot.Time.GetTicksMsec() - baselineStart < 1000) await Frames(1);
		int baseline = frames - baselineFrom;

		MediaViewer viewer = Find<MediaViewer>(editor).First();
		Button filter = Find<Button>(viewer).First(b => b.Name == "Filter");

		// --clip: the first clip's options button instead; --tile: the media tile's
		string[] args = OS.GetCmdlineUserArgs();
		if (args.Contains("--clip"))
		{
			UITimeline timeline = editor.GetNode<UITimeline>("VSplitContainer/Timeline");
			UIClipsView clipsView = (UIClipsView)timeline.Get("clipsView");
			// the topmost clip whose button is in view: the rows at the bottom sit under the scrollbar
			filter = clipsView.UIClips.Select(c => Find<Button>(c).First(b => b.Name == "Options"))
				.Where(b => b.IsVisibleInTree() && timeline.ViewContains(b.GetGlobalRect().GetCenter()))
				.OrderBy(b => b.GlobalPosition.Y).First();
		}
		else if (args.Contains("--tile"))
		{
			filter = Find<Button>(Find<UIMediaItem>(viewer).First()).First(b => b.Name == "Options");
		}
		int tilesBefore = Find<UIMediaItem>(viewer).Count(t => t.Visible && t.GetParent() is not null);

		// --cascade: open the filter menu, click a clip's options button while it is up (which dismisses
		// the menu; the click should open the clip's), dismiss that, then open the filter menu again
		if (args.Contains("--cascade"))
		{
			UITimeline timeline = editor.GetNode<UITimeline>("VSplitContainer/Timeline");
			UIClipsView clipsView = (UIClipsView)timeline.Get("clipsView");
			Button clipOptions = clipsView.UIClips.Select(c => Find<Button>(c).First(b => b.Name == "Options"))
				.Where(b => b.IsVisibleInTree() && timeline.ViewContains(b.GetGlobalRect().GetCenter()))
				.OrderBy(b => b.GlobalPosition.Y).First();

			bool allOk = true;

			async Task<bool> ClickAndCheck(Control button, string what)
			{
				Vector2I screen = DisplayServer.WindowGetPosition(GetWindow().GetWindowId()) + (Vector2I)(GetViewport().GetScreenTransform() * button.GetGlobalRect().GetCenter()).Round();
				SetCursorPos(screen.X, screen.Y);
				await Frames(3);
				mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
				await Frames(2);
				mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
				double t = Godot.Time.GetTicksMsec();
				while (Godot.Time.GetTicksMsec() - t < 700) await Frames(1);
				Rect2I? r = ContextMenus.Handler.OpenMenuRect();
				GD.Print($"MENU {what}: open={r is not null} rect={r}");
				return r is not null;
			}

			allOk &= await ClickAndCheck(filter, "filter first");
			Rect2I? firstRect = ContextMenus.Handler.OpenMenuRect();
			allOk &= await ClickAndCheck(clipOptions, "clip options while filter menu open");
			Rect2I? secondRect = ContextMenus.Handler.OpenMenuRect();
			bool different = firstRect != secondRect;
			GD.Print($"MENU the clip menu replaced the filter menu: {different}");
			allOk &= different;

			ContextMenus.Handler.Dismiss();
			await Frames(20);
			GD.Print($"MENU after dismiss open={ContextMenus.Handler.OpenMenuRect() is not null}");
			allOk &= ContextMenus.Handler.OpenMenuRect() is null;

			allOk &= await ClickAndCheck(filter, "filter again");
			ContextMenus.Handler.Dismiss();
			await Frames(10);
			allOk &= await ClickAndCheck(clipOptions, "clip options again");
			ContextMenus.Handler.Dismiss();
			await Frames(10);

			GD.Print(allOk ? "MENU OK" : "MENU FAIL");
			GetTree().Quit(allOk ? 0 : 1);
			return;
		}

		// --submenu: right-click empty viewer space, hover into the Filter submenu, click its first
		// item five times; a sampling thread records every change in the visible menu windows and
		// in the pixel under the clicked item, to catch flashes and submenus closing
		if (args.Contains("--submenu") || args.Contains("--repeat"))
		{
			bool top = args.Contains("--repeat");
			// --repeat opens the Filter button's menu, whose first row is a check; --submenu right-clicks empty space
			Control opener = top ? filter : Find<ScrollContainer>(viewer).First();
			Vector2 spot = top ? opener.GetGlobalRect().GetCenter() : opener.GetGlobalRect().Position + new Vector2(opener.Size.X - 40f, opener.Size.Y - 40f);
			Vector2I screenSpot = DisplayServer.WindowGetPosition(GetWindow().GetWindowId()) + (Vector2I)(GetViewport().GetScreenTransform() * spot).Round();
			SetCursorPos(screenSpot.X, screenSpot.Y);
			await Frames(3);
			mouse_event(top ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
			await Frames(2);
			mouse_event(top ? MOUSEEVENTF_LEFTUP : MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);

			double t0 = Godot.Time.GetTicksMsec();
			while (ContextMenus.Handler.OpenMenuRect() is null && Godot.Time.GetTicksMsec() - t0 < 5000) await Frames(1);
			Rect2I? main = ContextMenus.Handler.OpenMenuRect();
			GD.Print($"MENU viewer menu {main}");
			if (main is not Rect2I m) { GD.Print("MENU FAIL"); GetTree().Quit(1); return; }

			// --repeat: the first row; --submenu: Filter, the last row, and its first item
			Vector2I item;
			if (top) item = new(m.Position.X + 60, m.Position.Y + 16);
			else
			{
				SetCursorPos(m.Position.X + 40, m.End.Y - 16);
				double t1 = Godot.Time.GetTicksMsec();
				List<Rect2I> popups = [];
				while (Godot.Time.GetTicksMsec() - t1 < 3000)
				{
					await Frames(1);
					popups = VisiblePopups();
					if (popups.Count >= 2) break;
				}
				GD.Print($"MENU popups after hover: {string.Join(" ", popups)}");
				if (popups.Count < 2) { GD.Print("MENU FAIL"); GetTree().Quit(1); return; }

				Rect2I sub = popups.First(p => p != m);
				item = new(sub.Position.X + 60, sub.Position.Y + 16);
			}
			SetCursorPos(item.X, item.Y);
			await Frames(10);

			List<string> events = [];
			bool sampling = true;
			System.Threading.Thread sampler = new(() =>
			{
				string lastPopups = "";
				uint lastPixel = 0xFFFFFFFF;
				nint dc = GetDC(0);
				double start = Godot.Time.GetTicksMsec();
				while (sampling)
				{
					string now = string.Join(" ", VisiblePopups());
					if (now != lastPopups) { lock (events) events.Add($"{Godot.Time.GetTicksMsec() - start:0}ms popups {now}"); lastPopups = now; }
					uint pixel = GetPixel(dc, item.X - 40, item.Y);
					if (pixel != lastPixel) { lock (events) events.Add($"{Godot.Time.GetTicksMsec() - start:0}ms pixel {pixel:X6}"); lastPixel = pixel; }
					System.Threading.Thread.Sleep(1);
				}
				ReleaseDC(0, dc);
			}) { IsBackground = true };
			sampler.Start();

			for (int i = 0; i < 5; i++)
			{
				mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
				await Frames(2);
				mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
				double tc = Godot.Time.GetTicksMsec();
				while (Godot.Time.GetTicksMsec() - tc < 400) await Frames(1);
			}

			sampling = false;
			sampler.Join();
			foreach (string e in events) GD.Print($"MENU  {e}");
			int popupChanges = events.Count(e => e.Contains(" popups "));
			GD.Print(popupChanges <= 1 ? "MENU OK: the menu windows never changed" : $"MENU FAIL: the menu windows changed {popupChanges - 1} times");

			ContextMenus.Handler.Dismiss();
			await Frames(10);
			GetTree().Quit();
			return;
		}

		// --click: a real mouse click on the button through the OS, as a hand would do it
		if (OS.GetCmdlineUserArgs().Contains("--click"))
		{
			Vector2 centre = filter.GetGlobalRect().GetCenter();
			Vector2I screen = DisplayServer.WindowGetPosition(GetWindow().GetWindowId()) + (Vector2I)(GetViewport().GetScreenTransform() * centre).Round();
			SetCursorPos(screen.X, screen.Y);
			await Frames(5);
			GD.Print($"MENU clicking {filter.GetPath()} at {screen}, hovered={GetViewport().GuiGetHoveredControl()?.GetPath()}");
			filter.Pressed += () => GD.Print("MENU button pressed");
			mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
			if (!args.Contains("--fast")) await Frames(3);
			mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
		}
		else filter.EmitSignal(BaseButton.SignalName.Pressed);

		// the menu opens on a deferred call, and within a few seconds on the slowest machine;
		// then a second of frames while it is up, against a second of frames before it
		double waitFrom = Godot.Time.GetTicksMsec();
		while (ContextMenus.Handler.OpenMenuRect() is null && Godot.Time.GetTicksMsec() - waitFrom < 10000) await Frames(1);
		int at = frames;
		double started = Godot.Time.GetTicksMsec();
		while (Godot.Time.GetTicksMsec() - started < 1000) await Frames(1);
		int during = frames - at;
		Rect2I? rect = ContextMenus.Handler.OpenMenuRect();
		GD.Print($"MENU framesBefore={baseline} framesWhileOpen={during}");
		GD.Print($"MENU handler={ContextMenus.Handler.GetType().Name} open={rect is not null} framesWhileOpen={during}");

		// "Used in a Timeline" is the first item; unchecking it hides the used media.
		// --mousepick clicks the row through the OS instead of picking it from the keyboard
		if (args.Contains("--mousepick") && rect is Rect2I menuRect)
		{
			SetCursorPos(menuRect.Position.X + 40, menuRect.Position.Y + 14);
			await Frames(3);
			mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
			await Frames(2);
			mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
		}
		else
		{
			ContextMenus.Handler.HighlightNext();
			await Frames(5);
			ContextMenus.Handler.ActivateHighlighted();
		}
		// the pick goes through the menu's own thread or process first; a slow machine gets a while
		double pickFrom = Godot.Time.GetTicksMsec();
		await Frames(5);
		while (Find<UIMediaItem>(viewer).Count(t => t.GetParent() is not null) >= tilesBefore && Godot.Time.GetTicksMsec() - pickFrom < 10000) await Frames(1);
		await Frames(5);
		GD.Print($"MENU pick took {Godot.Time.GetTicksMsec() - pickFrom:0}ms");

		// a menu that stays open by showing again (macOS) takes a moment to be back
		double reopenFrom = Godot.Time.GetTicksMsec();
		while (ContextMenus.Handler.OpenMenuRect() is null && Godot.Time.GetTicksMsec() - reopenFrom < 5000) await Frames(1);
		Rect2I? afterPick = ContextMenus.Handler.OpenMenuRect();
		GD.Print($"MENU after pick: open={afterPick is not null} sameRect={afterPick == rect}");

		int tilesAfter = Find<UIMediaItem>(viewer).Count(t => t.GetParent() is not null);
		bool stillOpen = ContextMenus.Handler.OpenMenuRect() is not null;
		GD.Print($"MENU tilesBefore={tilesBefore} tilesAfter={tilesAfter} reopened={stillOpen}");

		ContextMenus.Handler.Dismiss();
		await Frames(10);

		bool filterMenu = !args.Contains("--clip") && !args.Contains("--tile");
		bool ok = rect is not null && during * 2 >= baseline && (!filterMenu || (tilesAfter < tilesBefore && stillOpen));
		GD.Print(ok ? "MENU OK" : "MENU FAIL");
		GetTree().Quit(ok ? 0 : 1);
	}

	const uint MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4, MOUSEEVENTF_RIGHTDOWN = 0x8, MOUSEEVENTF_RIGHTUP = 0x10;

	static List<Rect2I> VisiblePopups()
	{
		List<Rect2I> found = [];
		for (nint popup = FindWindowExW(0, 0, "#32768", null); popup != 0; popup = FindWindowExW(0, popup, "#32768", null))
		{
			if (!IsWindowVisible(popup) || !GetWindowRect(popup, out RECT r)) continue;
			found.Add(new Rect2I(r.left, r.top, r.right - r.left, r.bottom - r.top));
		}
		return found;
	}

	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
	struct RECT { public int left, top, right, bottom; }

	[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern nint FindWindowExW(nint parent, nint after, string cls, string name);
	[System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindowVisible(nint hwnd);
	[System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(nint hwnd, out RECT rect);
	[System.Runtime.InteropServices.DllImport("user32.dll")] static extern nint GetDC(nint hwnd);
	[System.Runtime.InteropServices.DllImport("user32.dll")] static extern int ReleaseDC(nint hwnd, nint dc);
	[System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern uint GetPixel(nint dc, int x, int y);
	[System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
	[System.Runtime.InteropServices.DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, nuint extra);

	async Task Frames(int count)
	{
		for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	static System.Collections.Generic.List<T> Find<T>(Node root) where T : Node
	{
		System.Collections.Generic.List<T> found = [];
		Collect(root, found);
		return found;
	}

	static void Collect<T>(Node node, System.Collections.Generic.List<T> into) where T : Node
	{
		if (node is T match) into.Add(match);
		foreach (Node child in node.GetChildren()) Collect(child, into);
	}
}
