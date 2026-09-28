using Halide.Scripts.UI.Docking;
using System.Linq;
using System.Text.Json.Nodes;

namespace Halide.Tests;

// the dock layout model: inserting, removing, going back home, and JSON
[TestFixture]
public sealed class DockTreeTests
{
	static DockTree Editing() => new(new DockSplit(true, 0.6f,
		new DockSplit(false, 0.25f, new DockStack(["media"]), new DockSplit(false, 0.7f, new DockStack(["program"]), new DockStack(["inspector"]))),
		new DockStack(["timeline"])));

	[Test]
	public void EmptyTreeTakesFirstViewAsItsRoot()
	{
		DockTree tree = new();
		Assert.True(tree.Empty);
		DockStack stack = tree.Insert("media", null, DockSide.Left);
		Assert.Equal(stack, tree.Root as DockStack);
		Assert.Sequence(["media"], tree.Views);
	}

	[Test]
	public void CenterInsertJoinsTheTabsAndBecomesCurrent()
	{
		DockTree tree = Editing();
		DockStack media = tree.StackOf("media");
		tree.Insert("source", media, DockSide.Center);
		Assert.Sequence(["media", "source"], media.ViewIds);
		Assert.Equal("source", media.Current);
	}

	[Test]
	public void CenterInsertHonoursTheIndex()
	{
		DockTree tree = Editing();
		DockStack media = tree.StackOf("media");
		tree.Insert("source", media, DockSide.Center, index: 0);
		tree.Insert("graph", media, DockSide.Center, index: 1);
		Assert.Sequence(["source", "graph", "media"], media.ViewIds);
	}

	[Test]
	public void SideInsertsSplitThePaneTheRightWay()
	{
		foreach (DockSide side in new[] { DockSide.Left, DockSide.Right, DockSide.Top, DockSide.Bottom })
		{
			DockTree tree = Editing();
			DockStack program = tree.StackOf("program");
			DockStack added = tree.Insert("source", program, side, ratio: 0.4f);
			DockSplit split = Assert.NotNull(added.Parent, $"{side}: the new pane sits in a split");

			Assert.Equal(side is DockSide.Top or DockSide.Bottom, split.Vertical, $"{side}: orientation");
			bool before = side is DockSide.Left or DockSide.Top;
			Assert.Equal(before ? (DockNode)added : program, split.First, $"{side}: order");
			Assert.Near(before ? 0.4 : 0.6, split.Ratio, 1e-5, $"{side}: the new pane's share");
		}
	}

	[Test]
	public void RemovingATabKeepsThePaneAndPicksANeighbour()
	{
		DockTree tree = Editing();
		DockStack media = tree.StackOf("media");
		tree.Insert("source", media, DockSide.Center);
		tree.Insert("graph", media, DockSide.Center);
		media.Current = "source";

		DockHome home = tree.Remove("source");
		Assert.Sequence(["media", "graph"], media.ViewIds);
		Assert.Equal("graph", media.Current, "the next tab takes over");
		Assert.Equal(DockSide.Center, home.Side);
		Assert.Sequence(["media", "graph"], home.Anchors);
	}

	[Test]
	public void RemovingALonePaneCollapsesItsSplit()
	{
		DockTree tree = Editing();
		tree.Remove("inspector");
		Assert.True(tree.StackOf("program").Parent.Second == tree.StackOf("program"), "program took the place of the split it shared");
		Assert.Count(3, tree.Stacks);

		tree.Remove("timeline");
		Assert.True(tree.Root is DockSplit { Vertical: false }, "the top row became the root");
	}

	[Test]
	public void RemovingTheLastViewEmptiesTheTree()
	{
		DockTree tree = new(new DockStack(["media"]));
		Assert.Null(tree.Remove("media"));
		Assert.True(tree.Empty);
	}

	[Test]
	public void RemovingAnUnknownViewDoesNothing()
	{
		DockTree tree = Editing();
		string before = tree.Root.ToJson().ToJsonString();
		Assert.Null(tree.Remove("nowhere"));
		Assert.Equal(before, tree.Root.ToJson().ToJsonString());
	}

	[Test]
	public void AViewGoesBackWhereItWas()
	{
		foreach (string view in new[] { "media", "program", "inspector", "timeline" })
		{
			DockTree tree = Editing();
			string before = tree.Root.ToJson().ToJsonString();
			DockHome home = tree.Remove(view);
			Assert.NotNull(tree.Return(view, home), $"{view} found its home");
			Assert.Equal(before, Normalized(tree), $"{view} is back exactly where it was");
		}
	}

	[Test]
	public void AViewCantGoBackWhenWhatItSatBesideIsGone()
	{
		DockTree tree = Editing();
		DockHome home = tree.Remove("inspector");
		tree.Remove("program");
		Assert.Null(tree.Return("inspector", home));
	}

	[Test]
	public void CommonFindsTheSmallestPartHoldingViews()
	{
		DockTree tree = Editing();
		Assert.Equal(tree.StackOf("program").Parent, tree.Common(["program", "inspector"]) as DockSplit);
		Assert.Equal(tree.Root, tree.Common(["media", "timeline"]));
		Assert.Equal((DockNode)tree.StackOf("media"), tree.Common(["media"]));
	}

	[Test]
	public void JsonRoundTripsExactly()
	{
		DockTree tree = Editing();
		tree.Insert("source", tree.StackOf("media"), DockSide.Center);
		string json = tree.Root.ToJson().ToJsonString();
		Assert.Equal(json, new DockTree(DockNode.FromJson(JsonNode.Parse(json))).Root.ToJson().ToJsonString());
	}

	[Test]
	public void JsonDropsEmptyStacksAndBadInput()
	{
		JsonObject split = new()
		{
			["type"] = "split",
			["first"] = new JsonObject { ["type"] = "stack", ["views"] = new JsonArray() },
			["second"] = new JsonObject { ["type"] = "stack", ["views"] = new JsonArray("media", "", "timeline"), ["current"] = 9 },
		};
		DockNode node = DockNode.FromJson(split);
		DockStack stack = Assert.NotNull(node as DockStack, "an empty half collapses away");
		Assert.Sequence(["media", "timeline"], stack.ViewIds, "blank ids are dropped");
		Assert.Equal("media", stack.Current, "an out of range current falls back to the first");

		Assert.Null(DockNode.FromJson(null));
		Assert.Null(DockNode.FromJson(JsonValue.Create(3)));
	}

	[Test]
	public void ViewsAndStacksReadInOrder()
	{
		DockTree tree = Editing();
		Assert.Sequence(["media", "program", "inspector", "timeline"], tree.Views);
		Assert.Sequence(["media", "program", "inspector", "timeline"], tree.Stacks.Select(s => s.ViewIds[0]));
	}

	// ratios come back as floats; compare the shape through JSON with them rounded
	static string Normalized(DockTree tree)
	{
		JsonNode json = tree.Root.ToJson();
		Round(json);
		return json.ToJsonString();

		static void Round(JsonNode n)
		{
			if (n is not JsonObject o) return;
			if (o["ratio"] is JsonNode r) o["ratio"] = System.Math.Round(r.GetValue<float>(), 4);
			Round(o["first"]);
			Round(o["second"]);
		}
	}
}
