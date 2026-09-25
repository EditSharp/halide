using System;
using Godot;

namespace EditSharpGUI.Scripts.UI.ContextMenu.Platform;

public abstract class PlatformHandler
{
    public abstract void HandleMenu(ContextMenu menu, Vector2? position = null);
}
