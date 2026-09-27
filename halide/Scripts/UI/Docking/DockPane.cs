using Halide.Scripts.UI.ContextMenu;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halide.Scripts.UI.Docking;

// one tab stack of a dock layout: a strip of tabs over the views, only the current one showing and sizing the pane.
// knows nothing of the layout; its requests go to whoever built it; laid out in DockPane.tscn
[GlobalClass]
public partial class DockPane : VBoxContainer
{
	[Export] Control strip;
	[Export] TabBar tabBar;
	[Export] Container content;
	[Export] Button options;
	[Export] ContextMenu.ContextMenu menu;

	// how far a pressed tab moves before it's dragged
	[Export] float dragThreshold = 9;

	public DockStack Stack { get; set; }

	public event Action<string> TabSelected;
	public event Action<string> TabDragStarted;
	public event Action<string> CloseRequested;
	public event Action<string> CloseOthersRequested;
	public event Action<string> FloatRequested;
	public event Action<string> OpenRequested;
	public event Action ResetRequested;

	// the views that could be opened here, by id and title
	public Func<IEnumerable<(string Id, string Title)>> AvailableViews;

	// whether a view may float out of here
	public Func<string, bool> CanFloat;

	readonly List<(string Id, Control View)> views = [];

	int pressedTab = -1;
	Vector2 pressedAt;

	public override void _Ready()
	{
		tabBar.TabSelected += tab => ShowTab((int)tab, notify: true);
		tabBar.TabClosePressed += tab => CloseRequested?.Invoke(views[(int)tab].Id);
		tabBar.GuiInput += OnTabBarInput;
		options.Pressed += () => ShowMenu(tabBar.CurrentTab, options);
	}

	// fills the pane with `shown`, taking each view from wherever it was
	public void Show(IReadOnlyList<(string Id, string Title, Control View)> shown, string current)
	{
		views.Clear();
		tabBar.ClearTabs();

		foreach ((string id, string title, Control view) in shown)
		{
			if (view.GetParent() != content)
			{
				view.GetParent()?.RemoveChild(view);
				content.AddChild(view);
			}

			views.Add((id, view));
			tabBar.AddTab(title);
		}

		int index = Math.Max(0, views.FindIndex(v => v.Id == current));
		if (views.Count > 0)
		{
			tabBar.SetBlockSignals(true);
			tabBar.CurrentTab = index;
			tabBar.SetBlockSignals(false);
			ShowTab(index, notify: false);
		}
	}

	// lets go of its views without freeing them
	public void Release()
	{
		foreach ((string _, Control view) in views) if (view.GetParent() == content) content.RemoveChild(view);
		views.Clear();
	}

	void ShowTab(int tab, bool notify)
	{
		for (int i = 0; i < views.Count; i++) views[i].View.Visible = i == tab;
		if (notify && tab >= 0 && tab < views.Count) TabSelected?.Invoke(views[tab].Id);
	}

	// ---- dragging a tab out ----

	void OnTabBarInput(InputEvent e)
	{
		ContextTrigger.Handle(tabBar, _ => ShowMenu(tabBar.GetTabIdxAtPoint(tabBar.GetLocalMousePosition()), null));

		if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
		{
			pressedTab = button.Pressed ? tabBar.GetTabIdxAtPoint(button.Position) : -1;
			pressedAt = button.Position;
		}
		else if (e is InputEventMouseMotion motion && pressedTab >= 0 && motion.Position.DistanceTo(pressedAt) > dragThreshold)
		{
			string id = views[pressedTab].Id;
			pressedTab = -1;
			TabDragStarted?.Invoke(id);
		}
	}

	// a tab being dragged away shows faded until it lands
	public void Dim(string id, bool dim)
	{
		int index = views.FindIndex(v => v.Id == id);
		if (index >= 0) tabBar.SetTabDisabled(index, dim);
	}

	// ---- where a dragged tab would land ----

	// the tabs and options for a float's bar to show in its place, filling it with the tabs from the left
	public Control LendStrip()
	{
		strip.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		strip.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		tabBar.TabAlignment = TabBar.AlignmentMode.Left;
		tabBar.ThemeTypeVariation = "TopBarTabs";
		return strip;
	}

	// in the window's coordinates, whatever scale the strip is drawn at
	public Rect2 StripRect => tabBar.GetGlobalTransform() * new Rect2(Vector2.Zero, tabBar.Size);
	public Rect2 ContentRect => content.GetGlobalRect();

	// the tab gap nearest `at` and the global x of its marker
	public int InsertionAt(Vector2 at, out float markerX)
	{
		Transform2D toGlobal = tabBar.GetGlobalTransform();
		float x = (toGlobal.AffineInverse() * at).X;

		for (int i = 0; i < tabBar.TabCount; i++)
		{
			Rect2 tab = tabBar.GetTabRect(i);
			if (x < tab.GetCenter().X)
			{
				markerX = (toGlobal * tab.Position).X;
				return i;
			}
		}

		Rect2 last = tabBar.TabCount > 0 ? tabBar.GetTabRect(tabBar.TabCount - 1) : new Rect2();
		markerX = (toGlobal * new Vector2(last.End.X, 0)).X;
		return tabBar.TabCount;
	}

	// ---- the options menu ----

	// `tab` is the one right-clicked, or the current one from the options button; -1 when there's none
	void ShowMenu(int tab, Control anchor)
	{
		if (menu is null) return;

		ContextMenu.ContextMenu shown = menu.Clone();
		string id = tab >= 0 && tab < views.Count ? views[tab].Id : null;

		Wire(shown, "tabs.close", id is not null, () => CloseRequested?.Invoke(id));
		Wire(shown, "tabs.closeOthers", id is not null && views.Count > 1, () => CloseOthersRequested?.Invoke(id));
		Wire(shown, "tabs.float", id is not null && (CanFloat?.Invoke(id) ?? true), () => FloatRequested?.Invoke(id));
		Wire(shown, "tabs.reset", true, () => ResetRequested?.Invoke());

		if (shown.Find<ContextSubmenu>("tabs.open") is ContextSubmenu open)
		{
			open.Elements.Clear();

			foreach ((string viewId, string title) in AvailableViews?.Invoke() ?? [])
			{
				ContextButton button = new() { Id = $"tabs.open.{viewId}", Text = new(title) };
				button.Pressed += () => OpenRequested?.Invoke(viewId);
				open.Elements.Add(button);
			}

			if (open.Elements.Count == 0) open.Elements.Add(new ContextText("Every view is open"));
		}

		if (anchor is not null) ContextMenus.ShowContextMenuBelow(shown, anchor);
		else ContextMenus.ShowContextMenu(shown, this);
	}

	static void Wire(ContextMenu.ContextMenu menu, string id, bool enabled, Action action)
	{
		if (menu.Find<ContextButton>(id) is not ContextButton button) return;
		button.Enabled = enabled;
		button.Pressed += () => action();
	}
}
