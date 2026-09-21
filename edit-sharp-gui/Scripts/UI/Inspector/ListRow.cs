using EditSharp.Editing;
using EditSharp.History;
using Godot;
using System.Collections;
using System.Collections.Generic;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// a list property: a fold titled with the count and an add button, and a
// row per item with a remove button. items that are values get editors;
// items that are objects fold inline. one object at a time - lists on
// several objects at once have no sensible shared edit. ListRow.tscn
// holds the fold and its add button
[Tool]
public partial class ListRow : VBoxContainer
{
	[Export] InspectorSection section;
	[Export] Button add;

	Inspector inspector;
	string label;
	PropertyDescriptor descriptor;
	PropertyBinding binding;

	int builtCount = -1;

	readonly List<InspectorRow> rows = [];
	readonly List<InspectorSection> objectSections = [];

	public void Configure(Inspector inspector, string label, PropertyDescriptor descriptor, PropertyBinding binding)
	{
		this.inspector = inspector;
		this.label = label;
		this.descriptor = descriptor;
		this.binding = binding;
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

	IList List => descriptor.GetList(binding.Target);

	public void Refresh()
	{
		if (section is null) return;

		Visible = binding.IsVisible;
		if (!Visible) return;

		IList list = List;
		int count = list?.Count ?? 0;

		section.Title = $"{label} ({count})";

		if (count != builtCount) Rebuild(list);
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

	void Rebuild(IList list)
	{
		foreach (Node child in section.Body.GetChildren()) child.QueueFree();
		rows.Clear();
		objectSections.Clear();

		builtCount = list?.Count ?? 0;
		if (list is null) return;

		bool objects = descriptor.ItemEditor is PropertyEditor.Object or PropertyEditor.Media && descriptor.ItemValueType is not null && Inspect.Of(descriptor.ItemValueType).Count > 0;

		for (int i = 0; i < list.Count; i++)
		{
			int index = i;

			if (objects)
			{
				object item = list[i];
				InspectorSection s = inspector.CreateSection(i.ToString(), null, nested: true);
				section.Body.AddChild(s);
				objectSections.Add(s);

				if (!descriptor.IsReadOnly) s.AddHeaderControl(RemoveButton(index));

				if (item is not null) inspector.BuildRows(s.Body, [new InspectorTarget(item, binding.Clip)]);
			}
			else
			{
				InspectorRow row = inspector.CreateRow(i.ToString(), EditorSpec.OfItem(descriptor), [new ListItemBinding(list, i, descriptor.ItemIsAnimatable) { Clip = binding.Clip }]);
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

	void Add()
	{
		using (Transaction.Scope change = inspector.BeginChange($"Add {label} item"))
		{
			descriptor.AddItem(binding.Target, descriptor.CreateItem());
			change?.Commit();
		}

		inspector.NotifyEdited();
		Refresh();
	}

	void Remove(int index)
	{
		using (Transaction.Scope change = inspector.BeginChange($"Remove {label} item"))
		{
			descriptor.RemoveItem(binding.Target, index);
			change?.Commit();
		}

		inspector.NotifyEdited();
		Refresh();
	}
}
