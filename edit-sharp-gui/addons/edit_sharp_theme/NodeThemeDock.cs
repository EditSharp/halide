#if TOOLS
using EditSharpGUI.Scripts.UI.Theming;
using Godot;
using System.Collections.Generic;
using System.Linq;

// the Node Theme tab: for the selected control - local or remote - the
// theme pages of the variation it wears and of the class beneath it, each
// in one of godot's own inspectors. a type's styleboxes share one
// ThemeStyle, so that appears once, with its picks and shape; its colour
// and font bindings and its constants come as one proxy object. edits are
// undoable through the editor and the theme file is saved at once. every
// signal here is connected by method name, so the connections survive
// the assembly reload a build causes
[Tool]
public partial class NodeThemeDock : VBoxContainer
{
	[Export] Label header;
	[Export] ScrollContainer scroll;
	[Export] VBoxContainer pages;

	readonly List<string> shownTypes = [];
	readonly List<TypeThemeProxy> proxies = [];
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

	static readonly string[] States = ["hover", "pressed", "hover_pressed", "disabled", "read_only", "focus"];

	// a ThemedStyleBox added in the theme editor comes with no style inside
	// and draws nothing; it gets one here so there is something to edit - the
	// state items share the normal one, in their state
	static void GiveStyles(EditSharpTheme theme, string type)
	{
		bool changed = false;
		ThemeStyle shared = theme.GetStylebox("normal", type) is ThemedStyleBox n ? n.Style : null;

		foreach (string item in theme.GetStyleboxList(type))
		{
			if (theme.GetStylebox(item, type) is not ThemedStyleBox box || box.Style is not null) continue;

			if (States.Contains(item) && shared is not null)
			{
				box.Style = shared;
				box.State = item switch
				{
					"hover" => StyleState.Hover,
					"pressed" or "hover_pressed" => StyleState.Pressed,
					"disabled" or "read_only" => StyleState.Disabled,
					"focus" => StyleState.Focus,
					_ => StyleState.Normal
				};
			}
			else
			{
				box.Style = new ThemeStyle { Palette = theme.Palette };
				if (item == "normal") shared = box.Style;
			}

			changed = true;
		}

		if (changed) Save(theme);
	}

	// the inspectors this dock holds, for tests and for the plugin
	public IEnumerable<EditorInspector> Inspectors => pages.GetChildren().OfType<EditorInspector>();

	void AddPage(string title, GodotObject target)
	{
		pages.AddChild(new Label { Text = title });

		EditorInspector inspector = new() { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new(0f, 24f) };
		pages.AddChild(inspector);
		inspector.Edit(target);

		// every edit goes to the file at once; undo is the editor's own
		inspector.Connect(EditorInspector.SignalName.PropertyEdited, new Callable(this, MethodName.OnPropertyEdited));
	}

	void OnPropertyEdited(string property) => Save(ProjectTheme);

	void Clear()
	{
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
