using Godot;

namespace EditSharpGUI.Scripts.UI.ContextMenu.Platform;

// one of a menu bar's menus: where it opens and where its button is, in screen pixels
public sealed record BarMenu(ContextMenu Menu, Vector2I At, Rect2I Button);
