using Halide.Scripts.App.Commands;
using Halide.Scripts.App.Layouts;
using Halide.Scripts.UI.Docking;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Halide.Tests;

// the presets and the app-wide layout store
[TestFixture]
public sealed class LayoutStoreTests
{
	static readonly string[] Known = [.. AppCommands.Views.Select(v => v.Id)];

	static JsonObject State(params string[] views) => new() { ["main"] = new DockStack([.. views]).ToJson(), ["floats"] = new JsonArray() };

	[SetUp]
	public void Fresh()
	{
		File.Delete(Godot.ProjectSettings.GlobalizePath(LayoutStore.FilePath));
		LayoutStore.Reload();
	}

	[Test]
	public void PresetsAreValidLayoutsOfKnownViews()
	{
		foreach (string name in LayoutPresets.Names)
		{
			JsonObject state = Assert.NotNull(LayoutPresets.State(name), name);
			DockTree tree = new(DockNode.FromJson(state["main"]));
			Assert.False(tree.Empty, $"{name} has views");
			Assert.Equal(tree.Views.Count(), tree.Views.Distinct().Count(), $"{name} holds each view once");
			foreach (string view in tree.Views) Assert.Contains(view, Known, $"{name}'s views are real");
			foreach (string needed in new[] { "timeline", "program", "inspector", "media" }) Assert.Contains(needed, tree.Views, $"{name} has the essentials");
		}
	}

	[Test]
	public void PresetsDifferFromEachOther()
	{
		string[] shapes = [.. LayoutPresets.Names.Select(n => LayoutPresets.State(n).ToJsonString())];
		Assert.Equal(shapes.Length, shapes.Distinct().Count());
	}

	[Test]
	public void NamesStartWithThePresetsInOrder()
	{
		LayoutStore.Add("Mine", State("media"));
		Assert.Sequence(["Editing", "Assembly", "Audio", "Mine"], LayoutStore.Names);
	}

	[Test]
	public void AddRejectsEmptyTakenAndPresetNames()
	{
		Assert.True(LayoutStore.Add("Mine", State("media")));
		Assert.False(LayoutStore.Add("Mine", State("media")), "taken");
		Assert.False(LayoutStore.Add("  ", State("media")), "empty");
		Assert.False(LayoutStore.Add("Editing", State("media")), "a preset's name");
		Assert.True(LayoutStore.Add("  Spaced  ", State("media")));
		Assert.Contains("Spaced", LayoutStore.Names, "names are trimmed");
	}

	[Test]
	public void PresetsCantBeRenamedOrDeleted()
	{
		Assert.False(LayoutStore.Rename("Editing", "Other"));
		Assert.False(LayoutStore.Delete("Audio"));
		Assert.Sequence(LayoutPresets.Names, LayoutStore.Names);
	}

	[Test]
	public void RenameAndDeleteUserLayouts()
	{
		LayoutStore.Add("Mine", State("media"));
		LayoutStore.Add("Yours", State("media"));
		Assert.False(LayoutStore.Rename("Mine", "Yours"), "can't take another's name");
		Assert.True(LayoutStore.Rename("Mine", "Ours"));
		Assert.True(LayoutStore.Delete("Yours"));
		Assert.Sequence(["Editing", "Assembly", "Audio", "Ours"], LayoutStore.Names);
	}

	[Test]
	public void RememberingAPresetAndResettingIt()
	{
		JsonObject changed = State("timeline", "media");
		LayoutStore.Remember("Editing", changed);
		Assert.Equal(changed.ToJsonString(), LayoutStore.Current("Editing").ToJsonString());
		Assert.Equal(LayoutPresets.State("Editing").ToJsonString(), LayoutStore.Saved("Editing").ToJsonString(), "the saved state is the shipped one");

		LayoutStore.Reset("Editing");
		Assert.Equal(LayoutPresets.State("Editing").ToJsonString(), LayoutStore.Current("Editing").ToJsonString());
	}

	[Test]
	public void RememberingAUserLayoutAndResettingIt()
	{
		LayoutStore.Add("Mine", State("media"));
		LayoutStore.Remember("Mine", State("timeline"));
		Assert.Equal(State("timeline").ToJsonString(), LayoutStore.Current("Mine").ToJsonString());
		LayoutStore.Reset("Mine");
		Assert.Equal(State("media").ToJsonString(), LayoutStore.Current("Mine").ToJsonString());
	}

	[Test]
	public void CurrentIsACopy()
	{
		LayoutStore.Current("Editing")["main"] = null;
		Assert.NotNull(LayoutStore.Current("Editing")["main"], "changing what came back changes nothing");
	}

	[Test]
	public void UnknownLayoutsHaveNoState()
	{
		Assert.Null(LayoutStore.Current("Nope"));
		Assert.Null(LayoutStore.Saved("Nope"));
		Assert.False(LayoutStore.Exists("Nope"));
	}

	[Test]
	public async Task LayoutsSurviveARestart()
	{
		LayoutStore.Add("Mine", State("media"));
		LayoutStore.Remember("Mine", State("timeline"));
		LayoutStore.Remember("Audio", State("program"));
		await TestApp.Frames(2);

		LayoutStore.Reload();
		Assert.Contains("Mine", LayoutStore.Names);
		Assert.Equal(State("timeline").ToJsonString(), LayoutStore.Current("Mine").ToJsonString());
		Assert.Equal(State("media").ToJsonString(), LayoutStore.Saved("Mine").ToJsonString());
		Assert.Equal(State("program").ToJsonString(), LayoutStore.Current("Audio").ToJsonString());
	}

	[Test]
	public async Task ABrokenFileIsIgnored()
	{
		string file = Godot.ProjectSettings.GlobalizePath(LayoutStore.FilePath);
		Directory.CreateDirectory(Path.GetDirectoryName(file)!);
		await File.WriteAllTextAsync(file, "{ not json");
		LayoutStore.Reload();
		Assert.Sequence(LayoutPresets.Names, LayoutStore.Names);
	}

	[Test]
	public void ChangedFiresForAddRenameDelete()
	{
		int changes = 0;
		void Count() => changes++;
		LayoutStore.Changed += Count;
		try
		{
			LayoutStore.Add("Mine", State("media"));
			LayoutStore.Rename("Mine", "Ours");
			LayoutStore.Delete("Ours");
		}
		finally { LayoutStore.Changed -= Count; }
		Assert.Equal(3, changes);
	}
}
