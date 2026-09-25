using System;
using Godot;

namespace EditSharpGUI.Scripts.UI.ContextMenu.Platform;

public abstract class PlatformHandler
{
    public abstract void HandleMenu(ContextMenu menu, Vector2? position = null);

    // for previews: the screen rect of the open popup, when the platform can tell. safe from any thread
    public virtual Rect2I? OpenMenuRect() => null;

    // for previews: closes the open popup as a cancel would. safe from any thread
    public virtual void Dismiss() { }

    // for previews: moves the keyboard highlight down one item, as the down arrow would. safe from any thread
    public virtual void HighlightNext() { }
}
