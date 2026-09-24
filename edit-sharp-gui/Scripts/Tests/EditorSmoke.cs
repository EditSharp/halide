#if TOOLS
using EditSharpGUI.Scripts.UI.Theming;
using Godot;
using System.Linq;

// the editor-side smoke test: drives the Node Theme dock the way a click
// does - an EditorInspector edit of a style and of a binding - and checks
// the change reaches the live theme, the styleboxes that share the style
// and the file. the plugin runs it when the editor is started with
//   godot --headless --editor --path . -- --editor-smoke
// and quits the editor with the result as the exit code
public static class EditorSmoke
{
	public const string Flag = "--editor-smoke";

	static int failures;

	public static bool Wanted => OS.GetCmdlineUserArgs().Contains(Flag);

	static void Check(bool ok, string what)
	{
		GD.Print((ok ? "EDITOR PASS " : "EDITOR FAIL ") + what);
		if (!ok) failures++;
	}

	public static void Run()
	{
		failures = 0;
		EditSharpTheme theme = NodeThemeDock.ProjectTheme;
		Check(theme is not null, "the editor has the project theme");
		if (theme is null) { Finish(); return; }

		Check(ThemeDB.GetProjectTheme() == theme, "and it is the instance the edited scene draws with");

		NodeThemeDock dock = GD.Load<PackedScene>("res://addons/edit_sharp_theme/NodeThemeDock.tscn").Instantiate<NodeThemeDock>();
		EditorInterface.Singleton.GetBaseControl().AddChild(dock);
		dock.ShowType("InspectorSection");

		EditorInspector[] inspectors = dock.Inspectors.ToArray();
		Check(inspectors.Length == 2, $"the section type shows a style page and a bindings page: {inspectors.Length}");

		// the pages are one list under the dock's own scrollbar, not a
		// column of little scroll views sharing the height between them
		Check(inspectors.All(i => i.VerticalScrollMode == ScrollContainer.ScrollMode.Disabled), "the pages do not scroll on their own");
		Check(inspectors.All(i => i.GetCombinedMinimumSize().Y > 0f), $"so each page asks for the height of everything in it: {string.Join(", ", inspectors.Select(i => i.GetCombinedMinimumSize().Y))}");
		Check(dock.Sections.Count() == inspectors.Length, $"and each sits in a foldable section: {dock.Sections.Count()}");

		ThemedStyleBox normal = theme.GetStylebox("normal", "InspectorSection") as ThemedStyleBox;
		ThemeStyle style = normal?.Style;
		Check(style is not null, "the section's normal box has a style");

		if (style is not null && inspectors.Length == 2)
		{
			int was = style.CornerRadiusTopLeft;
			int hoverWas = ((ThemedStyleBox)theme.GetStylebox("hover", "InspectorSection")).MakeFlat().CornerRadiusTopLeft;

			// an edit the way the inspector makes one: through its property editor
			EditorProperty radius = Find(inspectors[0], "CornerRadiusTopLeft");
			Check(radius is not null, "the style page has a corner radius editor");

			if (radius is not null)
			{
				radius.EmitChanged("CornerRadiusTopLeft", was + 7);
				Check(style.CornerRadiusTopLeft == was + 7, $"the edit reached the style: {style.CornerRadiusTopLeft}");
				Check(normal.MakeFlat().CornerRadiusTopLeft == was + 7, "and the normal box draws it");
				Check(((ThemedStyleBox)theme.GetStylebox("hover", "InspectorSection")).MakeFlat().CornerRadiusTopLeft == was + 7 && hoverWas == was, "and so does the hover box sharing the style");

				string saved = FileAccess.GetFileAsString("res://main_theme.tres");
				Check(saved.Contains($"CornerRadiusTopLeft = {was + 7}"), "and the file was saved with it");

				radius.EmitChanged("CornerRadiusTopLeft", was);
				Check(style.CornerRadiusTopLeft == was, "put back");
			}

			EditorProperty weight = Find(inspectors[1], "font/weight");
			Check(weight is not null, "the bindings page has a font weight editor");

			if (weight is not null)
			{
				FontBinding binding = theme.FindFontBinding("InspectorSection");
				ThemeFontWeight wasWeight = binding?.Weight ?? ThemeFontWeight.Regular;
				ThemeFontFamily wasFamily = binding?.Family ?? ThemeFontFamily.None;

				weight.EmitChanged("font/weight", (int)ThemeFontWeight.Bold);
				binding = theme.FindFontBinding("InspectorSection");
				Check(binding is not null && binding.Weight == ThemeFontWeight.Bold && binding.Family != ThemeFontFamily.None, "picking a weight makes a UI font binding");
				Check(theme.GetFont("font", "InspectorSection") == theme.Palette.ResolveFont(ThemeFontFamily.UI, ThemeFontWeight.Bold), "and the type's font item is the bold variation");

				binding.Weight = wasWeight;
				binding.Family = wasFamily;
				theme.ApplyBindings();
			}
		}

		// a section folded away comes back folded when the dock is rebuilt
		FoldableContainer[] sections = dock.Sections.ToArray();

		if (sections.Length == 2)
		{
			sections[1].Folded = true;
			dock.ShowType("InspectorSection");
			FoldableContainer[] again = dock.Sections.ToArray();
			Check(again.Length == 2 && again[1].Folded && !again[0].Folded, "a folded section comes back folded and its neighbour does not");
			again[1].Folded = false;
		}

		// the tab family is one look: the unselected tab holds the style and
		// the rest of the tab items share it, each in its state
		dock.ShowType("TabContainer");
		ThemedStyleBox unselected = theme.GetStylebox("tab_unselected", "TabContainer") as ThemedStyleBox;
		ThemedStyleBox selected = theme.GetStylebox("tab_selected", "TabContainer") as ThemedStyleBox;
		ThemedStyleBox hovered = theme.GetStylebox("tab_hovered", "TabContainer") as ThemedStyleBox;
		Check(unselected?.Style is not null && unselected.State == StyleState.Normal, "the unselected tab holds the tab family's style");
		Check(selected?.Style == unselected?.Style && selected?.State == StyleState.Pressed, "the selected tab shares it, pressed");
		Check(hovered?.Style == unselected?.Style && hovered?.State == StyleState.Hover, "the hovered tab shares it, hovered");
		Check((theme.GetStylebox("panel", "TabContainer") as ThemedStyleBox)?.Style != unselected?.Style, "and the panel keeps a look of its own");
		Check(dock.Inspectors.Count() == 4, $"so the type shows three style pages and a bindings page: {dock.Inspectors.Count()}");

		// a box added in the theme editor has no style: showing its type gives it one, shared by its states
		theme.SetStylebox("normal", "ProbeEmpty", new ThemedStyleBox());
		theme.SetStylebox("hover", "ProbeEmpty", new ThemedStyleBox());
		dock.ShowType("ProbeEmpty");
		ThemedStyleBox probeNormal = theme.GetStylebox("normal", "ProbeEmpty") as ThemedStyleBox;
		ThemedStyleBox probeHover = theme.GetStylebox("hover", "ProbeEmpty") as ThemedStyleBox;
		Check(probeNormal?.Style is not null && probeNormal.Style.Palette == theme.Palette, "an empty box gets a style on the theme's palette");
		Check(probeHover?.Style == probeNormal?.Style && probeHover?.State == StyleState.Hover, "and its hover box shares it in the hover state");
		Check(dock.Inspectors.Count() == 2, $"and the dock shows its style page: {dock.Inspectors.Count()}");
		dock.ShowType("Button");
		dock.ShowNothing();
		theme.RemoveType("ProbeEmpty");

		NodeThemeDock.Save(theme);
		dock.QueueFree();
		Finish();
	}

	static EditorProperty Find(Node root, string property)
	{
		if (root is EditorProperty p && p.GetEditedProperty() == property) return p;
		foreach (Node child in root.GetChildren()) if (Find(child, property) is EditorProperty found) return found;
		return null;
	}

	static void Finish()
	{
		GD.Print(failures == 0 ? "EDITOR OK" : $"EDITOR FAILED ({failures})");
		EditorInterface.Singleton.GetBaseControl().GetTree().Quit(failures == 0 ? 0 : 1);
	}
}
#endif
