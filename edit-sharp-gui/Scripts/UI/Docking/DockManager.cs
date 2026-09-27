using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace EditSharpGUI.Scripts.UI.Docking;

// one window's views and where they sit: its own dock tree, the floats it owns, and the closed views parked out of sight
[GlobalClass]
public partial class DockManager : Node
{
	[Export] DockArea main;

	// closed views wait here, hidden but alive
	[Export] Control parking;

	// the layout a reset goes back to
	public Func<DockTree> DefaultLayout;

	// where a view with no home opens: beside a view, or null for the largest pane
	public Func<string, (string Beside, DockSide Side)?> DefaultSpot;

	public event Action<DockFloatWindow> FloatOpened;

	readonly Dictionary<string, (string Title, Control View)> views = [];
	readonly List<(DockTree Tree, DockFloatWindow Window)> floats = [];
	readonly Dictionary<string, DockHome> homes = [];
	DockTree tree = new();

	public override void _Ready() => Wire(main);

	public void Register(string id, string title, Control view)
	{
		views[id] = (title, view);
		Park(view);
	}

	public bool IsOpen(string id) => TreeOf(id) is not null;

	// anything but a float's only view, which is floating already
	public bool CanFloat(string id) => TreeOf(id) is not DockTree from || from == tree || from.Views.Count() > 1;

	// ---- what the user asks for ----

	// brings a view back: where it last sat, or its default spot
	public void Open(string id, DockStack into = null)
	{
		if (!views.ContainsKey(id)) return;

		if (TreeOf(id) is DockTree open)
		{
			DockStack stack = open.StackOf(id);
			stack.Current = id;
		}
		else if (into is not null) TreeHolding(into).Insert(id, into, DockSide.Center);
		else Dock(id);

		Refresh();
	}

	public void Close(string id)
	{
		Leave(id);
		Refresh();
	}

	// out into a window of its own, at `rect` in screen pixels, or over where it was
	public void Float(string id, Rect2I? rect = null)
	{
		if (!CanFloat(id)) return;
		rect ??= DefaultFloatRect(id);
		Leave(id);

		DockFloatWindow window = DockFloatWindow.Create(main.GetWindow(), rect.Value);
		DockTree floating = new(new DockStack([id]));
		floats.Add((floating, window));
		Wire(window.Area);
		window.Closing += DockBack;
		FloatOpened?.Invoke(window);
		Refresh();
	}

	public void Reset()
	{
		foreach ((DockTree _, DockFloatWindow window) in floats) window.QueueFree();
		floats.Clear();
		homes.Clear();
		tree = DefaultLayout?.Invoke() ?? new DockTree();
		Refresh();
	}

	// ---- the areas' requests ----

	void Wire(DockArea area)
	{
		area.Resolve = id => views[id];
		area.AvailableViews = () => views.Where(v => !IsOpen(v.Key)).Select(v => (v.Key, v.Value.Title));
		area.CanFloat = CanFloat;
		area.CloseRequested += Close;
		area.CloseOthersRequested += id =>
		{
			foreach (string other in TreeOf(id).StackOf(id).ViewIds.Where(v => v != id).ToList()) Leave(other);
			Refresh();
		};
		area.FloatRequested += id => Float(id);
		area.OpenRequested += (stack, id) => Open(id, stack);
		area.ResetRequested += Reset;
		area.TabSelected += (_, _) => Retitle();
		area.TabDragStarted += BeginDrag;
	}

	// a view's tab picked up; the drag follows the pointer until it's let go
	public void BeginDrag(string id)
	{
		DockDrag drag = new()
		{
			View = id,
			Title = views[id].Title,
			Areas = () => [.. floats.Select(f => f.Window.Area).Reverse(), main],
			FloatSize = DefaultFloatRect(id).Size,
			Floatable = CanFloat(id),
		};
		Dim(id, true);
		drag.Dropped += drop => { Dim(id, false); Move(id, drop); };
		drag.Floated += rect => { Dim(id, false); Float(id, rect); };
		drag.Cancelled += () => Dim(id, false);
		AddChild(drag);
	}

	void Dim(string id, bool dim)
	{
		main.Dim(id, dim);
		foreach ((DockTree _, DockFloatWindow window) in floats) window.Area.Dim(id, dim);
	}

	void Move(string id, DockDrop drop)
	{
		DockStack stack = drop.Stack;
		bool inStack = stack.ViewIds.Contains(id);

		// among its own tabs, a new place in the row
		if (inStack && drop.Side == DockSide.Center)
		{
			int from = stack.ViewIds.IndexOf(id);
			int to = drop.Index < 0 ? stack.ViewIds.Count : drop.Index;
			if (to > from) to--;
			stack.ViewIds.RemoveAt(from);
			stack.ViewIds.Insert(to, id);
			stack.Current = id;
			Refresh();
			return;
		}

		// splitting its own pane needs something left behind in it
		if (inStack && stack.ViewIds.Count == 1) return;

		Leave(id);
		drop.Area.Tree.Insert(id, stack, drop.Side, drop.Index);
		Refresh();
	}

	// a float closing: its views go back where they came from
	void DockBack(DockFloatWindow window)
	{
		int index = floats.FindIndex(f => f.Window == window);
		if (index < 0) return;

		DockTree floating = floats[index].Tree;
		floats.RemoveAt(index);
		foreach (string id in floating.Views.ToList()) Dock(id);
		window.QueueFree();
		Refresh();
	}

	// ---- the trees ----

	// out of whichever tree holds it, remembering where it sat in this window's own
	void Leave(string id)
	{
		DockTree from = TreeOf(id);
		if (from is null) return;

		DockHome home = from.Remove(id);
		if (from == tree && home is not null) homes[id] = home;
	}

	// into this window's tree: its home, its default spot, or the largest pane
	void Dock(string id)
	{
		if (homes.TryGetValue(id, out DockHome home) && tree.Return(id, home) is not null) return;

		if (DefaultSpot?.Invoke(id) is (string beside, DockSide side) && tree.StackOf(beside) is DockStack near)
		{
			tree.Insert(id, near, side);
			return;
		}

		tree.Insert(id, main.LargestStack ?? tree.Stacks.FirstOrDefault(), DockSide.Center);
	}

	DockTree TreeOf(string id) => tree.Contains(id) ? tree : floats.Select(f => f.Tree).FirstOrDefault(t => t.Contains(id));

	DockTree TreeHolding(DockStack stack) => tree.Stacks.Contains(stack) ? tree : floats.Select(f => f.Tree).First(t => t.Stacks.Contains(stack));

	// every area shown again, emptied floats closed, closed views parked
	void Refresh()
	{
		foreach ((DockTree floating, DockFloatWindow window) in floats.Where(f => f.Tree.Empty).ToList())
		{
			floats.Remove((floating, window));
			window.QueueFree();
		}

		main.Show(tree);
		foreach ((DockTree floating, DockFloatWindow window) in floats) window.Area.Show(floating);

		foreach ((string id, (string _, Control view)) in views)
			if (!IsOpen(id)) Park(view);

		Retitle();
	}

	void Retitle()
	{
		foreach ((DockTree floating, DockFloatWindow window) in floats)
			window.SetTitle(string.Join(", ", floating.Views.Select(v => views[v].Title)));
	}

	void Park(Control view)
	{
		if (view.GetParent() == parking) return;
		view.GetParent()?.RemoveChild(view);
		parking.AddChild(view);
	}

	// over the view's pane if it's showing, at least a comfortable size, in screen pixels
	Rect2I DefaultFloatRect(string id)
	{
		Window owner = main.GetWindow();
		float scale = owner.ContentScaleFactor;
		Vector2I minimum = (Vector2I)(new Vector2(480, 360) * scale);

		if (TreeOf(id)?.StackOf(id) is DockStack stack && TreeOf(id) == tree)
		{
			Rect2 pane = main.RectOf(stack);
			Vector2I position = owner.Position + (Vector2I)(pane.Position * scale) + new Vector2I(30, 30);
			return new Rect2I(position, ((Vector2I)(pane.Size * scale)).Max(minimum));
		}

		return new Rect2I(owner.Position + owner.Size / 2 - minimum / 2, minimum);
	}

	// ---- remembered with the project ----

	public JsonObject Save() => new()
	{
		["main"] = tree.Root?.ToJson(),
		["floats"] = new JsonArray([.. floats.Select(f => (JsonNode)new JsonObject
		{
			["x"] = f.Window.Position.X,
			["y"] = f.Window.Position.Y,
			["width"] = f.Window.Size.X,
			["height"] = f.Window.Size.Y,
			["layout"] = f.Tree.Root?.ToJson(),
		})]),
		["homes"] = new JsonObject(homes.Select(h => KeyValuePair.Create(h.Key, (JsonNode)new JsonObject
		{
			["side"] = h.Value.Side.ToString(),
			["ratio"] = h.Value.Ratio,
			["anchors"] = new JsonArray([.. h.Value.Anchors.Select(a => (JsonNode)a)]),
		}))),
	};

	// the saved layout, or the default when there's none
	public void Restore(JsonObject state)
	{
		if (state?["main"] is null)
		{
			Reset();
			return;
		}

		foreach ((DockTree _, DockFloatWindow window) in floats) window.QueueFree();
		floats.Clear();
		homes.Clear();

		HashSet<string> seen = [];
		tree = Known(new DockTree(DockNode.FromJson(state["main"])), seen);

		foreach (JsonNode saved in state["floats"]?.AsArray() ?? [])
		{
			DockTree floating = Known(new DockTree(DockNode.FromJson(saved?["layout"])), seen);
			if (floating.Empty) continue;

			Rect2I rect = new((int?)saved["x"] ?? 0, (int?)saved["y"] ?? 0, (int?)saved["width"] ?? 480, (int?)saved["height"] ?? 360);
			DockFloatWindow window = DockFloatWindow.Create(main.GetWindow(), rect);
			floats.Add((floating, window));
			Wire(window.Area);
			window.Closing += DockBack;
			FloatOpened?.Invoke(window);
		}

		foreach ((string id, JsonNode home) in state["homes"]?.AsObject() ?? [])
		{
			if (!views.ContainsKey(id) || home is null || !Enum.TryParse((string)home["side"], out DockSide side)) continue;
			homes[id] = new DockHome(side, (float?)home["ratio"] ?? 0.5f, [.. (home["anchors"]?.AsArray() ?? []).Select(a => (string)a)]);
		}

		Refresh();
	}

	// without views this window doesn't have, or that an earlier tree already holds
	DockTree Known(DockTree layout, HashSet<string> seen)
	{
		foreach (string id in layout.Views.ToList())
			if (!views.ContainsKey(id) || !seen.Add(id)) layout.Remove(id);

		return layout;
	}
}
