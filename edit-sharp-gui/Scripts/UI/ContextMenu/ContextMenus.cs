using System;
using EditSharpGUI.Scripts.UI.ContextMenu.Platform;
using Godot;

namespace EditSharpGUI.Scripts.UI.ContextMenu;

public static class ContextMenus
{
    // the native handler for this OS; null where none exists yet
    public static PlatformHandler Handler { get; } = OS.GetName() switch
    {
        "Windows" => new WindowsHandler(),
        _ => null,
    };

    // position is in main window (viewport) pixels; null opens at the cursor.
    // the menu opens once the event that asked for it has finished dispatching
    public static void ShowContextMenu(ContextMenu menu, Vector2? position = null)
    {
        ArgumentNullException.ThrowIfNull(menu);
        if (Handler is null) return;
        Callable.From(() => Handler.HandleMenu(menu, position)).CallDeferred();
    }

    public static ContextMenu Example { get; } = BuildExample();

    static ContextMenu BuildExample()
    {
        ContextButton cut = new() { Icon = new(), Text = new("Cut"), ShortcutHint = new("Ctrl+X") };
        ContextButton copy = new() { Icon = new(), Text = new("Copy"), ShortcutHint = new("Ctrl+C") };
        ContextButton paste = new() { Icon = new(), Text = new("Paste"), ShortcutHint = new("Ctrl+V") };
        ContextButton linked = new() { Text = new("Linked"), Checked = false, Type = ContextButton.CheckType.Check };

        ContextRadioList colors = new()
        {
            Buttons = [
                new() { Icon = new(), Text = new("Red") },
                new() { Icon = new(), Text = new("Yellow") },
                new() { Icon = new(), Text = new("Blue") },
            ]
        };

        ContextMenu menu = new()
        {
            Elements = [
                cut,
                copy,
                paste,
                new ContextDivider(),
                linked,
                new ContextDivider(),
                new ContextSubmenu()
                {
                    Icon = new(), // Clip's current color
                    Text = new("Clip Color"),
                    Elements = [colors]
                }
            ]
        };

        cut.Pressed += () => GD.Print("Cut");
        copy.Pressed += () => GD.Print("Copy");
        paste.Pressed += () => GD.Print("Paste");
        linked.Pressed += () => GD.Print($"Linked: {linked.Checked}");
        colors.Selected += button => GD.Print($"Color: {button.Text.Text}");
        menu.Closed += () => GD.Print("Closed");

        return menu;
    }
}
