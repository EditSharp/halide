using System;
using Godot;
using Godot.Collections;

namespace EditSharpGUI.Scripts.UI.ContextMenu;

[GlobalClass]
public partial class ContextMenu : Resource
{
    [Export] public Array<ContextElement> Elements = [];

    [Export] public bool HideOnItemSelect = true;
    [Export] public bool HideOnCheckableItemSelect = false;

    // fires once the menu is gone, whether or not anything was picked
    [Signal] public delegate void ClosedEventHandler();
}
