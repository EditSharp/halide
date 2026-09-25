using System;
using System.Collections.Generic;
using Godot;

namespace EditSharpGUI.Scripts.UI.ContextMenu;

public class ContextMenu
{
    public List<ContextElement> Elements = [];

    public bool HideOnItemSelect = true;
    public bool HideOnCheckableItemSelect = false;
}

public class ContextSubmenu : ContextBaseButton
{
    public List<ContextElement> Elements = [];
}

public abstract class ContextElement
{
    public bool Visible = true;
}

public class ContextDivider : ContextElement
{
    
}

public abstract class ContextBaseButton : ContextElement
{
    public ContextText Text;
    public ContextText ShortcutHint = null;
    public Texture2D Icon;
}

public class ContextText(string text) : ContextElement
{
    public string Text = text;

    public Weight TextWeight = Weight.Normal;

    public enum Weight { Normal, Bold }
}

public class ContextButton : ContextBaseButton
{
    public bool Enabled = true;
    public bool Checked = false;

    public CheckType Type = CheckType.None;
    public enum CheckType { None, Check, Radio}
}

// list of buttons where any can be selected/deselected
public class ContextCheckList : ContextElement
{
    public List<ContextButton> Buttons = [];
    public List<int> CheckedButtons = [];
}

// list of buttons where only one can be selected
public class ContextRadioList : ContextElement
{
    public List<ContextButton> Buttons = [];
    public int SelectedButton = 0;
}