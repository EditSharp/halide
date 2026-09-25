using System;
using System.Collections.Generic;
using Godot;

namespace EditSharpGUI.Scripts.UI.ContextMenu;

public class ContextMenu
{
    List<ContextElement> elements = [];
}

public class ContextElement
{
    public bool Enabled = true;
}

public class ContextButton : ContextElement
{
    public string Text;
    public Texture2D Icon;

    public bool Selected = false;
}

public class ContextCheckList : ContextButton
{
    
}

public class ContextRadioList : ContextButton
{
    
}