using EditSharpGUI.Api;
using EditSharpGUI.Scripts.App.Chrome;
using EditSharpGUI.Scripts.UI.ContextMenu;
using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace EditSharpGUI.Tests;

// the bar's menus through the real Windows handler and real OS mouse input: each drops down under its own
// button, hovering across the bar switches reliably back and forth (including onto the layout switcher),
// and the app is still responding once it's done -- reproduces the hover-switch deadlock (decisions log)
[TestFixture]
[Windowed]
[OnlyOn("Windows")]
public sealed class NativeBarMenuTests
{
	[TearDown]
	public async Task CloseAnyMenu()
	{
		ContextMenus.Handler.Dismiss();
		await TestApp.Seconds(0.3);
	}

	static async Task<Rect2I?> OpenAndMeasure(BarMenus bar, string name)
	{
		bar.Open(name);
		await TestApp.WaitUntil(() => ContextMenus.Handler.OpenMenuRect() is not null, $"the {name} menu opens", 5);
		return ContextMenus.Handler.OpenMenuRect();
	}

	[Test(Timeout = 30)]
	public async Task EachMenuOpensUnderItsButtonAndCloses()
	{
		ProjectHandle project = await TestApp.NewProjectAsync();
		await TestApp.Seconds(0.5);
		BarMenus bar = project.Window.GetChildren().OfType<BarMenus>().First();

		Rect2I edit = (await OpenAndMeasure(bar, "Edit")).Value;
		ContextMenus.Handler.Dismiss();
		await TestApp.WaitUntil(() => ContextMenus.Handler.OpenMenuRect() is null, "it closes", 5);
		await TestApp.Seconds(0.2);

		Rect2I layouts = (await OpenAndMeasure(bar, BarMenus.LayoutsMenu)).Value;
		Assert.True(layouts.Position.X > edit.Position.X, "the layouts menu drops from its own button, right of Edit");
		ContextMenus.Handler.Dismiss();
		await TestApp.WaitUntil(() => ContextMenus.Handler.OpenMenuRect() is null, "it closes", 5);
	}

	// the button's centre, in real OS screen pixels, for SetCursorPos/mouse_event
	static Vector2I ScreenCenterOf(Control c) =>
		DisplayServer.WindowGetPosition(c.GetWindow().GetWindowId()) + (Vector2I)(c.GetViewport().GetScreenTransform() * c.GetGlobalRect().GetCenter()).Round();

	// a real mouse move in several steps, the way a hand crossing the bar generates many WM_MOUSEMOVE
	// messages rather than one jump; each step waits a frame so the OS and the menu thread both see it
	static async Task MoveMouseTo(Vector2I to, int steps = 8)
	{
		SetCursorPos(out Vector2I from);
		for (int i = 1; i <= steps; i++)
		{
			Vector2I at = from + (to - from) * i / steps;
			SetCursorPos(at.X, at.Y);
			await TestApp.Frames(1);
		}
	}

	static void SetCursorPos(out Vector2I at)
	{
		GetCursorPos(out POINT p);
		at = new Vector2I(p.x, p.y);
	}

	static void Click()
	{
		mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
		mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
	}

	[Test(Timeout = 60)]
	public async Task HoveringBackAndForthSwitchesReliablyIncludingTheLayoutSwitcher()
	{
		ProjectHandle project = await TestApp.NewProjectAsync();
		await TestApp.Seconds(0.5);
		UITopBar top = project.Window.Frame.Bar;

		// the real buttons, in bar order, ending with the layout switcher -- exactly what a hand sweeping
		// the bar left to right would cross, and what the old polling path never reached
		List<(string Name, Control Button)> stops =
		[
			.. top.FindChildren("*", "Button", true, false).OfType<Button>()
				.Where(b => b.Text is "File" or "Edit" or "View" or "Playback")
				.OrderBy(b => b.GetGlobalRect().Position.X)
				.Select(b => (b.Text, (Control)b)),
			(BarMenus.LayoutsMenu, top.LayoutsAnchor),
		];
		Assert.Count(5, stops);

		// open the first one with a real click, the way a hand would start
		SetCursorPos(ScreenCenterOf(stops[0].Button).X, ScreenCenterOf(stops[0].Button).Y);
		await TestApp.Frames(3);
		Click();
		await TestApp.WaitUntil(() => ContextMenus.Handler.OpenMenuRect() is not null, "the first menu opens on a real click", 5);

		// forward across the bar, then back again: two passes, so a switch that only sometimes lands shows up
		List<(string, Control)> sweep = [.. stops, .. Enumerable.Reverse(stops)];
		foreach ((string name, Control button) in sweep)
		{
			await MoveMouseTo(ScreenCenterOf(button));

			int expectedX = ScreenCenterOf(button).X;
			await TestApp.WaitUntil(
				() => ContextMenus.Handler.OpenMenuRect() is Rect2I r && System.Math.Abs(r.Position.X - expectedX) < 200,
				$"hovering onto {name} switches the open menu there", 3);
		}

		ContextMenus.Handler.Dismiss();
		await TestApp.WaitUntil(() => ContextMenus.Handler.OpenMenuRect() is null, "it closes", 5);

		// the app itself must still be alive: frames keep advancing and a fresh project opens normally
		int before = Engine.GetFramesDrawn();
		await TestApp.Seconds(0.5);
		Assert.True(Engine.GetFramesDrawn() > before, "frames are still advancing after the sweep");
		Assert.NotNull(await TestApp.NewProjectAsync(), "the app still responds to ordinary commands");
	}

	const uint MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4;

	[StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }

	[DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
	[DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
	[DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, nuint extra);
}
