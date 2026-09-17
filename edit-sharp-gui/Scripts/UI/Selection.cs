using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace EditSharpGUI.Scripts.UI;

public class Selection<T>() : List<T>
{
    public void Select(T item, SelectionMode mode)
    {
        switch (mode)
        {
            case SelectionMode.ExclusiveIfUnselected:
                if (!Contains(item)) Select(item, SelectionMode.Exclusive);
                return;
            case SelectionMode.Exclusive:
                Clear();
                break;
        }

        Add(item);
    }

    // selection that treats group of items as one
    public void Select(IEnumerable<T> items, SelectionMode mode)
    {
        switch (mode)
        {
            case SelectionMode.ExclusiveIfUnselected:
                if (!items.Any(Contains)) Select(items, SelectionMode.Exclusive);
                return;
            case SelectionMode.Exclusive:
                Clear();
                break;
        }

        foreach (T item in items) if (!Contains(item)) Add(item);
    }

    public void Deselect(T item)
    {
        Remove(item);
    }

    public void Deselect(IEnumerable<T> items)
    {
        foreach (T item in items) Remove(item);
    }
}

public enum SelectionMode
{
    // add this item to existing selection
    // if it is not already part of it
    Inclusive,
    // if this item is part of the current selection, do nothing
    // otherwise act exclusive
    ExclusiveIfUnselected,
    // make this item the only one in the selection
    Exclusive
}

