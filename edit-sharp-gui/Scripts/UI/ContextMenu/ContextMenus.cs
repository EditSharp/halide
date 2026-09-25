using System;
using EditSharpGUI.Scripts.UI.ContextMenu.Platform;
using Godot;

namespace EditSharpGUI.Scripts.UI.ContextMenu;

public static class ContextMenus
{
    public static void ShowContextMenu(ContextMenu menu, Vector2? position = null)
    {
        switch (OS.GetName())
        {
            case "Windows":
                new WindowsHandler().HandleMenu(menu, position);
                break;
        }
    }

    static ContextMenu Example = new()
    {
        Elements = [
            new ContextButton() {
                Icon = new(), // cut icon
                Text = new("Cut"),
            },
            new ContextButton() {
                Icon = new(), // copy icon
                Text = new("Copy"),
            },
            new ContextButton() {
                Icon = new(), // paste icon
                Text = new("Paste"),
            },
            new ContextDivider(),
            new ContextButton() {
                Text = new("Linked"),
                Checked = false,
                Type = ContextButton.CheckType.Check
            },
            new ContextDivider(),
            new ContextSubmenu()
            {
                Icon = new(), // Clip's current color
                Text = new("Clip Color"),
                Elements = [
                    new ContextRadioList() {
                        Buttons = [
                            new() {
                                Icon = new(), // red icon
                                Text = new("Red"),
                            },
                            new() {
                                Icon = new(), // yellow icon
                                Text = new("Yellow"),
                            },
                            new() {
                                Icon = new(), // blue icon
                                Text = new("Blue"),
                            }
                        ]
                    }
                ]
            }
        ]
    };
}
