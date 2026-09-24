using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class TabsView : VBoxContainer
{
	[Export] TabBar tabBar;
	[Export] Control tabView;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		tabBar.TabSelected += OnTabSelected;
		tabBar.TabClosePressed += OnTabClosePressed;
	}

    void OnTabSelected(long tab)
    {
		if (tabView.GetChildCount() > 0 && tab > -1)
		if (ReferenceEquals((Control)tabBar.GetTabMetadata((int)tab), tabView.GetChild(0))) return;

		HideCurrentTab();

		if (tab == -1) return;

		tabView.AddChild((Control)tabBar.GetTabMetadata((int)tab));
    }

	void OnTabClosePressed(long tab)
    {
		tabBar.RemoveTab((int)tab);
        HideCurrentTab();

		// select different tab if there is one
		if (tabBar.TabCount > 1) tabBar.CurrentTab = 0;
		else tabBar.CurrentTab = -1;
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
}
