using System;
using System.Collections.Generic;
using Godot;
using Godot.Collections;

namespace EditSharpGUI.Scripts.UI.ContextMenu;

[GlobalClass]
public partial class ContextMenu : Resource
{
    [Export] public Array<ContextElement> Elements = [];

    [Export] public bool FadeAnimations = false;
    [Export] public bool HideOnItemSelect = true;
    [Export] public bool HideOnCheckableItemSelect = false;

    // fires once the menu is gone, whether or not anything was picked
    [Signal] public delegate void ClosedEventHandler();

    // the element with this Id, anywhere in the menu: submenus and lists included
    public T Find<T>(string id) where T : ContextElement => Find<T>(Elements, id);

    public ContextElement Find(string id) => Find<ContextElement>(Elements, id);

    public static T Find<T>(IEnumerable<ContextElement> elements, string id) where T : ContextElement
    {
        foreach (ContextElement element in elements)
        {
            if (element is null) continue;
            if (element.Id == id && element is T match) return match;

            T inner = element switch
            {
                ContextSubmenu submenu => Find<T>(submenu.Elements, id),
                ContextCheckList list => Find<T>(list.Buttons, id),
                ContextRadioList list => Find<T>(list.Buttons, id),
                _ => null
            };

            if (inner is not null) return inner;
        }

        return null;
    }

    // every element anywhere in the menu, depth first
    public IEnumerable<ContextElement> All() => All(Elements);

    public static IEnumerable<ContextElement> All(IEnumerable<ContextElement> elements)
    {
        foreach (ContextElement element in elements)
        {
            if (element is null) continue;
            yield return element;

            IEnumerable<ContextElement> inner = element switch
            {
                ContextSubmenu submenu => All(submenu.Elements),
                ContextCheckList list => list.Buttons,
                ContextRadioList list => list.Buttons,
                _ => null
            };

            if (inner is null) continue;
            foreach (ContextElement e in inner) yield return e;
        }
    }

    // a fresh copy of the whole tree, for a menu shown twice at once
    public ContextMenu Clone() => (ContextMenu)Duplicate(true);
}
