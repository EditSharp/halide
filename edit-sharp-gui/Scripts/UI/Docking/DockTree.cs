using System.Collections.Generic;
using System.Linq;

namespace EditSharpGUI.Scripts.UI.Docking;

// one window's layout: a tree of splits over tab stacks, empty when every view has left it
public sealed class DockTree(DockNode root = null)
{
	public DockNode Root { get; private set; } = root;

	public bool Empty => Root is null;

	public IEnumerable<string> Views => Root?.Views ?? [];

	public IEnumerable<DockStack> Stacks => Root?.Stacks ?? [];

	public bool Contains(string view) => Views.Contains(view);

	public DockStack StackOf(string view) => Stacks.FirstOrDefault(s => s.ViewIds.Contains(view));

	// takes a view out, collapsing its pane if it was the last there; returns where it was
	public DockHome Remove(string view)
	{
		DockStack stack = StackOf(view);
		if (stack is null) return null;

		int index = stack.ViewIds.IndexOf(view);
		stack.ViewIds.RemoveAt(index);

		if (stack.ViewIds.Count > 0)
		{
			if (stack.Current == view) stack.Current = stack.ViewIds[System.Math.Min(index, stack.ViewIds.Count - 1)];
			return new DockHome(DockSide.Center, 0.5f, [.. stack.ViewIds]);
		}

		DockSplit parent = stack.Parent;
		if (parent is null)
		{
			Root = null;
			return null;
		}

		bool first = parent.First == stack;
		DockNode sibling = first ? parent.Second : parent.First;
		DockSide side = (parent.Vertical, first) switch
		{
			(false, true) => DockSide.Left,
			(false, false) => DockSide.Right,
			(true, true) => DockSide.Top,
			(true, false) => DockSide.Bottom,
		};
		DockHome home = new(side, first ? parent.Ratio : 1 - parent.Ratio, [.. sibling.Views]);

		// the sibling takes the parent's place
		if (parent.Parent is DockSplit grand) grand.Replace(parent, sibling);
		else
		{
			Root = sibling;
			sibling.Parent = null;
		}

		return home;
	}

	// puts a view among a stack's tabs at `index` (the end when -1), or splits `target` to hold it on `side`
	public DockStack Insert(string view, DockNode target, DockSide side, int index = -1, float ratio = 0.5f)
	{
		if (Root is null || target is null)
		{
			DockStack only = new([view]);
			Root = only;
			return only;
		}

		if (side == DockSide.Center && target is DockStack stack)
		{
			stack.ViewIds.Insert(index < 0 || index > stack.ViewIds.Count ? stack.ViewIds.Count : index, view);
			stack.Current = view;
			return stack;
		}

		DockStack added = new([view]);
		DockSplit parent = target.Parent;
		bool vertical = side is DockSide.Top or DockSide.Bottom;
		bool before = side is DockSide.Left or DockSide.Top;
		DockSplit split = before ? new(vertical, ratio, added, target) : new(vertical, 1 - ratio, target, added);

		if (parent is null)
		{
			Root = split;
			split.Parent = null;
		}
		else parent.Replace(target, split);

		return added;
	}

	// back where it was, if what it sat beside is still here
	public DockStack Return(string view, DockHome home)
	{
		if (home is null) return null;

		List<string> present = [.. home.Anchors.Where(Contains)];
		if (present.Count == 0) return null;

		if (home.Side == DockSide.Center) return Insert(view, StackOf(present[0]), DockSide.Center);
		return Insert(view, Common(present), home.Side, ratio: home.Ratio);
	}

	// the smallest part holding all of `views`
	public DockNode Common(IReadOnlyList<string> views)
	{
		DockNode node = StackOf(views[0]);
		while (node is not null && !views.All(v => node.Views.Contains(v))) node = node.Parent;
		return node ?? Root;
	}
}
