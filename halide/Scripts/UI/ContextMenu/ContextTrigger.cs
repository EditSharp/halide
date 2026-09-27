using System;
using Halide.Scripts.Input;
using Godot;

namespace Halide.Scripts.UI.ContextMenu;

// the right-click gesture, the way the rest of the app reads the mouse: the
// press is captured by the control under it, and the menu opens on the
// release, so the native popup never swallows a release the app is waiting
// for. a child that captured the press keeps its parent from opening a menu
// of its own, since a captured button refuses a second captor
public static class ContextTrigger
{
    // call from _GuiInput; `show` gets the cursor position when a right click completes here
    public static void Handle(Node node, Action<Vector2> show)
    {
        MouseButtonState right = InputManager.Singleton.Mouse.RightButton;

        switch (right.Action)
        {
            case MouseAction.Press:
            case MouseAction.DoubleClick:
                right.Capture(node);
                break;

            case MouseAction.Click:
                if (!right.HasCapture(node)) break;
                show(InputManager.Singleton.Mouse.CurrentPosition);
                break;
        }
    }
}
