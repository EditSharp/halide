using System;
using EditSharpGUI.Scripts.UI.ContextMenu.Platform;
using EditSharpGUI.Scripts.UI.ContextMenu.Platform.GodotDrawn;
using EditSharpGUI.Scripts.UI.ContextMenu.Platform.MacOS;
using EditSharpGUI.Scripts.UI.ContextMenu.Platform.Windows;
using Godot;

namespace EditSharpGUI.Scripts.UI.ContextMenu;

public static class ContextMenus
{
    // the handler for this OS. EDITSHARP_MENU_HANDLER=godot forces the godot-drawn menu anywhere, to try it
    public static PlatformHandler Handler { get; } = Create();

    static PlatformHandler Create()
    {
        if (OS.GetEnvironment("EDITSHARP_MENU_HANDLER") == "godot") return new GodotHandler();
        return OS.GetName() switch
        {
            "Windows" => new WindowsHandler(),
            "macOS" => new MacOSHandler(),
            _ => new GodotHandler(),
        };
    }

    // position is in main window (viewport) pixels; null opens at the cursor.
    // the menu opens once the event that asked for it has finished dispatching
    public static void ShowContextMenu(ContextMenu menu, Vector2? position = null)
    {
        ArgumentNullException.ThrowIfNull(menu);
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
