using EditSharpGUI.Scripts.UI.ContextMenu;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// a bar of tabs over one view at a time. it knows nothing about what the
// views are: whoever puts them in (Editor) answers the requests that reach
// past this bar - moving a tab to the other side, opening a view that is
// closed, resetting the layout
public partial class TabsView : VBoxContainer
{
	[Export] TabBar tabBar;
	[Export] Control tabView;
	[Export] Button options;
	[Export] ContextMenu menu;

	// a tab was closed here; the view is out of the tree but not freed
	public event Action<Control> TabClosed;
	public event Action<Control> MoveToOtherSideRequested;
	public event Action<string> OpenRequested;
	public event Action ResetLayoutRequested;
	public event Action<Control> FloatRequested;

	// the views that could be opened from here, by id and title; set by the page
	public Func<IEnumerable<(string Id, string Title)>> AvailableViews;

	public override void _Ready()
	{
		tabBar.TabSelected += OnTabSelected;
		tabBar.TabClosePressed += tab => CloseTab((int)tab);
		tabBar.GuiInput += OnTabBarInput;

		if (options is not null) options.Pressed += () => ShowMenu(tabBar.CurrentTab, options);
	}

	void OnTabBarInput(InputEvent _)
	{
		ContextTrigger.Handle(tabBar, at =>
		{
			int tab = tabBar.GetTabIdxAtPoint(tabBar.GetLocalMousePosition());
			ShowMenu(tab, null);
		});
	}

	void OnTabSelected(long tab)
	{
		if (tabView.GetChildCount() > 0 && tab > -1)
		if (ReferenceEquals((Control)tabBar.GetTabMetadata((int)tab), tabView.GetChild(0))) return;

		HideCurrentTab();

		if (tab == -1) return;

		tabView.AddChild((Control)tabBar.GetTabMetadata((int)tab));
	}

	// the views in this bar, in tab order
	public IEnumerable<Control> Views
	{
		get
		{
			for (int i = 0; i < tabBar.TabCount; i++) yield return (Control)tabBar.GetTabMetadata(i);
		}
	}

	public bool Holds(Control view) => Views.Contains(view);

	public Control Current => tabBar.CurrentTab >= 0 ? (Control)tabBar.GetTabMetadata(tabBar.CurrentTab) : null;

	void CloseTab(int tab)
	{
		if (tab < 0 || tab >= tabBar.TabCount) return;

		Control view = RemoveView(tab);
		TabClosed?.Invoke(view);
	}

	// takes a view out of the bar without freeing it, and shows a neighbour
	public Control RemoveView(Control view)
	{
		for (int i = 0; i < tabBar.TabCount; i++)
		{
			if (ReferenceEquals((Control)tabBar.GetTabMetadata(i), view)) return RemoveView(i);
		}

		return null;
	}

	Control RemoveView(int tab)
	{
		Control view = (Control)tabBar.GetTabMetadata(tab);
		bool current = tabBar.CurrentTab == tab;

		tabBar.RemoveTab(tab);

		if (current)
		{
			HideCurrentTab();
			tabBar.CurrentTab = tabBar.TabCount > 0 ? Mathf.Min(tab, tabBar.TabCount - 1) : -1;
			OnTabSelected(tabBar.CurrentTab);
		}

		return view;
	}

    public void AddTab(Control c, Texture2D icon = null)
	{
		// do not allow duplicate tabs
		for (int i = 0; i < tabBar.TabCount; i++)
		{
			if (ReferenceEquals((Control)tabBar.GetTabMetadata(i), c)) return;
		}

		tabBar.AddTab(c.Name, icon);
		tabBar.SetTabMetadata(tabBar.TabCount - 1, c);

		tabBar.CurrentTab = tabBar.TabCount - 1;
	}

	void HideCurrentTab()
	{
		if (tabView.GetChildCount() <= 0) return;
		tabView.RemoveChild(tabView.GetChild(0));
	}

	// ---- the options menu ----

	// `tab` is the tab the menu is about: the one right-clicked, or the
	// current one from the options button; -1 when there is none
	void ShowMenu(int tab, Control anchor)
	{
		if (menu is null) return;

		ContextMenu shown = menu.Clone();
		Control view = tab >= 0 && tab < tabBar.TabCount ? (Control)tabBar.GetTabMetadata(tab) : null;

		Wire(shown, "tabs.close", view is not null, () => CloseTab(tab));
		Wire(shown, "tabs.closeOthers", view is not null && tabBar.TabCount > 1, () =>
		{
			foreach (Control other in Views.Where(v => v != view).ToList()) TabClosed?.Invoke(RemoveView(other));
		});
		Wire(shown, "tabs.moveSide", view is not null && MoveToOtherSideRequested is not null, () => MoveToOtherSideRequested?.Invoke(view));
		Wire(shown, "tabs.float", view is not null, () => FloatRequested?.Invoke(view));
		Wire(shown, "tabs.reset", ResetLayoutRequested is not null, () => ResetLayoutRequested?.Invoke());

		if (shown.Find<ContextSubmenu>("tabs.open") is ContextSubmenu open)
		{
			open.Elements.Clear();

			foreach ((string id, string title) in AvailableViews?.Invoke() ?? [])
			{
				ContextButton button = new() { Id = $"tabs.open.{id}", Text = new(title) };
				string captured = id;
				button.Pressed += () => OpenRequested?.Invoke(captured);
				open.Elements.Add(button);
			}

			if (open.Elements.Count == 0) open.Elements.Add(new ContextText("Every view is open"));
		}

		if (anchor is not null) ContextMenus.ShowContextMenu(shown, anchor);
		else ContextMenus.ShowContextMenu(shown);
	}

	static void Wire(ContextMenu menu, string id, bool enabled, Action action)
	{
		if (menu.Find<ContextButton>(id) is not ContextButton button) return;
		button.Enabled = enabled;
		button.Pressed += () => action();
	}
}
