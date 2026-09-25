using EditSharp.Components.Media;
using EditSharp.Components.Nodes;
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
// it shows, and a picker: an input node's kind, or which of the project's
// media a media property holds. Section.tscn and Subsection.tscn lay the
// two kinds out; the inspector instantiates whichever fits
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

	// the picker on the header, whichever it is bound as
	public OptionButton Picker => kindPicker;

	// which of the two scenes this is, so a rebuild can tell them apart
	public bool Nested { get; internal set; }

	public PropertyDescriptor ToggleDescriptor => toggleDescriptor;

	// whether the picker chooses the kind of the nodes shown here
	public bool HasKindPicker => kindNodes is not null;

	// the media property the picker chooses for, when it does
	public PropertyDescriptor MediaDescriptor => mediaDescriptor;

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
			kindPicker.Visible = kindNodes is not null || mediaDescriptor is not null;
			kindPicker.ItemSelected += OnPickerSelected;
			RefreshPicker();
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

	// ---- the picker ----

	// what the picker's items are, so it is only rebuilt when they change
	object[] pickerItems = [];

	void SetPickerItems(IEnumerable<object> items, IEnumerable<string> labels, string tooltip)
	{
		object[] next = [.. items];

		if (!pickerItems.SequenceEqual(next))
		{
			pickerItems = next;
			kindPicker.Clear();
			foreach (string label in labels) kindPicker.AddItem(label);
		}

		kindPicker.TooltipText = tooltip;
		kindPicker.Visible = true;
	}

	void OnPickerSelected(long index)
	{
		if (inspector is null || index < 0) return;

		if (kindNodes is not null) OnKindSelected((int)index);
		else if (mediaDescriptor is not null) OnMediaSelected((int)index);
	}

	public void RefreshPicker()
	{
		if (kindNodes is not null) RefreshKindPicker();
		else if (mediaDescriptor is not null) RefreshMediaPicker();
	}

	// whether what the picker was bound to has been swapped for another
	// object since the rows were planned - a switch, a media change, or
	// their undo - so the rows are bound to the old one
	public bool Replaced()
	{
		if (kindNodes is not null)
		{
			for (int i = 0; i < kindNodes.Count; i++)
			{
				if (kindNodes[i].Clip is { } clip && !clip.Graph.AllNodes.Contains(boundNodes[i])) return true;
			}
		}

		if (mediaDescriptor is not null)
		{
			for (int i = 0; i < mediaTargets.Count; i++)
			{
				if (!ReferenceEquals(mediaDescriptor.GetValue(mediaTargets[i].Object), boundMedia[i])) return true;
			}
		}

		return false;
	}

	// ---- the kind picker: which kind of input node these are ----

	IReadOnlyList<InspectorTarget> kindNodes;
	IReadOnlyList<NodeKindInfo> kinds = [];
	EditSharp.Components.Nodes.Node[] boundNodes = [];

	// the picker shows which kind every node here is, blank when they
	// differ, and switches all of them at once, each in its own graph
	public void BindKindPicker(Inspector inspector, IReadOnlyList<InspectorTarget> nodes, IReadOnlyList<NodeKindInfo> kinds)
	{
		this.inspector = inspector;
		this.kinds = kinds;
		kindNodes = nodes;
		mediaDescriptor = null;
		boundNodes = [.. nodes.Select(t => (EditSharp.Components.Nodes.Node)t.Object)];

		if (kindPicker is null) return;

		SetPickerItems(kinds, kinds.Select(k => k.DisplayName), "Node type");
		RefreshKindPicker();
	}

	void RefreshKindPicker()
	{
		if (kindPicker is null) return;

		Type shared = null;
		bool mixed = false;

		foreach (InspectorTarget t in kindNodes)
		{
			Type type = t.Object.GetType();

			if (shared is null) shared = type;
			else if (shared != type) { mixed = true; break; }
		}

		int index = mixed ? -1 : kinds.ToList().FindIndex(k => k.Type == shared);
		kindPicker.Select(index);

		// an unlisted kind, or several, shows its name or a dash
		if (index < 0)
			kindPicker.Text = mixed ? "—" : NodeKinds.Of((EditSharp.Components.Nodes.Node)kindNodes[0].Object)?.DisplayName ?? "";
	}

	void OnKindSelected(int index)
	{
		if (index >= kinds.Count) return;

		NodeKindInfo kind = kinds[index];

		using (Transaction.Scope change = inspector.BeginChange($"Change {Title} to {kind.DisplayName}"))
		{
			foreach (EditSharp.Components.Nodes.Node node in boundNodes)
			{
				if (node.GetType() != kind.Type) NodeKinds.Switch(node, kind);
			}

			change?.Commit();
		}

		// the sections are the new nodes'
		inspector.Replan();
		inspector.NotifyEdited();
	}

	// ---- the media picker: which of the project's media a property holds ----

	PropertyDescriptor mediaDescriptor;
	IReadOnlyList<InspectorTarget> mediaTargets;
	object[] boundMedia = [];
	IReadOnlyList<IMedia> offered = [];

	const string BrowseItem = "Browse…";

	// the picker lists the project's media the property can hold, then
	// Browse, which brings a file in as a new media; it shows the media in
	// this property of every object, blank when they differ, and sets all
	// of them at once
	public void BindMediaPicker(Inspector inspector, PropertyDescriptor descriptor, IReadOnlyList<InspectorTarget> targets)
	{
		this.inspector = inspector;
		mediaDescriptor = descriptor;
		mediaTargets = targets;
		kindNodes = null;
		boundMedia = [.. targets.Select(t => descriptor.GetValue(t.Object))];

		if (kindPicker is null) return;

		offered = inspector.Media?.For(descriptor.ValueType) ?? [];
		SetPickerItems([.. offered, BrowseItem], [.. offered.Select(m => m.Name), BrowseItem], "Media");
		RefreshMediaPicker();
	}

	void RefreshMediaPicker()
	{
		if (kindPicker is null) return;

		// names can change under a bound picker
		for (int i = 0; i < offered.Count; i++) kindPicker.SetItemText(i, offered[i].Name);

		IMedia shared = null;
		bool mixed = false;
		bool first = true;

		foreach (InspectorTarget t in mediaTargets)
		{
			var media = mediaDescriptor.GetValue(t.Object) as IMedia;

			if (first) { shared = media; first = false; }
			else if (!ReferenceEquals(shared, media)) { mixed = true; break; }
		}

		int index = mixed || shared is null ? -1 : offered.ToList().IndexOf(shared);
		kindPicker.Select(index);

		// no media, several, or one the project doesn't list
		if (index < 0)
			kindPicker.Text = mixed ? "—" : shared?.Name ?? "(none)";
	}

	void OnMediaSelected(int index)
	{
		if (index < offered.Count) { Assign(offered[index], $"Change {Title}"); return; }

		if (kindPicker.GetItemText(index) != BrowseItem) return;

		// the picker would show Browse as chosen until a file is picked
		RefreshMediaPicker();

		var dialog = new FileDialog
		{
			FileMode = FileDialog.FileModeEnum.OpenFile,
			Access = FileDialog.AccessEnum.Filesystem,
			Title = "Choose a media file",
		};

		AddChild(dialog);
		dialog.FileSelected += path => { dialog.QueueFree(); Bring(path); };
		dialog.Canceled += dialog.QueueFree;
		dialog.PopupCentered(new Vector2I(800, 600));
	}

	// a file becomes a media of the property's type, joins the project,
	// and is what every object here holds - one entry
	void Bring(string path)
	{
		IMedia media = Transaction.Suppressed<IMedia>(() =>
		{
			if (typeof(VideoMedia).IsAssignableFrom(mediaDescriptor.ValueType)) return new VideoMedia { Path = path };
			if (typeof(AudioMedia).IsAssignableFrom(mediaDescriptor.ValueType)) return new AudioMedia { Path = path };
			throw new InvalidOperationException($"{mediaDescriptor.DisplayName} can't hold a file.");
		});

		using (Transaction.Scope change = inspector.BeginChange($"Bring in {media.Name}"))
		{
			inspector.Media?.Add(media);
			foreach (InspectorTarget t in mediaTargets) mediaDescriptor.SetValue(t.Object, media);
			change?.Commit();
		}

		inspector.Replan();
		inspector.NotifyEdited();
	}

	void Assign(IMedia media, string description)
	{
		using (Transaction.Scope change = inspector.BeginChange(description))
		{
			foreach (InspectorTarget t in mediaTargets)
			{
				if (!ReferenceEquals(mediaDescriptor.GetValue(t.Object), media)) mediaDescriptor.SetValue(t.Object, media);
			}

			change?.Commit();
		}

		// the rows under it are the new media's
		inspector.Replan();
		inspector.NotifyEdited();
	}
}
