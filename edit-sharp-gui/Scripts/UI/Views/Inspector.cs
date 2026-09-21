using EditSharp.Components.Clips;
using EditSharp.Editing;
using EditSharp.History;
using EditSharpGUI.Scripts.UI;
using EditSharpGUI.Scripts.UI.Inspecting;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// one object the inspector shows, and the clip its keyframes belong to
// (null for something that is not in a clip - a channel, a transition)
public sealed record InspectorTarget(object Object, Clip Clip = null);

// a titled group of objects edited together: their shared properties are
// shown once and an edit writes to all of them
public sealed record InspectorSectionSpec(string Title, IReadOnlyList<InspectorTarget> Targets, Color? Accent = null);

// a value committed on a row: what it wrote, to which bindings, and what
// each held before - enough to undo it from outside
public sealed record InspectorEditArgs(string Label, IReadOnlyList<Binding> Bindings, object[] Before, object After);

// the property editor. it is handed sections of objects and shows their
// editable properties - found through the model's Editable metadata - as
// rows with a reset and a keyframe column, godot-inspector style. every
// part is a scene under Scenes/Inspector, exported here, so its layout
// and looks are edited in the editor; this only instantiates and wires.
// it knows the playhead, for what an animated value is right now and
// where a new keyframe goes, and asks to move it when a keyframe arrow is
// pressed. it never sees the timeline or the playback: whoever wires the
// page feeds it
[Tool]
public partial class Inspector : Control
{
	[ExportGroup("Parts")]
	[Export] ScrollContainer scroll;
	[Export] VBoxContainer content;
	[Export] Control empty;

	[ExportGroup("Scenes")]
	[Export] PackedScene sectionScene;
	[Export] PackedScene subsectionScene;
	[Export] PackedScene rowScene;
	[Export] PackedScene listRowScene;
	[Export] PackedScene removeButtonScene;

	[ExportSubgroup("Editors")]
	[Export] PackedScene toggleEditor;
	[Export] PackedScene numberEditor;
	[Export] PackedScene angleEditor;
	[Export] PackedScene textEditor;
	[Export] PackedScene multilineEditor;
	[Export] PackedScene pathEditor;
	[Export] PackedScene dropdownEditor;
	[Export] PackedScene colorEditor;
	[Export] PackedScene vectorEditor;
	[Export] PackedScene timeEditor;
	[Export] PackedScene labelEditor;

	// where edits are recorded. the active history when unset
	public History History
	{
		get => field ?? History.Active;
		set
		{
			if (field is not null) field.Changed -= OnHistoryChanged;
			field = value;
			if (field is not null) field.Changed += OnHistoryChanged;
		}
	}

	// for time fields shown as frames
	public int Framerate { get; set; } = 30;

	// time fields as frame counts rather than clocks. toggled from any
	// time field, for all of them
	public bool ShowFrames
	{
		get;
		set { field = value; RefreshValues(); }
	}

	// timeline time. animated rows show their value here, and a keyframe
	// added from a row goes here
	public TimeSpan Playhead
	{
		get;
		set
		{
			if (field == value) return;
			field = value;
			RefreshForTime();
		}
	}

	// a keyframe arrow wants the playhead moved
	public event EventHandler<TimeSpan> SeekRequested;

	// something was edited here - live, during a drag, as much as on a
	// commit. the page may need to redraw what it shows of the same objects
	public event EventHandler Edited;

	// a value settled on a row, with what it replaced
	public event EventHandler<InspectorEditArgs> ValueCommitted;

	public override void _ExitTree()
	{
		if (History is History h) h.Changed -= OnHistoryChanged;
	}

	// ---- the parts, from their scenes ----

	public PackedScene EditorSceneFor(PropertyEditor editor) => editor switch
	{
		PropertyEditor.Toggle => toggleEditor,
		PropertyEditor.Number or PropertyEditor.Slider or PropertyEditor.Percent => numberEditor,
		PropertyEditor.Angle => angleEditor,
		PropertyEditor.Text => textEditor,
		PropertyEditor.Multiline => multilineEditor,
		PropertyEditor.Path => pathEditor,
		PropertyEditor.Dropdown => dropdownEditor,
		PropertyEditor.Color => colorEditor,
		PropertyEditor.Vector => vectorEditor,
		PropertyEditor.Time => timeEditor,
		_ => labelEditor
	};

	internal InspectorSection CreateSection(string title, Color? accent, bool nested)
	{
		InspectorSection section = (nested ? subsectionScene : sectionScene).Instantiate<InspectorSection>();
		section.Title = title;
		section.Accent = accent;
		return section;
	}

	internal InspectorRow CreateRow(string label, EditorSpec spec, IReadOnlyList<Binding> bindings)
	{
		InspectorRow row = rowScene.Instantiate<InspectorRow>();
		row.Configure(this, label, spec, bindings);
		return row;
	}

	internal ListRow CreateListRow(string label, PropertyDescriptor descriptor, PropertyBinding binding)
	{
		ListRow row = listRowScene.Instantiate<ListRow>();
		row.Configure(this, label, descriptor, binding);
		return row;
	}

	internal Button CreateRemoveButton()
		=> removeButtonScene?.Instantiate<Button>() ?? new Button { Text = "×", ThemeTypeVariation = "InspectorButton" };

	// ---- what is shown ----

	public void Clear() => Show([]);

	public void Show(IReadOnlyList<InspectorSectionSpec> sections)
	{
		foreach (Node child in content.GetChildren())
		{
			if (child != empty) child.QueueFree();
		}

		empty.Visible = sections.Count == 0;

		foreach (InspectorSectionSpec spec in sections)
		{
			InspectorSection section = CreateSection(spec.Title, spec.Accent, nested: false);
			content.AddChild(section);

			// an Enabled shared by every object goes on the header as a
			// switch rather than in the body as a row
			PropertyDescriptor enabled = HeaderToggle(spec.Targets);
			if (enabled is not null) section.BindToggle(this, enabled, spec.Targets);

			BuildRows(section.Body, spec.Targets, skip: enabled?.Name);
		}
	}

	// the bool property named Enabled, when every object has one
	static PropertyDescriptor HeaderToggle(IReadOnlyList<InspectorTarget> targets)
	{
		if (targets.Count == 0) return null;

		PropertyDescriptor first = Inspect.Of(targets[0].Object).FirstOrDefault(d => d.Name == "Enabled" && d.ValueType == typeof(bool) && !d.IsReadOnly);
		if (first is null) return null;

		for (int i = 1; i < targets.Count; i++)
		{
			if (!Inspect.Of(targets[i].Object).Any(d => d.Name == "Enabled" && d.ValueType == typeof(bool))) return null;
		}

		return first;
	}

	// clips: the clip itself, then each node of its graph in graph order,
	// each a section. several clips: only what they all have - the same
	// node type at the same place in the graph - edited together
	public void ShowClips(IReadOnlyList<Clip> clips)
	{
		if (clips is null || clips.Count == 0) { Clear(); return; }

		List<InspectorSectionSpec> sections = [];

		string title = clips.Count == 1 ? clips[0].Name : $"{clips.Count} clips";
		sections.Add(new(title, [.. clips.Select(c => new InspectorTarget(c, c))], ClipAccent(clips[0])));

		int count = clips.Min(c => c.Graph.Nodes.Count);

		for (int i = 0; i < count; i++)
		{
			EditSharp.Components.Nodes.Node first = clips[0].Graph.Nodes[i];

			if (first is EditSharp.Components.Nodes.OutputNode) continue;
			if (clips.Any(c => c.Graph.Nodes[i].GetType() != first.GetType())) continue;

			sections.Add(new(
				NodeCategory.Title(first),
				[.. clips.Select(c => new InspectorTarget(c.Graph.Nodes[i], c))],
				GetThemeColor(NodeCategory.Of(first), "Node")));
		}

		Show(sections);
	}

	Color ClipAccent(Clip clip) => GetThemeColor(clip is AudioClip ? "audio" : "video", "Clip");

	// the rows for a set of objects edited together: one per property they
	// all have, grouped as their attributes say, objects folded inline,
	// lists as lists
	internal void BuildRows(VBoxContainer into, IReadOnlyList<InspectorTarget> targets, string skip = null)
	{
		if (targets.Count == 0) return;

		// the descriptors of the first, kept only where every other object
		// has one by the same name
		List<PropertyDescriptor> shared = [.. Inspect.Of(targets[0].Object).Where(d => d.Name != skip)];

		for (int i = 1; i < targets.Count; i++)
		{
			HashSet<string> names = [.. Inspect.Of(targets[i].Object).Select(d => d.Name)];
			shared.RemoveAll(d => !names.Contains(d.Name));
		}

		Dictionary<string, InspectorSection> groups = [];

		foreach (PropertyDescriptor descriptor in shared.OrderBy(d => d.Order))
		{
			VBoxContainer parent = into;

			if (!string.IsNullOrEmpty(descriptor.Group))
			{
				if (!groups.TryGetValue(descriptor.Group, out InspectorSection group))
				{
					group = CreateSection(descriptor.Group, null, nested: true);
					into.AddChild(group);
					groups[descriptor.Group] = group;
				}

				parent = group.Body;
			}

			parent.AddChild(BuildRow(descriptor, targets));
		}
	}

	Control BuildRow(PropertyDescriptor descriptor, IReadOnlyList<InspectorTarget> targets)
	{
		// a list: one object at a time
		if (descriptor.IsCollection)
		{
			if (targets.Count == 1)
				return CreateListRow(descriptor.DisplayName, descriptor, new PropertyBinding(descriptor, targets[0].Object) { Clip = targets[0].Clip });

			return CreateRow(descriptor.DisplayName, EditorSpec.Of(descriptor) with { Editor = PropertyEditor.Auto, ReadOnly = true }, [.. targets.Select(t => new PropertyBinding(descriptor, t.Object) { Clip = t.Clip })]);
		}

		// an object with properties of its own: folded inline
		bool objectLike = descriptor.Editor is PropertyEditor.Object or PropertyEditor.Media or PropertyEditor.Timeline;

		if (objectLike && Inspect.Of(descriptor.ValueType).Count > 0)
		{
			List<InspectorTarget> children = [];

			foreach (InspectorTarget t in targets)
			{
				object child = descriptor.GetValue(t.Object);
				if (child is not null) children.Add(new InspectorTarget(child, t.Clip));
			}

			InspectorSection section = CreateSection(descriptor.DisplayName, null, nested: true);

			if (children.Count == targets.Count) BuildRows(section.Body, children);
			else section.Body.AddChild(new Label { Text = "(none)", ThemeTypeVariation = "InspectorLabel" });

			return section;
		}

		EditorSpec spec = EditorSpec.Of(descriptor);

		// a string the object offers choices for is a dropdown of them
		if (targets[0].Object is IChoiceProvider provider && provider.ChoicesFor(descriptor.Name) is IReadOnlyList<string> choices)
			spec = spec with { Editor = PropertyEditor.Dropdown, Choices = [.. choices] };

		return CreateRow(descriptor.DisplayName, spec, [.. targets.Select(t => new PropertyBinding(descriptor, t.Object) { Clip = t.Clip })]);
	}

	// ---- keeping up with the data ----

	public void RefreshValues()
	{
		if (content is null) return;
		foreach (Node child in content.GetChildren()) RefreshNode(child);
	}

	void RefreshForTime()
	{
		if (content is null) return;
		foreach (Node child in content.GetChildren()) RefreshNodeForTime(child);
	}

	internal static void RefreshNode(Node node)
	{
		switch (node)
		{
			case InspectorRow row: row.Refresh(); break;
			case ListRow list: list.Refresh(); break;
			case InspectorSection section: section.RefreshToggle(); foreach (Node child in section.Body.GetChildren()) RefreshNode(child); break;
		}
	}

	internal static void RefreshNodeForTime(Node node)
	{
		switch (node)
		{
			case InspectorRow row: row.RefreshForTime(); break;
			case ListRow list: list.RefreshForTime(); break;
			case InspectorSection section: foreach (Node child in section.Body.GetChildren()) RefreshNodeForTime(child); break;
		}
	}

	// an undo, a redo, or a change made somewhere else: re-read everything.
	// a commit made here re-reads too - the row that made it is already
	// showing the value, and the rest may depend on it
	void OnHistoryChanged(object sender, HistoryEventArgs e) => RefreshValues();

	// a history scope for an edit made here; null when there is no history
	// to record into, in which case the edit still happens, unrecorded
	internal Transaction.Scope BeginChange(string description) => History?.Begin(description);

	internal void NotifyEdited() => Edited?.Invoke(this, EventArgs.Empty);

	internal void NotifyCommitted(InspectorEditArgs e) => ValueCommitted?.Invoke(this, e);

	internal void RequestSeek(TimeSpan time) => SeekRequested?.Invoke(this, time);
}
