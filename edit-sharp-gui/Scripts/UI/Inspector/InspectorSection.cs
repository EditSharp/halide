using EditSharp.Components.Sources;
using EditSharp.Editing;
using EditSharp.History;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// a titled, collapsible group of rows: an object, a node, a nested object
// folded inline, a list. the header is a button the whole width; clicking
// it anywhere folds the body. a section can carry a switch on the right of
// its header - a node's Enabled - bound to a bool property of every object
// it shows - and a source's kind picker. Section.tscn and Subsection.tscn lay the two kinds out; the
// inspector instantiates whichever fits
[Tool]
public partial class InspectorSection : VBoxContainer
{
	[Export] SectionHeader header;
	[Export] Control headerControls;
	[Export] Control indent;
	[Export] VBoxContainer body;
	[Export] CheckButton toggle;
	[Export] OptionButton kindPicker;

	public VBoxContainer Body => body;

	// which of the two scenes this is, so a rebuild can tell them apart
	public bool Nested { get; internal set; }

	public PropertyDescriptor ToggleDescriptor => toggleDescriptor;

	public PropertyDescriptor KindDescriptor => kindDescriptor;

	// these delegate to the header, an export. the C# hot-reload serialiser
	// reads and writes public properties before it restores exported
	// fields, so the header can be null here
	public string Title
	{
		get => header?.Title ?? string.Empty;
		set { if (header is not null) header.Title = value; }
	}

	// a strip of colour down the header's left: a node's category
	public Color? Accent
	{
		get => header?.Accent;
		set { if (header is not null) header.Accent = value; }
	}

	public bool Collapsed
	{
		get => header is null || !header.ButtonPressed;
		set
		{
			if (header is not null) header.ButtonPressed = !value;
			if (indent is not null) indent.Visible = !value;
		}
	}

	public override void _Ready()
	{
		indent.Visible = header.ButtonPressed;
		header.Toggled += on => { indent.Visible = on; header.QueueRedraw(); };

		if (toggle is not null)
		{
			toggle.Visible = toggleDescriptor is not null;
			toggle.Toggled += OnToggled;
			RefreshToggle();
		}

		if (kindPicker is not null)
		{
			kindPicker.Visible = kindDescriptor is not null;
			kindPicker.ItemSelected += OnKindSelected;
			RefreshKindPicker();
		}
	}

	// controls on the header's right: a list's add button, an item's remove
	public void AddHeaderControl(Control control)
	{
		control.MouseFilter = MouseFilterEnum.Stop;
		headerControls.AddChild(control);
	}

	// ---- the header switch ----

	Inspector inspector;
	PropertyDescriptor toggleDescriptor;
	IReadOnlyList<InspectorTarget> toggleTargets;

	// the switch shows this bool property of every object, and flips it on
	// all of them at once
	public void BindToggle(Inspector inspector, PropertyDescriptor descriptor, IReadOnlyList<InspectorTarget> targets)
	{
		this.inspector = inspector;
		toggleDescriptor = descriptor;
		toggleTargets = targets;

		if (toggle is null) return;

		toggle.Visible = true;
		toggle.TooltipText = descriptor.DisplayName;
		RefreshToggle();
	}

	public void RefreshToggle()
	{
		if (toggle is null || toggleDescriptor is null) return;

		bool? shared = null;
		bool mixed = false;

		foreach (InspectorTarget t in toggleTargets)
		{
			bool value = toggleDescriptor.GetValue(t.Object) is true;

			if (shared is null) shared = value;
			else if (shared != value) { mixed = true; break; }
		}

		toggle.SetPressedNoSignal(shared ?? false);
		toggle.SelfModulate = mixed ? new Color(1f, 1f, 1f, 0.4f) : Colors.White;
	}

	void OnToggled(bool on)
	{
		if (toggleDescriptor is null || inspector is null) return;

		using (Transaction.Scope change = inspector.BeginChange(on ? $"Enable {Title}" : $"Disable {Title}"))
		{
			foreach (InspectorTarget t in toggleTargets) toggleDescriptor.SetValue(t.Object, on);
			change?.Commit();
		}

		RefreshToggle();
		inspector.NotifyEdited();
	}

	// ---- the kind picker ----

	PropertyDescriptor kindDescriptor;
	IReadOnlyList<InspectorTarget> kindTargets;
	IReadOnlyList<SourceKindInfo> kinds = [];
	object[] boundSources = [];

	// the picker shows which kind the source in this property of every
	// object is, blank when they differ, and switches all of them at once
	public void BindKindPicker(Inspector inspector, PropertyDescriptor descriptor, IReadOnlyList<InspectorTarget> targets, IReadOnlyList<SourceKindInfo> kinds)
	{
		this.inspector = inspector;
		kindDescriptor = descriptor;
		kindTargets = targets;
		boundSources = [.. targets.Select(t => descriptor.GetValue(t.Object))];

		if (kindPicker is null) return;

		if (!this.kinds.SequenceEqual(kinds))
		{
			this.kinds = kinds;
			kindPicker.Clear();
			foreach (SourceKindInfo kind in kinds) kindPicker.AddItem(kind.DisplayName);
		}

		kindPicker.Visible = true;
		RefreshKindPicker();
	}

	// whether a source shown here was swapped for another object since the
	// rows were planned (a switch, or its undo), so they're bound to the old one
	public bool SourcesReplaced()
	{
		if (kindDescriptor is null) return false;

		for (int i = 0; i < kindTargets.Count; i++)
			if (!ReferenceEquals(kindDescriptor.GetValue(kindTargets[i].Object), boundSources[i])) return true;

		return false;
	}

	public void RefreshKindPicker()
	{
		if (kindPicker is null || kindDescriptor is null) return;

		Type shared = null;
		bool mixed = false;

		foreach (InspectorTarget t in kindTargets)
		{
			Type type = kindDescriptor.GetValue(t.Object)?.GetType();

			if (shared is null) shared = type;
			else if (shared != type) { mixed = true; break; }
		}

		int index = mixed ? -1 : kinds.ToList().FindIndex(k => k.Type == shared);
		kindPicker.Select(index);

		// an unlisted kind, or several, shows its name or a dash
		if (index < 0)
			kindPicker.Text = mixed ? "—" : (kindDescriptor.GetValue(kindTargets[0].Object) is Source s && SourceKinds.Of(s) is { } kind ? kind.DisplayName : "");
	}

	void OnKindSelected(long index)
	{
		if (kindDescriptor is null || inspector is null || index < 0 || index >= kinds.Count) return;

		SourceKindInfo kind = kinds[(int)index];

		using (Transaction.Scope change = inspector.BeginChange($"Change {Title} to {kind.DisplayName}"))
		{
			foreach (InspectorTarget t in kindTargets)
			{
				if (kindDescriptor.GetValue(t.Object) is Source from && from.GetType() != kind.Type)
					kindDescriptor.SetValue(t.Object, SourceKinds.Switch(from, kind));
			}

			change?.Commit();
		}

		// the rows under it are the new kind's
		inspector.Replan();
		inspector.NotifyEdited();
	}
}
