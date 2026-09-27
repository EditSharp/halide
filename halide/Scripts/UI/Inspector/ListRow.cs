using EditSharp.Editing;
using EditSharp.History;
using Godot;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Halide.Scripts.UI.Inspecting;

// a list property: a fold titled with the count and an add button, and a
// row per item with a remove button. items that are values get editors;
// items that are objects fold inline. a list of values on several objects
// shows what they all hold: an item added or edited lands in every list,
// a removed one leaves every list. lists of objects edit one object at a
// time. ListRow.tscn holds the fold and its add button
[Tool]
public partial class ListRow : VBoxContainer
{
	[Export] InspectorSection section;
	[Export] Button add;

	Inspector inspector;
	string label;
	PropertyDescriptor descriptor;
	IReadOnlyList<PropertyBinding> bindings = [];

	// the items shown when last built, so a rename with the same count still rebuilds
	List<object> builtItems;

	readonly List<InspectorRow> rows = [];
	readonly List<InspectorSection> objectSections = [];

	public void Configure(Inspector inspector, string label, PropertyDescriptor descriptor, IReadOnlyList<PropertyBinding> bindings)
	{
		this.inspector = inspector;
		this.label = label;
		this.descriptor = descriptor;
		this.bindings = bindings;
	}

	public string Label => label;
	public PropertyDescriptor Descriptor => descriptor;

	// the same list property on other objects. items are bound to the old
	// lists, so they are built again from the new ones
	public void Rebind(IReadOnlyList<PropertyBinding> bindings)
	{
		bool same = bindings.Count == this.bindings.Count && bindings.Zip(this.bindings).All(p => ReferenceEquals(p.First.Target, p.Second.Target));

		this.bindings = bindings;
		if (!same) builtItems = null;

		Refresh();
	}

	public override void _Ready()
	{
		// placed in a scene by hand, with no list behind it: shown as laid out
		if (descriptor is null)
		{
			if (section.Title == "Section" || section.Title == "Subsection") section.Title = Name;
			return;
		}

		add.Visible = !descriptor.IsReadOnly;
		add.TooltipText = $"Add {label} item";
		add.Pressed += Add;

		Refresh();
	}

	bool Multi => bindings.Count > 1;

	IEnumerable<IList> Lists => bindings.Select(b => descriptor.GetList(b.Target)).Where(l => l is not null);

	IList SingleList => bindings.Count == 0 ? null : descriptor.GetList(bindings[0].Target);

	// what the fold shows: one object's list as it is, or the items every
	// object's list holds, in the first one's order
	List<object> Items()
	{
		if (!Multi) return SingleList is IList list ? [.. list.Cast<object>()] : [];

		List<IList> lists = [.. Lists];
		if (lists.Count == 0) return [];

		return [.. lists[0].Cast<object>().Where(item => lists.Skip(1).All(l => l.Contains(item))).Distinct()];
	}

	public void Refresh()
	{
		if (section is null) return;

		Visible = bindings.All(b => b.IsVisible);
		if (!Visible) return;

		List<object> items = Items();
		section.Title = $"{label} ({items.Count})";

		if (builtItems is null || !builtItems.SequenceEqual(items)) Rebuild(items);
		else
		{
			foreach (InspectorRow row in rows) row.Refresh();
			foreach (InspectorSection s in objectSections) foreach (Node child in s.Body.GetChildren()) Inspector.RefreshNode(child);
		}
	}

	public void RefreshForTime()
	{
		foreach (InspectorRow row in rows) row.RefreshForTime();
		foreach (InspectorSection s in objectSections) foreach (Node child in s.Body.GetChildren()) Inspector.RefreshNodeForTime(child);
	}

	void Rebuild(List<object> items)
	{
		foreach (Node child in section.Body.GetChildren()) child.QueueFree();
		rows.Clear();
		objectSections.Clear();

		builtItems = [.. items];

		bool objects = descriptor.ItemEditor is PropertyEditor.Object or PropertyEditor.Media && descriptor.ItemValueType is not null && Inspect.Of(descriptor.ItemValueType).Count > 0;
		IList single = SingleList;
		List<IList> lists = [.. Lists];

		for (int i = 0; i < items.Count; i++)
		{
			int index = i;
			object item = items[i];

			if (objects && !Multi)
			{
				InspectorSection s = inspector.CreateSection(i.ToString(), null, nested: true);
				section.Body.AddChild(s);
				objectSections.Add(s);

				if (!descriptor.IsReadOnly) s.AddHeaderControl(RemoveButton(index));

				if (item is not null) inspector.BuildRows(s.Body, [new InspectorTarget(item, bindings[0].Clip)]);
			}
			else
			{
				Binding binding = Multi
					? new MultiListItemBinding(lists, item)
					: new ListItemBinding(single, i, descriptor.ItemIsAnimatable) { Clip = bindings[0].Clip };

				InspectorRow row = inspector.CreateRow(i.ToString(), EditorSpec.OfItem(descriptor), [binding]);
				if (!descriptor.IsReadOnly) row.AddTrailing(RemoveButton(index));
				section.Body.AddChild(row);
				rows.Add(row);
			}
		}
	}

	Button RemoveButton(int index)
	{
		Button remove = inspector.CreateRemoveButton();
		remove.Pressed += () => Remove(index);
		return remove;
	}

	// a new item in every list shown
	void Add()
	{
		using (Transaction.Scope change = inspector.BeginChange($"Add {label} item"))
		{
			object item = descriptor.CreateItem();

			foreach (PropertyBinding binding in bindings)
			{
				// one object can't hold the same item twice when the lists are matched by value
				if (Multi && descriptor.GetList(binding.Target) is IList list && list.Contains(item)) continue;
				descriptor.AddItem(binding.Target, Multi ? item : descriptor.CreateItem());
			}

			change?.Commit();
		}

		inspector.NotifyEdited();
		Refresh();
	}

	// the item at a shown position leaves every list
	void Remove(int index)
	{
		if (builtItems is null || index < 0 || index >= builtItems.Count) return;
		object item = builtItems[index];

		using (Transaction.Scope change = inspector.BeginChange($"Remove {label} item"))
		{
			foreach (PropertyBinding binding in bindings)
			{
				IList list = descriptor.GetList(binding.Target);
				if (list is null) continue;

				int at = Multi ? list.IndexOf(item) : index;
				if (at >= 0) descriptor.RemoveItem(binding.Target, at);
			}

			change?.Commit();
		}

		inspector.NotifyEdited();
		Refresh();
	}
}
