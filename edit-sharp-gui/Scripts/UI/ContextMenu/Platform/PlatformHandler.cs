using System;
using System.Collections.Generic;
using Godot;

namespace EditSharpGUI.Scripts.UI.ContextMenu.Platform;

public abstract class PlatformHandler
{
    // owner: the OS window the menu belongs to, null for none (the tray). at: screen pixels, null for the cursor
    public abstract void HandleMenu(ContextMenu menu, Window owner, Vector2I? at);

    // whether HandleBar moves between a bar's menus itself as the pointer crosses their buttons
    public virtual bool SwitchesBarMenus => false;

    // a menu bar's menus with `index` opening; a handler that can't switch between them just shows that one
    public virtual void HandleBar(IReadOnlyList<BarMenu> menus, int index, Window owner) => HandleMenu(menus[index].Menu, owner, menus[index].At);

    // for previews: the screen rect of the open popup, when the platform can tell. safe from any thread
    public virtual Rect2I? OpenMenuRect() => null;

    // for previews: closes the open popup as a cancel would. safe from any thread
    public virtual void Dismiss() { }

    // for previews: moves the keyboard highlight down one item, as the down arrow would. safe from any thread
    public virtual void HighlightNext() { }

    // for previews: picks the highlighted item, as enter would. safe from any thread
    public virtual void ActivateHighlighted() { }
}
