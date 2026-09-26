using EditSharp.Components.Clips;
using EditSharp.Components.Media;
using EditSharp.Components.Nodes.Input;
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
// shown once and an edit writes to all of them. Kinds, when given, puts a
// picker of them on the header that switches every object (a node in its
// graph) to the kind chosen
public sealed record InspectorSectionSpec(string Title, IReadOnlyList<InspectorTarget> Targets, Color? Accent = null, IReadOnlyList<EditSharp.Components.Nodes.NodeKindInfo> Kinds = null);

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

	// the project's media, for a media property's picker; null offers only
	// bringing a file in
	public MediaLibrary Media { get; set; }

	// for time fields shown as frames
	public int Framerate { get; set; } = 30;

	// time fields as frame counts rather than clocks. toggled from any
	// time field, for all of them
	public bool ShowFrames
	{
		get;
		set { field = value; RefreshValues(); }
	}

	// the render resolution, for frame-relative values shown in pixels
	public Vector2I FrameSize { get; set; } = new(1920, 1080);

	// frame-relative values (positions, sizes, blur radii) in pixels rather
	// than fractions of the frame. toggled from any of them, for all of them
	public bool ShowPixels
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

	// a click on nothing in here - between rows, on a label, below the
	// last section - takes focus out of the field being typed in, as a
	// click on empty timeline drops the selection
	public override void _Input(InputEvent e)
	{
		if (Engine.IsEditorHint() || e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click) return;
		if (!IsVisibleInTree() || !GetGlobalRect().HasPoint(click.Position)) return;

		Control hovered = GetViewport().GuiGetHoveredControl();
		if (hovered is null || !IsAncestorOf(hovered) && hovered != this) return;

		if (hovered is Container or Label or Panel or ColorRect || hovered == this)
			GetViewport().GuiReleaseFocus();
	}

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
		section.Nested = nested;
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

	internal ListRow CreateListRow(string label, PropertyDescriptor descriptor, IReadOnlyList<PropertyBinding> bindings)
	{
		ListRow row = listRowScene.Instantiate<ListRow>();
		row.Configure(this, label, descriptor, bindings);
		return row;
	}

	internal Button CreateRemoveButton()
		=> removeButtonScene?.Instantiate<Button>() ?? new Button { Text = "×", ThemeTypeVariation = "InspectorButton" };

	// ---- what is shown ----

	// a show is planned as data first and then reconciled onto the tree:
	// a part already there that would be built the same way is kept and
	// only re-pointed at the new objects, so a selection that grows by a
	// clip re-reads the rows instead of instantiating every scene again.
	// that is the difference between a rubber-band that keeps up and one
	// that stalls every time it takes in another clip

	abstract record Plan;

	// a section: what its header shows, the switch and the picker on it,
	// and its body
	sealed record SectionPlan(string Title, Color? Accent, bool Nested, PropertyDescriptor Toggle, IReadOnlyList<InspectorTarget> ToggleTargets, IReadOnlyList<Plan> Body, KindPlan Kind = null, MediaPlan Media = null) : Plan;

	// an input node section's kind picker: the nodes, the kinds offered
	sealed record KindPlan(IReadOnlyList<InspectorTarget> Targets, IReadOnlyList<EditSharp.Components.Nodes.NodeKindInfo> Kinds);

	// a media property's picker: the property, the objects holding it
	sealed record MediaPlan(PropertyDescriptor Descriptor, IReadOnlyList<InspectorTarget> Targets);

	// a row is the same row whenever it edits the same kind of value under
	// the same name - the objects behind it are only ever bindings
	sealed record RowPlan(string Label, EditorSpec Spec, IReadOnlyList<Binding> Bindings) : Plan;

	sealed record ListPlan(string Label, PropertyDescriptor Descriptor, IReadOnlyList<PropertyBinding> Bindings) : Plan;

	// a line of text where rows would go: "(none)"
	sealed record NotePlan(string Text) : Plan;

	public void Clear() => Show([]);

	IReadOnlyList<InspectorSectionSpec> shown = [];

	// the clips the sections were made from, when they were; a re-plan
	// reads their graphs again, since a switch puts new nodes in them
	IReadOnlyList<Clip> shownClips;

	public void Show(IReadOnlyList<InspectorSectionSpec> sections)
	{
		shownClips = null;
		ShowSections(sections);
	}

	void ShowSections(IReadOnlyList<InspectorSectionSpec> sections)
	{
		shown = sections;
		empty.Visible = sections.Count == 0;

		Reconcile(content, [.. sections.Select(PlanSection)]);
	}

	SectionPlan PlanSection(InspectorSectionSpec spec)
	{
		// an Enabled shared by every object goes on the header as a
		// switch rather than in the body as a row
		PropertyDescriptor enabled = HeaderToggle(spec.Targets);

		return new(spec.Title, spec.Accent, Nested: false, enabled, spec.Targets, PlanRows(spec.Targets, skip: enabled?.Name),
			spec.Kinds is null ? null : new KindPlan(spec.Targets, spec.Kinds));
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
	// node type at the same place in the graph - edited together. input
	// nodes are the exception: inputs of one kind of graph get a kind
	// picker, and while their kinds differ the section shows only what
	// every input has, so the picker can switch them all
	public void ShowClips(IReadOnlyList<Clip> clips)
	{
		if (clips is null || clips.Count == 0) { Clear(); return; }

		shownClips = clips;

		List<InspectorSectionSpec> sections = [];

		string title = clips.Count == 1 ? clips[0].Name : $"{clips.Count} clips";
		sections.Add(new(title, [.. clips.Select(c => new InspectorTarget(c, c))], ClipAccent(clips[0])));

		int count = clips.Min(c => c.Graph.Nodes.Count);

		for (int i = 0; i < count; i++)
		{
			EditSharp.Components.Nodes.Node first = clips[0].Graph.Nodes[i];

			if (first is EditSharp.Components.Nodes.OutputNode) continue;

			bool same = clips.All(c => c.Graph.Nodes[i].GetType() == first.GetType());

			Type inputBase = first is VideoInputNode ? typeof(VideoInputNode) : first is AudioInputNode ? typeof(AudioInputNode) : null;
			if (!same && (inputBase is null || clips.Any(c => !inputBase.IsInstanceOfType(c.Graph.Nodes[i])))) continue;

			sections.Add(new(
				same ? NodeCategory.Title(first) : "Input",
				[.. clips.Select(c => new InspectorTarget(c.Graph.Nodes[i], c))],
				GetThemeColor(NodeCategory.Of(first), "Node"),
				inputBase is null ? null : EditSharp.Components.Nodes.NodeKinds.For(inputBase)));
		}

		ShowSections(sections);
	}

	Color ClipAccent(Clip clip) => GetThemeColor(clip is AudioClip ? "audio" : "video", "Clip");

	// media from the library: one section of what they all have, named for
	// the one media or the count
	public void ShowMedia(IReadOnlyList<IMedia> media)
	{
		if (media is null || media.Count == 0) { Clear(); return; }

		string title = media.Count == 1 ? media[0].Name : $"{media.Count} media";
		Show([new InspectorSectionSpec(title, [.. media.Select(m => new InspectorTarget(m))])]);
	}

	// the rows for a set of objects edited together, built into a body that
	// is already in the tree - a list's item, from ListRow
	internal void BuildRows(VBoxContainer into, IReadOnlyList<InspectorTarget> targets, string skip = null)
		=> Reconcile(into, PlanRows(targets, skip));

	// the same objects again, for when what they have has changed shape -
	// a node switched to another kind, a media changed, or either undone.
	// clips are read again, since their graphs hold the new nodes
	internal void Replan()
	{
		if (shownClips is not null) ShowClips(shownClips);
		else ShowSections(shown);
	}

	// one row per property they all have, grouped as their attributes say,
	// objects folded inline, lists as lists. `only`, when given, keeps just
	// those names
	List<Plan> PlanRows(IReadOnlyList<InspectorTarget> targets, string skip = null, IReadOnlySet<string> only = null)
	{
		List<Plan> plans = [];
		if (targets.Count == 0) return plans;

		// the descriptors of the first, kept only where every other object
		// has one by the same name
		List<PropertyDescriptor> shared = [.. Inspect.Of(targets[0].Object).Where(d => d.Name != skip && (only is null || only.Contains(d.Name)))];

		for (int i = 1; i < targets.Count; i++)
		{
			HashSet<string> names = [.. Inspect.Of(targets[i].Object).Select(d => d.Name)];
			shared.RemoveAll(d => !names.Contains(d.Name));
		}

		// a group is a nested section, placed where its first member is
		Dictionary<string, List<Plan>> groups = [];

		foreach (PropertyDescriptor descriptor in shared.OrderBy(d => d.Order))
		{
			List<Plan> into = plans;

			if (!string.IsNullOrEmpty(descriptor.Group))
			{
				if (!groups.TryGetValue(descriptor.Group, out List<Plan> body))
				{
					body = [];
					groups[descriptor.Group] = body;
					plans.Add(new SectionPlan(descriptor.Group, null, Nested: true, null, null, body));
				}

				into = body;
			}

			into.Add(PlanRow(descriptor, targets));
		}

		return plans;
	}

	Plan PlanRow(PropertyDescriptor descriptor, IReadOnlyList<InspectorTarget> targets)
	{
		// a list: values edit across every object, objects one at a time
		if (descriptor.IsCollection)
		{
			bool objectItems = descriptor.ItemEditor is PropertyEditor.Object or PropertyEditor.Media && descriptor.ItemValueType is not null && Inspect.Of(descriptor.ItemValueType).Count > 0;

			if (targets.Count == 1 || !objectItems)
				return new ListPlan(descriptor.DisplayName, descriptor, [.. targets.Select(t => new PropertyBinding(descriptor, t.Object) { Clip = t.Clip })]);

			return new RowPlan(descriptor.DisplayName, EditorSpec.Of(descriptor) with { Editor = PropertyEditor.Auto, ReadOnly = true }, [.. targets.Select(t => new PropertyBinding(descriptor, t.Object) { Clip = t.Clip })]);
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

			// a media: a picker of the project's media on the header. the
			// rows are the media's own, shared by every clip reading it
			MediaPlan media = typeof(IMedia).IsAssignableFrom(descriptor.ValueType) ? new MediaPlan(descriptor, targets) : null;

			IReadOnlyList<Plan> body = children.Count == targets.Count ? PlanRows(children) : [new NotePlan("(none)")];

			return new SectionPlan(descriptor.DisplayName, null, Nested: true, null, null, body, Media: media);
		}

		EditorSpec spec = EditorSpec.Of(descriptor);

		// a string the object offers choices for is a dropdown of them
		if (targets[0].Object is IChoiceProvider provider && provider.ChoicesFor(descriptor.Name) is IReadOnlyList<Choice> choices)
			spec = spec with { Editor = PropertyEditor.Dropdown, Choices = [.. choices] };

		return new RowPlan(descriptor.DisplayName, spec, [.. targets.Select(t => new PropertyBinding(descriptor, t.Object) { Clip = t.Clip })]);
	}

	// ---- putting a plan on the tree ----

	// walks the plans against the children in order: a child that matches
	// its plan is updated in place, one that does not is replaced, and
	// whatever is left past the end goes. sections recurse into their
	// bodies, so a clip joining the selection touches only the rows whose
	// set of properties actually changed
	void Reconcile(Node into, IReadOnlyList<Plan> plans)
	{
		// the children still standing - not the empty notice, not anything
		// already on its way out
		List<Node> existing = [.. into.GetChildren().Where(n => n != empty && !n.IsQueuedForDeletion())];

		int i = 0;

		for (; i < plans.Count; i++)
		{
			Plan plan = plans[i];

			if (i < existing.Count && Matches(existing[i], plan))
			{
				Update(existing[i], plan);
				continue;
			}

			Node node = Build(plan);

			if (i < existing.Count)
			{
				Node old = existing[i];
				int at = old.GetIndex();

				// out of the tree now, not at the end of the frame, so the
				// slot it held is really free for the new one
				into.RemoveChild(old);
				old.QueueFree();

				into.AddChild(node);
				into.MoveChild(node, at);
				existing[i] = node;
			}
			else into.AddChild(node);
		}

		for (; i < existing.Count; i++)
		{
			into.RemoveChild(existing[i]);
			existing[i].QueueFree();
		}
	}

	// whether a part already built would be built the same way for this
	// plan - the same scene with the same editor - so it can be kept
	static bool Matches(Node node, Plan plan) => (node, plan) switch
	{
		(InspectorSection s, SectionPlan p) => s.Nested == p.Nested && (s.ToggleDescriptor is null) == (p.Toggle is null) && s.HasKindPicker == (p.Kind is not null) && (s.MediaDescriptor is null) == (p.Media is null),
		(InspectorRow r, RowPlan p) => r.Label == p.Label && SameSpec(r.Spec, p.Spec),
		(ListRow l, ListPlan p) => l.Descriptor == p.Descriptor && l.Label == p.Label,
		(Label t, NotePlan p) => t.Text == p.Text,
		_ => false
	};

	// records compare arrays by reference, and the choices are made anew
	// for every plan
	static bool SameSpec(EditorSpec a, EditorSpec b)
		=> (a with { Choices = null }) == (b with { Choices = null })
		&& (a.Choices is null ? b.Choices is null : b.Choices is not null && a.Choices.SequenceEqual(b.Choices));

	void Update(Node node, Plan plan)
	{
		switch (node, plan)
		{
			case (InspectorSection s, SectionPlan p):
				s.Title = p.Title;
				s.Accent = p.Accent;
				if (p.Toggle is not null) s.BindToggle(this, p.Toggle, p.ToggleTargets);
				if (p.Kind is not null) s.BindKindPicker(this, p.Kind.Targets, p.Kind.Kinds);
				if (p.Media is not null) s.BindMediaPicker(this, p.Media.Descriptor, p.Media.Targets);
				Reconcile(s.Body, p.Body);
				break;

			case (InspectorRow r, RowPlan p):
				r.Rebind(p.Bindings);
				break;

			case (ListRow l, ListPlan p):
				l.Rebind(p.Bindings);
				break;
		}
	}

	Node Build(Plan plan)
	{
		switch (plan)
		{
			case SectionPlan p:
			{
				InspectorSection section = CreateSection(p.Title, p.Accent, p.Nested);
				if (p.Toggle is not null) section.BindToggle(this, p.Toggle, p.ToggleTargets);
				if (p.Kind is not null) section.BindKindPicker(this, p.Kind.Targets, p.Kind.Kinds);
				if (p.Media is not null) section.BindMediaPicker(this, p.Media.Descriptor, p.Media.Targets);
				Reconcile(section.Body, p.Body);
				return section;
			}

			case RowPlan p:
				return CreateRow(p.Label, p.Spec, p.Bindings);

			case ListPlan p:
				return CreateListRow(p.Label, p.Descriptor, p.Bindings);

			case NotePlan p:
				return new Label { Text = p.Text, ThemeTypeVariation = "InspectorLabel" };

			default:
				throw new ArgumentOutOfRangeException(nameof(plan));
		}
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
			case InspectorSection section: section.RefreshToggle(); section.RefreshPicker(); foreach (Node child in section.Body.GetChildren()) RefreshNode(child); break;
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

	// an undo, a redo, or a change made somewhere else: re-read everything,
	// re-planning first only if a node or media shown was replaced (changes
	// can come a primitive at a time, too often to re-plan on each). a commit
	// made here re-reads too - the row that made it is already showing the
	// value, and the rest may depend on it
	void OnHistoryChanged(object sender, HistoryEventArgs e)
	{
		if (content is not null && (Replaced(content) || ChoicesStale(content))) Replan();
		RefreshValues();
	}

	static bool ChoicesStale(Node node) => node switch
	{
		InspectorRow row => row.ChoicesStale(),
		InspectorSection section => section.Body.GetChildren().Any(ChoicesStale),
		_ => node.GetChildren().Any(ChoicesStale)
	};

	static bool Replaced(Node node) => node switch
	{
		InspectorSection section => section.Replaced() || section.Body.GetChildren().Any(Replaced),
		_ => node.GetChildren().Any(Replaced)
	};

	// a history scope for an edit made here; null when there is no history
	// to record into, in which case the edit still happens, unrecorded
	internal Transaction.Scope BeginChange(string description) => History?.Begin(description);

	internal void NotifyEdited() => Edited?.Invoke(this, EventArgs.Empty);

	internal void NotifyCommitted(InspectorEditArgs e) => ValueCommitted?.Invoke(this, e);

	internal void RequestSeek(TimeSpan time) => SeekRequested?.Invoke(this, time);
}
