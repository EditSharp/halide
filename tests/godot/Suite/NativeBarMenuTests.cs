using Halide.Api;
using Halide.Scripts.App.Chrome;
using Halide.Scripts.UI.ContextMenu;
using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Halide.Tests;

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

	// a bar-mode Open() while the previous one hasn't fully closed yet (its clearing is a deferred callback)
	// is silently ignored, so retry rather than call it once and hope the timing landed
	static async Task<Rect2I?> OpenAndMeasure(BarMenus bar, string name)
	{
		double until = Godot.Time.GetTicksMsec() + 5000;
		while (ContextMenus.Handler.OpenMenuRect() is null && Godot.Time.GetTicksMsec() < until)
		{
			bar.Open(name);
			await TestApp.Frames(2);
		}
		Rect2I? rect = ContextMenus.Handler.OpenMenuRect();
		Assert.True(rect is not null, $"the {name} menu opens");
		return rect;
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
		RealInput.CursorPosition = RealInput.ScreenCenterOf(stops[0].Button);
		await TestApp.Frames(3);
		RealInput.Click();
		await TestApp.WaitUntil(() => ContextMenus.Handler.OpenMenuRect() is not null, "the first menu opens on a real click", 5);

		// forward across the bar, then back again: two passes, so a switch that only sometimes lands shows up
		List<(string, Control)> sweep = [.. stops, .. Enumerable.Reverse(stops)];
		foreach ((string name, Control button) in sweep)
		{
			await RealInput.MoveTo(RealInput.ScreenCenterOf(button));

			int expectedX = RealInput.ScreenCenterOf(button).X;
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
}
