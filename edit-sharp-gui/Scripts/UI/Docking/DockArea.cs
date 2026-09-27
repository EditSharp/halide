using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EditSharpGUI.Scripts.UI.Docking;

// shows one window's dock tree as splitters and panes, and where a dragged tab would land in it.
// knows the views only through Resolve; the layout's owner answers its requests; laid out in DockArea.tscn
[GlobalClass]
public partial class DockArea : Control
{
	[Export] PackedScene paneScene;
	[Export] PackedScene splitterScene;
	[Export] Control tree;
	[Export] Panel preview;
	[Export] Panel marker;
	[Export] DockCompass compass;

	// a view's title and control, by id
	public Func<string, (string Title, Control View)> Resolve;

	// the views that could be opened, by id and title
	public Func<IEnumerable<(string Id, string Title)>> AvailableViews;

	public event Action<DockStack, string> TabSelected;
	public event Action<string> TabDragStarted;
	public event Action<string> CloseRequested;
	public event Action<string> CloseOthersRequested;
	public event Action<string> FloatRequested;
	public event Action<DockStack, string> OpenRequested;
	public event Action ResetRequested;

	// whether a view may float out of this area
	public Func<string, bool> CanFloat;

	// a lone pane's tab strip to show somewhere else, like a float's bar; null when there's none
	public Action<Control> ShowStrip;

	// a split was dragged to a new ratio
	public event Action LayoutEdited;

	readonly List<DockPane> panes = [];
	Control lentStrip;

	public DockTree Tree { get; private set; }

	// rebuilt from `layout`, the views moving into their new panes
	public void Show(DockTree layout)
	{
		Tree = layout;

		foreach (DockPane pane in panes) pane.Release();
		panes.Clear();

		// a strip shown elsewhere went with its pane
		if (IsInstanceValid(lentStrip))
		{
			ShowStrip?.Invoke(null);
			lentStrip.QueueFree();
		}
		lentStrip = null;
		foreach (Node child in tree.GetChildren())
		{
			tree.RemoveChild(child);
			child.QueueFree();
		}

		if (layout.Root is null) return;

		Control root = Build(layout.Root);
		root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		tree.AddChild(root);

		foreach (DockPane pane in panes)
			pane.Show([.. pane.Stack.ViewIds.Select(id => (id, Resolve(id).Title, Resolve(id).View))], pane.Stack.Current);

		if (ShowStrip is not null && panes.Count == 1)
		{
			lentStrip = panes[0].LendStrip();
			ShowStrip(lentStrip);
		}
	}

	Control Build(DockNode node)
	{
		if (node is DockSplit split)
		{
			DockSplitter splitter = splitterScene.Instantiate<DockSplitter>();
			splitter.Hold(split, Build(split.First), Build(split.Second));
			splitter.RatioChanged += () => LayoutEdited?.Invoke();
			return splitter;
		}

		DockStack stack = (DockStack)node;
		DockPane pane = paneScene.Instantiate<DockPane>();
		pane.Stack = stack;
		pane.AvailableViews = () => AvailableViews?.Invoke() ?? [];
		pane.CanFloat = id => CanFloat?.Invoke(id) ?? true;
		pane.TabSelected += id => { stack.Current = id; TabSelected?.Invoke(stack, id); };
		pane.TabDragStarted += id => TabDragStarted?.Invoke(id);
		pane.CloseRequested += id => CloseRequested?.Invoke(id);
		pane.CloseOthersRequested += id => CloseOthersRequested?.Invoke(id);
		pane.FloatRequested += id => FloatRequested?.Invoke(id);
		pane.OpenRequested += id => OpenRequested?.Invoke(stack, id);
		pane.ResetRequested += () => ResetRequested?.Invoke();
		panes.Add(pane);
		return pane;
	}

	public void Dim(string id, bool dim)
	{
		foreach (DockPane pane in panes) pane.Dim(id, dim);
	}

	// the biggest pane's stack, where a view with nowhere else to go joins
	public DockStack LargestStack => panes.OrderByDescending(p => p.Size.X * p.Size.Y).FirstOrDefault()?.Stack;

	// the pane showing `stack`, in global coordinates
	public Rect2 RectOf(DockStack stack) => panes.FirstOrDefault(p => p.Stack == stack)?.GetGlobalRect() ?? GetGlobalRect();

	// ---- a drag passing over ----

	// what a view let go at `at` (global) would do, shown as it would look
	public DockDrop Track(Vector2 at)
	{
		// a strip first: it may sit outside its pane, in a float's bar
		DockPane pane = panes.FirstOrDefault(p => p.StripRect.HasPoint(at)) ?? panes.FirstOrDefault(p => p.GetGlobalRect().HasPoint(at));
		if (pane is null)
		{
			Untrack();
			return null;
		}

		if (pane.StripRect.HasPoint(at))
		{
			compass.Visible = preview.Visible = false;
			int index = pane.InsertionAt(at, out float x);
			Rect2 strip = pane.StripRect;
			marker.GlobalPosition = new Vector2(Mathf.Round(x - marker.Size.X / 2), strip.Position.Y);
			marker.Size = new Vector2(marker.Size.X, strip.Size.Y);
			marker.Visible = true;
			return new DockDrop(this, pane.Stack, DockSide.Center, index);
		}

		marker.Visible = false;
		Rect2 content = pane.ContentRect;
		compass.ShowAt(content.GetCenter());

		DockCompassTile tile = compass.TileAt(at);
		if (tile is null)
		{
			preview.Visible = false;
			return null;
		}

		Rect2 whole = pane.GetGlobalRect();
		Vector2 half = whole.Size / 2;
		Rect2 shown = tile.Side switch
		{
			DockSide.Left => new Rect2(whole.Position, new Vector2(half.X, whole.Size.Y)),
			DockSide.Right => new Rect2(whole.Position + new Vector2(half.X, 0), new Vector2(half.X, whole.Size.Y)),
			DockSide.Top => new Rect2(whole.Position, new Vector2(whole.Size.X, half.Y)),
			DockSide.Bottom => new Rect2(whole.Position + new Vector2(0, half.Y), new Vector2(whole.Size.X, half.Y)),
			_ => whole,
		};
		preview.GlobalPosition = shown.Position;
		preview.Size = shown.Size;
		preview.Visible = true;
		return new DockDrop(this, pane.Stack, tile.Side);
	}

	public void Untrack()
	{
		compass.Visible = preview.Visible = marker.Visible = false;
		compass.Unlight();
	}
}
