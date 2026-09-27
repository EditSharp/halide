#if TOOLS
using Halide.Scripts.UI.Theming;
using Godot;
using System.Collections.Generic;
using System.Linq;

// the Node Theme tab: for the selected control - local or remote - the
// theme pages of the variation it wears and of the class beneath it, each
// in one of godot's own inspectors. a type's styleboxes share one
// ThemeStyle, so that appears once, with its picks and shape; its colour
// and font bindings and its constants come as one proxy object. every page
// is a foldable section that does not scroll on its own, so the whole dock
// is one list under one scrollbar. edits are undoable through the editor
// and the theme file is saved at once. every signal here is connected by
// method name, so the connections survive the assembly reload a build causes
[Tool]
public partial class NodeThemeDock : VBoxContainer
{
	[Export] Label header;
	[Export] ScrollContainer scroll;
	[Export] VBoxContainer pages;

	readonly List<string> shownTypes = [];
	readonly List<TypeThemeProxy> proxies = [];

	// the sections the user folded away, by title: a page coming back under
	// a title folded before comes back folded
	readonly HashSet<string> folded = [];
	string shownTitle;

	// the theme the edited scene's controls are drawn with, so an edit shows
	// where it is made; the file is the fallback outside a running editor
	public static EditSharpTheme ProjectTheme
	{
		get
		{
			if (ThemeDB.GetProjectTheme() is EditSharpTheme live) return live;

			string path = ProjectSettings.GetSetting("gui/theme/custom").AsString();
			return string.IsNullOrEmpty(path) ? null : ResourceLoader.Load<Theme>(path) as EditSharpTheme;
		}
	}

	public override void _Ready() => ShowNothing();

	public override void _ExitTree() => FreeProxies();

	// the object the editor's inspector is showing: a control in the open
	// scene, or a remote one from the running app
	public void ShowFor(GodotObject edited)
	{
		if (edited is null) { ShowNothing(); return; }

		string cls;
		string variation;

		if (edited.GetClass() == "EditorDebuggerRemoteObject")
		{
			cls = edited.Get("type_name").AsString();
			variation = edited.Get("theme_type_variation").AsString();
		}
		else if (edited is Control control)
		{
			cls = control.GetClass();
			variation = control.ThemeTypeVariation;
		}
		else { ShowNothing(); return; }

		if (!ClassDB.IsParentClass(cls, "Control")) { ShowNothing(); return; }

		EditSharpTheme theme = ProjectTheme;
		if (theme is null) { ShowNothing(); return; }

		string[] known = theme.GetTypeList();
		List<string> types = [];

		if (!string.IsNullOrEmpty(variation) && known.Contains(variation)) types.Add(variation);

		for (string c = cls; !string.IsNullOrEmpty(c) && c != "Node"; c = ClassDB.GetParentClass(c))
		{
			if (known.Contains(c) && !types.Contains(c)) types.Add(c);
		}

		ShowTypes(string.IsNullOrEmpty(variation) ? cls : $"{variation}  ({cls})", types, theme);
	}

	public void ShowType(string type) => ShowTypes(type, [type], ProjectTheme);

	void ShowTypes(string title, IReadOnlyList<string> types, EditSharpTheme theme)
	{
		Clear();
		shownTypes.AddRange(types);
		shownTitle = title;
		header.Text = title;

		if (theme is null) return;

		foreach (string type in types)
		{
			GiveStyles(theme, type);

			// the styles the type's styleboxes share, each once
			List<ThemeStyle> styles = [];

			foreach (string item in theme.GetStyleboxList(type))
			{
				if (theme.GetStylebox(item, type) is ThemedStyleBox box && box.Style is ThemeStyle style && !styles.Contains(style))
					styles.Add(style);
			}

			foreach (ThemeStyle style in styles)
			{
				string items = string.Join(", ", theme.GetStyleboxList(type).Where(i => theme.GetStylebox(i, type) is ThemedStyleBox b && b.Style == style));
				AddPage($"{type}  style  ({items})", style);
			}

			TypeThemeProxy proxy = new(theme, type);
			proxies.Add(proxy);
			AddPage($"{type}  colours, font, constants", proxy);
		}
	}

	// ---- the families: the stylebox items that are one look ----

	// a type's items that are one look worn in several states: the base item
	// holds the style and the rest share it, each in its state. a type keeps
	// whichever families it has a base item for; everything outside them - a
	// panel, a separator, a slider's track - is a look of its own
	static readonly (string Base, (string Item, StyleState State)[] Members)[] Families =
	[
		new("normal",
		[
			("hover", StyleState.Hover),
			("pressed", StyleState.Pressed),
			("hover_pressed", StyleState.Pressed),
			("disabled", StyleState.Disabled),
			("read_only", StyleState.Disabled),
			("focus", StyleState.Focus)
		]),

		// a tab bar's tabs: the unselected tab is the look and the selected
		// one wears it in the pressed shade, the way an on button does
		new("tab_unselected",
		[
			("tab_hovered", StyleState.Hover),
			("tab_selected", StyleState.Pressed),
			("tab_disabled", StyleState.Disabled),
			("tab_focus", StyleState.Focus)
		]),

		// a scrollbar's or slider's grabber, the filled part of a slider's
		// track, and a scrollbar's track
		new("grabber",
		[
			("grabber_highlight", StyleState.Hover),
			("grabber_pressed", StyleState.Pressed),
			("grabber_disabled", StyleState.Disabled)
		]),
		new("grabber_area", [("grabber_area_highlight", StyleState.Hover)]),
		new("scroll", [("scroll_focus", StyleState.Focus)])
	];

	// a ThemedStyleBox added in the theme editor comes with no style inside
	// and draws nothing; it gets one here so there is something to edit. a
	// family's items are pointed at their base item's style, in their state,
	// every time the type is shown - sharing one look is the invariant this
	// dock keeps, not something the theme editor has to be told item by item
	static void GiveStyles(EditSharpTheme theme, string type)
	{
		bool changed = false;
		HashSet<string> present = [.. theme.GetStyleboxList(type)];
		HashSet<string> familied = [];

		foreach ((string head, (string Item, StyleState State)[] members) in Families)
		{
			if (!present.Contains(head) || theme.GetStylebox(head, type) is not ThemedStyleBox shared) continue;

			familied.Add(head);

			if (shared.Style is null) { shared.Style = new ThemeStyle { Palette = theme.Palette }; changed = true; }
			if (shared.State != StyleState.Normal) { shared.State = StyleState.Normal; changed = true; }

			foreach ((string item, StyleState state) in members)
			{
				if (!present.Contains(item) || theme.GetStylebox(item, type) is not ThemedStyleBox box) continue;

				familied.Add(item);
				if (box.Style != shared.Style) { box.Style = shared.Style; changed = true; }
				if (box.State != state) { box.State = state; changed = true; }
			}
		}

		foreach (string item in present)
		{
			if (familied.Contains(item) || theme.GetStylebox(item, type) is not ThemedStyleBox box || box.Style is not null) continue;
			box.Style = new ThemeStyle { Palette = theme.Palette };
			changed = true;
		}

		if (changed) Save(theme);
	}

	// ---- the pages ----

	// the sections this dock holds, in the order they are shown
	public IEnumerable<FoldableContainer> Sections => pages.GetChildren().OfType<FoldableContainer>();

	// the inspectors this dock holds, for tests and for the plugin
	public IEnumerable<EditorInspector> Inspectors => Sections.SelectMany(section => section.GetChildren().OfType<EditorInspector>());

	void AddPage(string title, GodotObject target)
	{
		FoldableContainer section = new()
		{
			Title = title,
			Folded = folded.Contains(title),
			TitleTextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
		};
		pages.AddChild(section);

		// the page is not a scroll view of its own: with scrolling off the
		// inspector asks for the height of everything in it, and the one
		// scrollbar outside does the scrolling
		EditorInspector inspector = new()
		{
			VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			SizeFlagsVertical = SizeFlags.ShrinkBegin
		};
		section.AddChild(inspector);
		inspector.Edit(target);

		// every edit goes to the file at once; undo is the editor's own
		inspector.Connect(EditorInspector.SignalName.PropertyEdited, new Callable(this, MethodName.OnPropertyEdited));
	}

	void OnPropertyEdited(string property) => Save(ProjectTheme);

	// what is folded right now, so the next build can put it back. read off
	// the sections rather than followed by signal: the dock is built afresh
	// after every assembly reload, and this survives that
	void RememberFolds()
	{
		foreach (FoldableContainer section in Sections)
		{
			if (section.Folded) folded.Add(section.Title);
			else folded.Remove(section.Title);
		}
	}

	void Clear()
	{
		RememberFolds();
		shownTypes.Clear();
		shownTitle = null;

		// an inspector lets go of its object before the object goes, or it
		// tries to disconnect from it later
		foreach (EditorInspector inspector in Inspectors) inspector.Edit(null);
		foreach (Node child in pages.GetChildren()) { pages.RemoveChild(child); child.QueueFree(); }
		FreeProxies();
	}

	// proxies are plain objects, owned by nobody: freed here
	void FreeProxies()
	{
		foreach (TypeThemeProxy proxy in proxies) if (GodotObject.IsInstanceValid(proxy)) proxy.Free();
		proxies.Clear();
	}

	public void ShowNothing()
	{
		Clear();
		header.Text = "Select a control";
	}

	public void Refresh()
	{
		if (shownTypes.Count > 0) ShowTypes(shownTitle ?? header.Text, [.. shownTypes], ProjectTheme);
	}

	public static void Save(EditSharpTheme theme)
	{
		if (theme is null || string.IsNullOrEmpty(theme.ResourcePath)) return;
		Error error = ResourceSaver.Save(theme, theme.ResourcePath);
		if (error != Error.Ok) GD.PushError($"Could not save the theme: {error}");
	}
}
#endif
