using System.Collections.Generic;
using Godot;

namespace EditSharpGUI.Scripts.UI.ContextMenu.Platform;

// what every platform needs from a ContextMenu: the elements flattened into rows, the pick applied
// back to the model, and hints turned into key equivalents. no drawing here

public enum MenuItemKind { Separator, Label, Button, Submenu }

// a pickable row: the button, the list it belongs to (if any) and its index there
public record MenuEntry(ContextButton Button, ContextElement Owner, int Index);

public sealed class MenuItem
{
    public MenuItemKind Kind;
    public string Text = "", Hint;
    public bool Bold, Enabled = true, Checked, Radio;
    public Texture2D Icon;
    public List<MenuItem> Submenu;
    public MenuEntry Entry;
}

public static class MenuModel
{
    public static List<MenuItem> Flatten(Godot.Collections.Array<ContextElement> elements)
    {
        List<MenuItem> items = [];

        foreach (ContextElement element in elements)
        {
            if (element is null || !element.Visible) continue;

            switch (element)
            {
                case ContextDivider:
                    items.Add(new MenuItem { Kind = MenuItemKind.Separator });
                    break;

                case ContextText text:
                    items.Add(new MenuItem { Kind = MenuItemKind.Label, Text = text.Text, Bold = IsBold(text), Enabled = false });
                    break;

                case ContextSubmenu submenu:
                    MenuItem item = Labelled(submenu, MenuItemKind.Submenu);
                    item.Submenu = Flatten(submenu.Elements);
                    items.Add(item);
                    break;

                case ContextButton button:
                    items.Add(ButtonItem(button, null, 0, button.Checked, button.Type == ContextButton.CheckType.Radio));
                    break;

                case ContextCheckList list:
                    for (int i = 0; i < list.Buttons.Count; i++)
                    {
                        ContextButton button = list.Buttons[i];
                        if (button is null || !button.Visible) continue;
                        button.Checked = list.CheckedButtons.Contains(i);
                        items.Add(ButtonItem(button, list, i, button.Checked, radio: false));
                    }
                    break;

                case ContextRadioList list:
                    for (int i = 0; i < list.Buttons.Count; i++)
                    {
                        ContextButton button = list.Buttons[i];
                        if (button is null || !button.Visible) continue;
                        button.Checked = i == list.SelectedButton;
                        items.Add(ButtonItem(button, list, i, button.Checked, radio: true));
                    }
                    break;
            }
        }

        return items;
    }

    public static bool AnyIcons(List<MenuItem> items)
    {
        foreach (MenuItem item in items) if (item.Icon is not null) return true;
        return false;
    }

    // applies the pick to the model and emits its signal; true when the item was checkable
    public static bool Activate(MenuEntry entry)
    {
        ContextButton button = entry.Button;
        switch (entry.Owner)
        {
            case ContextCheckList list:
                bool on = !list.CheckedButtons.Remove(entry.Index);
                if (on) list.CheckedButtons.Add(entry.Index);
                button.Checked = on;
                list.EmitSignal(ContextCheckList.SignalName.Toggled, button, on);
                return true;

            case ContextRadioList list:
                list.SelectedButton = entry.Index;
                foreach (ContextButton b in list.Buttons) b.Checked = b == button;
                list.EmitSignal(ContextRadioList.SignalName.Selected, button);
                return true;

            default:
                if (button.Type == ContextButton.CheckType.Check) button.Checked = !button.Checked;
                else if (button.Type == ContextButton.CheckType.Radio) button.Checked = true;
                button.EmitSignal(ContextButton.SignalName.Pressed);
                return button.Type != ContextButton.CheckType.None;
        }
    }

    // "Ctrl+Shift+X" as a key with modifiers, for platforms that show hints as key equivalents
    public static bool TryParseHint(string hint, out Key key, out KeyModifierMask modifiers)
    {
        key = Key.None;
        modifiers = 0;
        if (string.IsNullOrWhiteSpace(hint)) return false;

        string[] parts = hint.Split('+', System.StringSplitOptions.TrimEntries | System.StringSplitOptions.RemoveEmptyEntries);
        if (hint.EndsWith('+')) parts = [.. parts, "+"];
        if (parts.Length == 0) return false;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= KeyModifierMask.MaskCtrl; break;
                case "shift": modifiers |= KeyModifierMask.MaskShift; break;
                case "alt" or "option": modifiers |= KeyModifierMask.MaskAlt; break;
                case "cmd" or "command" or "meta" or "win" or "super": modifiers |= KeyModifierMask.MaskMeta; break;
                default: return false;
            }
        }

        key = OS.FindKeycodeFromString(parts[^1]);
        return key != Key.None;
    }

    static MenuItem ButtonItem(ContextButton button, ContextElement owner, int index, bool isChecked, bool radio)
    {
        MenuItem item = Labelled(button, MenuItemKind.Button);
        item.Checked = isChecked;
        item.Radio = radio;
        item.Entry = new MenuEntry(button, owner, index);
        return item;
    }

    static MenuItem Labelled(ContextBaseButton button, MenuItemKind kind) => new()
    {
        Kind = kind,
        Text = button.Text?.Text ?? "",
        Hint = button.ShortcutHint?.Text,
        Bold = IsBold(button.Text),
        Enabled = button.Enabled,
        Icon = button.Icon,
    };

    static bool IsBold(ContextText text) => text?.TextWeight == ContextText.Weight.Bold;
}
