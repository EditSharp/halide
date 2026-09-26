using System;
using System.Collections.Generic;
using EditSharpGUI.Scripts.Input;
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

    // the menu's top-left at the button's bottom-left corner, like a dropdown
    public static void ShowContextMenu(ContextMenu menu, Control button)
    {
        ArgumentNullException.ThrowIfNull(button);
        Rect2 rect = button.GetGlobalRect();
        ShowContextMenu(menu, new Vector2(rect.Position.X, rect.End.Y));
    }

    // the first key bound to a shortcut action, as a hint: "Ctrl+X"; null when none is
    public static ContextText Hint(string action)
    {
        IReadOnlyList<KeyCombo> combos = InputManager.Singleton?.Keyboard.Shortcuts.Get(action) ?? [];
        return combos.Count == 0 ? null : new ContextText(combos[0].ToString());
    }

    // the hint on a button, from the shortcut map
    public static void SetHint(ContextBaseButton button, string action)
    {
        if (button is not null) button.ShortcutHint = Hint(action);
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
