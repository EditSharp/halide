using EditSharp.Components;
using EditSharp.Components.Clips;
using EditSharp.Editing;
using EditSharp.History;
using System;
using System.Collections;
using System.Collections.Generic;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// one value on one object, as the inspector reaches it: how to read it,
// write it, and animate it. a row holds one of these per object it edits,
// and the clip behind each - keyframes live in a clip's content time, and
// the playhead is timeline time, so the clip is what joins the two
public abstract class Binding
{
	public Clip Clip { get; init; }

	public abstract object Get();
	public abstract void Set(object value);

	// null when the value cannot be animated
	public virtual IAnimatable Animatable => null;

	public virtual bool IsVisible => true;

	// the value a reset goes back to, when there is one
	public virtual bool TryGetDefault(out object value)
	{
		value = null;
		return false;
	}

	public TimeSpan ContentTime(TimeSpan playhead)
		=> Clip is null ? playhead : TimeSpan.FromSeconds((playhead - Clip.Start).TotalSeconds * Clip.Speed);

	public TimeSpan TimelineTime(TimeSpan content)
		=> Clip is null ? content : Clip.Start + TimeSpan.FromSeconds(content.TotalSeconds / Clip.Speed);
}

// a property on an object, through its descriptor
public sealed class PropertyBinding(PropertyDescriptor descriptor, object target) : Binding
{
	public PropertyDescriptor Descriptor => descriptor;
	public object Target => target;

	public override object Get() => descriptor.GetValue(target);
	public override void Set(object value) => descriptor.SetValue(target, value);
	public override IAnimatable Animatable => descriptor.GetAnimatable(target);
	public override bool IsVisible => descriptor.IsVisible(target);
	public override bool TryGetDefault(out object value) => descriptor.TryGetDefault(target, out value);
}

// one item of a list, by index. an animatable item is the animatable
// itself; a plain one is written back into the list, recorded
public sealed class ListItemBinding(IList list, int index, bool animatable) : Binding
{
	public int Index => index;

	public override object Get() => animatable ? (list[index] as IAnimatable)?.GetStaticValue() : list[index];

	public override void Set(object value)
	{
		if (animatable)
		{
			(list[index] as IAnimatable)?.SetStaticValue(value);
			return;
		}

		object old = list[index];
		int at = index;
		Transaction.Apply(() => list[at] = value, () => list[at] = old, "set item");
	}

	public override IAnimatable Animatable => animatable ? list[index] as IAnimatable : null;
}

// one value held by every one of several lists, matched by value. writing
// replaces it in each list, recorded
public sealed class MultiListItemBinding(IReadOnlyList<IList> lists, object value) : Binding
{
	object value = value;

	public override object Get() => value;

	public override void Set(object newValue)
	{
		object old = value;

		foreach (IList list in lists)
		{
			int at = list.IndexOf(old);
			if (at < 0 || list.Contains(newValue)) continue;

			IList target = list;
			int index = at;
			Transaction.Apply(() => target[index] = newValue, () => target[index] = old, "set item");
		}

		value = newValue;
	}
}
