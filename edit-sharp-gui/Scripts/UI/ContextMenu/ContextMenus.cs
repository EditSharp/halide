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
    // the handler for this OS. windows tracks native menus on a thread of their
    // own; macOS shows them from a helper process (Tools/MacOS), falling back
    // to tracking in this process when the helper isn't built.
    // EDITSHARP_MENU_HANDLER=godot forces the godot-drawn menu anywhere, and
    // =inprocess the in-process NSMenu on macOS
    // settable so tests can stand in for the OS
    public static PlatformHandler Handler { get; internal set; } = Create();

    static PlatformHandler Create()
    {
        string forced = OS.GetEnvironment("EDITSHARP_MENU_HANDLER");
        if (forced == "godot") return new GodotHandler();
        return OS.GetName() switch
        {
            "Windows" => new WindowsHandler(),
            "macOS" when forced != "inprocess" && MacOSProcessHandler.Available => new MacOSProcessHandler(),
            "macOS" => new MacOSHandler(),
            _ => new GodotHandler(),
        };
    }

    // the menu belongs to from's window (null for none: the tray). position is in
    // from's viewport pixels; null opens at the cursor. the menu opens once the
    // event that asked for it has finished dispatching
    public static void ShowContextMenu(ContextMenu menu, Node from, Vector2? position = null)
    {
        ArgumentNullException.ThrowIfNull(menu);
        Window owner = NativeWindowOf(from);
        Vector2I? at = position is Vector2 p && from?.GetViewport() is Viewport viewport ? ToScreen(viewport, p) : null;
        Callable.From(() => Handler.HandleMenu(menu, owner, at)).CallDeferred();
    }

    // the menu's top-left at the button's bottom-left corner, like a dropdown
    public static void ShowContextMenuBelow(ContextMenu menu, Control button)
    {
        ArgumentNullException.ThrowIfNull(button);
        Rect2 rect = button.GetGlobalRect();
        ShowContextMenu(menu, button, new Vector2(rect.Position.X, rect.End.Y));
    }

    // a menu bar's menus, each dropping down from its button, with `index` opening; the handler may switch between them
    public static void ShowBar(IReadOnlyList<(ContextMenu Menu, Control Button)> menus, int index)
    {
        Window owner = NativeWindowOf(menus[index].Button);
        List<Platform.BarMenu> bar = [];
        foreach ((ContextMenu menu, Control button) in menus)
        {
            Rect2 rect = button.GetGlobalTransform() * new Rect2(Vector2.Zero, button.Size);
            Vector2I from = ToScreen(button.GetViewport(), rect.Position);
            Vector2I to = ToScreen(button.GetViewport(), rect.End);
            bar.Add(new Platform.BarMenu(menu, new Vector2I(from.X, to.Y), new Rect2I(from, to - from)));
        }
        Callable.From(() => Handler.HandleBar(bar, index, owner)).CallDeferred();
    }

    // a point in a viewport's pixels, in screen pixels
    public static Vector2I ToScreen(Viewport viewport, Vector2 at)
    {
        Window native = NativeWindowOf(viewport);
        return DisplayServer.WindowGetPosition(native.GetWindowId()) + (Vector2I)(viewport.GetScreenTransform() * at).Round();
    }

    // the OS window a node shows in, past any windows embedded in it
    public static Window NativeWindowOf(Node node)
    {
        Window window = node?.GetWindow();
        while (window is not null && window.IsEmbedded()) window = window.GetParent()?.GetWindow();
        return window;
    }

    // the window an embedded popup added under this node draws in: the nearest one embedding its subwindows
    public static Window EmbedderOf(Node node)
    {
        Window window = node?.GetWindow();
        while (window is not null && !window.GuiEmbedSubwindows) window = window.GetParent()?.GetWindow();
        return window;
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
