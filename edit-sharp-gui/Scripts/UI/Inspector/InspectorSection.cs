using EditSharp.Editing;
using EditSharp.History;
using Godot;
using System;
using System.Collections.Generic;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// a titled, collapsible group of rows: an object, a node, a nested object
// folded inline, a list. the header is a button the whole width; clicking
// it anywhere folds the body. a section can carry a switch on the right of
// its header - a node's Enabled - bound to a bool property of every object
// it shows. Section.tscn and Subsection.tscn lay the two kinds out; the
// inspector instantiates whichever fits
[Tool]
public partial class InspectorSection : VBoxContainer
{
	[Export] SectionHeader header;
	[Export] Control headerControls;
	[Export] Control indent;
	[Export] VBoxContainer body;
	[Export] CheckButton toggle;

	public VBoxContainer Body => body;

	public string Title
	{
		get => header.Title;
		set => header.Title = value;
	}

	// a strip of colour down the header's left: a node's category
	public Color? Accent
	{
		get => header.Accent;
		set => header.Accent = value;
	}

	public bool Collapsed
	{
		get => !header.ButtonPressed;
		set { header.ButtonPressed = !value; indent.Visible = !value; }
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
}
