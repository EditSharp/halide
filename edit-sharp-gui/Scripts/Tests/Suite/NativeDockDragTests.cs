using EditSharpGUI.Api;
using EditSharpGUI.Scripts.UI.Docking;
using Godot;
using System.Linq;
using System.Threading.Tasks;

namespace EditSharpGUI.Tests;

// the same dock-drag scenarios as DockDragTests, but with the real OS pointer instead of an injected one:
// DockDrag.Pointer/Held are left at their default (OsPointer), so this exercises the production input path
[TestFixture]
[Windowed]
[OnlyOn("Windows")]
public sealed class NativeDockDragTests
{
	ProjectHandle project;
	Window Main => project.Window;

	[SetUp]
	public async Task Open()
	{
		project = await TestApp.NewProjectAsync();
		Rect2I usable = DisplayServer.ScreenGetUsableRect(Main.CurrentScreen);
		Main.Size = new Vector2I(System.Math.Min(1200, usable.Size.X - 96), System.Math.Min(720, usable.Size.Y - 96));
		Main.Position = usable.Position + (usable.Size - Main.Size) / 2;
		await TestApp.Seconds(0.5);
	}

	static DockPane PaneOf(Window at, string view) =>
		at.FindChildren("*", "", true, false).OfType<DockPane>().FirstOrDefault(p => p.Stack?.ViewIds.Contains(view) == true);

	async Task Pick(string view)
	{
		await RealInput.MoveTo(RealInput.ScreenPointIn(Main, PaneOf(Main, view).GetGlobalRect().GetCenter()));
		RealInput.MouseDown();
		project.Editor.Docks.BeginDrag(view);
		await TestApp.Frames(2);
	}

	async Task Release()
	{
		RealInput.MouseUp();
		await TestApp.Frames(3);
	}

	async Task OntoTile(Window at, string view, DockSide side)
	{
		await RealInput.MoveTo(RealInput.ScreenPointIn(at, PaneOf(at, view).GetGlobalRect().GetCenter() + new Vector2(0, 14)));
		await TestApp.Frames(3);
		DockCompassTile tile = at.FindChildren("*", "", true, false).OfType<DockCompassTile>().First(t => t.Side == side && t.IsVisibleInTree());
		await RealInput.MoveTo(RealInput.ScreenPointIn(at, tile.GetGlobalRect().GetCenter()));
		await TestApp.Frames(3);
	}

	DockTree Tree => new(DockNode.FromJson(project.Layout.Capture()["main"]));

	[Test(Timeout = 30)]
	public async Task DroppingOnASideTileSplitsThatPane()
	{
		await Pick("inspector");
		await OntoTile(Main, "program", DockSide.Left);
		Assert.True(Main.FindChildren("*", "", true, false).OfType<DockTabGhost>().Any(g => g.Visible), "a tab ghost follows the real pointer");
		await Release();

		DockStack inspector = Tree.StackOf("inspector");
		Assert.True(inspector.Parent is { Vertical: false } s && s.First == inspector && s.Second.Views.Contains("program"));
		Assert.False(Main.FindChildren("*", "", true, false).OfType<DockTabGhost>().Any(), "the ghost is gone");
	}

	[Test(Timeout = 30)]
	public async Task DroppingOnATabStripJoinsTheTabs()
	{
		await Pick("timeline");
		Rect2 strip = PaneOf(Main, "media").StripRect;
		await RealInput.MoveTo(RealInput.ScreenPointIn(Main, new Vector2(strip.End.X - 60, strip.GetCenter().Y)));
		await TestApp.Frames(3);
		await Release();
		Assert.Sequence(["media", "timeline"], Tree.StackOf("media").ViewIds);
	}

	[Test(Timeout = 30)]
	public async Task DroppingOutsideEveryWindowFloats()
	{
		await Pick("media");
		await RealInput.MoveTo(RealInput.ScreenPositionOf(Main) + Main.Size + new Vector2I(60, 60));
		await TestApp.Frames(3);
		await Release();
		Assert.True(project.Layout.IsFloating("media"));
	}
}
