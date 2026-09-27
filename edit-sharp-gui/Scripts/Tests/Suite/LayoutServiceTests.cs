using EditSharpGUI.Api;
using EditSharpGUI.Scripts.App.Layouts;
using EditSharpGUI.Scripts.UI.Docking;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace EditSharpGUI.Tests;

// a project window's views and layouts through the service and the dock manager
[TestFixture]
public sealed class LayoutServiceTests
{
	ProjectHandle project;
	LayoutService Layout => project.Layout;

	[SetUp]
	public async Task Open()
	{
		System.IO.File.Delete(Godot.ProjectSettings.GlobalizePath(LayoutStore.FilePath));
		LayoutStore.Reload();
		project = await TestApp.NewProjectAsync();
	}

	static string Arrangement(ProjectHandle p) => new DockTree(DockNode.FromJson(p.Layout.Capture()["main"])).Root.ToJson().ToJsonString();

	[Test]
	public void ANewProjectOpensInEditing()
	{
		Assert.Equal("Editing", Layout.Active);
		foreach (string view in new[] { "media", "program", "inspector", "timeline" }) Assert.True(Layout.IsOpen(view), view);
		Assert.False(Layout.IsOpen("source"));
		Assert.False(Layout.IsOpen("graph"));
	}

	[Test]
	public void EveryViewIsKnown()
	{
		Assert.Sequence(["media", "inspector", "graph", "source", "program", "timeline"], Layout.Views.Select(v => v.Id));
	}

	[Test]
	public void OpeningAndClosingViews()
	{
		Layout.Close("inspector");
		Assert.False(Layout.IsOpen("inspector"));
		Layout.Open("inspector");
		Assert.True(Layout.IsOpen("inspector"));
		Assert.False(Layout.IsFloating("inspector"), "it came back where it was");
	}

	[Test]
	public void TheSourceOpensLeftOfTheProgram()
	{
		Layout.Open("source");
		DockTree tree = new(DockNode.FromJson(Layout.Capture()["main"]));
		DockSplit split = tree.StackOf("source").Parent;
		Assert.True(split is { Vertical: false } && split.First == tree.StackOf("source") && split.Second.Views.Contains("program"));
	}

	[Test]
	public async Task FloatingAndClosingTheFloatDocksBack()
	{
		string before = Arrangement(project);
		Layout.Float("inspector");
		Assert.True(Layout.IsFloating("inspector"));
		await TestApp.Frames(2);

		DockFloatWindow floating = project.Window.GetChildren().OfType<DockFloatWindow>().Single();
		floating.EmitSignal(Godot.Window.SignalName.CloseRequested);
		await TestApp.Frames(2);
		Assert.False(Layout.IsFloating("inspector"));
		Assert.Equal(before, Arrangement(project));
	}

	[Test]
	public void AFloatsOnlyViewCantFloatAgain()
	{
		Layout.Float("media");
		DockManager docks = project.Editor.Docks;
		Assert.False(docks.CanFloat("media"));
		Assert.True(docks.CanFloat("timeline"));
		Layout.Float("media");
		Assert.Count(1, project.Window.GetChildren().OfType<DockFloatWindow>().Where(w => !w.IsQueuedForDeletion()));
	}

	[Test]
	public void DockingBesideEveryWay()
	{
		foreach (DockSide side in new[] { DockSide.Center, DockSide.Left, DockSide.Right, DockSide.Top, DockSide.Bottom })
		{
			Assert.True(Layout.Dock("inspector", "timeline", side), side.ToString());
			DockTree tree = new(DockNode.FromJson(Layout.Capture()["main"]));
			if (side == DockSide.Center) Assert.Equal(tree.StackOf("timeline"), tree.StackOf("inspector"), "tabbed together");
			else Assert.Equal(tree.StackOf("inspector").Parent, tree.StackOf("timeline").Parent, $"{side}: split beside it");
		}
		Assert.False(Layout.Dock("inspector", "inspector"), "not beside itself");
		Assert.False(Layout.Dock("inspector", "graph"), "not beside a closed view");
	}

	[Test]
	public void ApplyingEachPreset()
	{
		foreach (string preset in LayoutPresets.Names)
		{
			Assert.True(Layout.Apply(preset));
			Assert.Equal(preset, Layout.Active);
			Assert.Equal(new DockTree(DockNode.FromJson(LayoutPresets.State(preset)["main"])).Root.ToJson().ToJsonString(), Arrangement(project), preset);
		}
		Assert.False(Layout.Apply("Nope"));
	}

	[Test]
	public void RearrangingIsRememberedAndResettable()
	{
		Layout.Close("inspector");
		Assert.DoesNotContain("inspector", new DockTree(DockNode.FromJson(LayoutStore.Current("Editing")["main"])).Views, "the active layout remembered");
		Layout.ResetActive();
		Assert.True(Layout.IsOpen("inspector"));
		Assert.Equal(LayoutPresets.State("Editing").ToJsonString(), LayoutStore.Current("Editing").ToJsonString());
	}

	[Test]
	public void SavingRenamingAndDeletingUserLayouts()
	{
		Layout.Close("media");
		Assert.True(Layout.SaveAsNew("No Media"));
		Assert.Equal("No Media", Layout.Active);
		Assert.False(Layout.SaveAsNew("No Media"), "taken");

		Assert.True(Layout.RenameActive("Media Free"));
		Assert.Contains("Media Free", Layout.Layouts);

		Layout.Apply("Assembly");
		Assert.True(Layout.IsOpen("media"));
		Layout.Apply("Media Free");
		Assert.False(Layout.IsOpen("media"));

		Assert.True(Layout.DeleteActive());
		Assert.Equal("Editing", Layout.Active);
		Assert.DoesNotContain("Media Free", Layout.Layouts);
	}

	[Test]
	public void PresetsRefuseRenameAndDelete()
	{
		Assert.False(Layout.RenameActive("Mine"));
		Assert.False(Layout.DeleteActive());
		Assert.Equal("Editing", Layout.Active);
	}

	[Test]
	public void ChangedFires()
	{
		int changes = 0;
		void Count() => changes++;
		Layout.Changed += Count;
		try
		{
			Layout.Close("inspector");
			Layout.Apply("Audio");
		}
		finally { Layout.Changed -= Count; }
		Assert.True(changes >= 2);
	}

	[Test]
	public void LayoutNumbersFollowTheSwitcher()
	{
		Assert.True(project.Editor.Layouts.ApplyNumber(3));
		Assert.Equal("Audio", Layout.Active);
		Assert.False(project.Editor.Layouts.ApplyNumber(9), "no ninth layout");
		Assert.False(project.Editor.Layouts.ApplyNumber(0));
	}

	[Test]
	public void FloatsAreCapturedRelativeToTheWindow()
	{
		Layout.Float("inspector", new Godot.Rect2I(project.Window.Position + new Godot.Vector2I(100, 50), new Godot.Vector2I(480, 360)));
		JsonObject captured = Layout.Capture();
		JsonObject f = (JsonObject)captured["floats"]![0];
		Assert.Equal(100, (int)f["x"]);
		Assert.Equal(50, (int)f["y"]);
	}
}
