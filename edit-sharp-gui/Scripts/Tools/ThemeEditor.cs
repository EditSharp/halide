using EditSharpGUI.Scripts.Tools.ThemeEditing;
using EditSharpGUI.Scripts.UI.Theming;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// the theme tool: every type the theme styles listed on the left in
// groups, the selected one previewed in the middle - each of its items as
// a swatch, and a live instance to hover and click - and its editable
// properties on the right, in the app's own inspector. colours are picked
// as definitions; the definitions themselves, with real colour pickers,
// are the first entry in the list. edits restyle the live theme at once;
// Save writes main_theme.tres, Revert reloads it. run the scene:
//
//   Scenes/Tools/ThemeEditor.tscn
public partial class ThemeEditor : Control
{
	[Export] Label title;
	[Export] Button save;
	[Export] Button revert;
	[Export] Tree types;
	[Export] VBoxContainer preview;
	[Export] Inspector inspector;

	const string ThemePath = "res://main_theme.tres";
	public const string DefinitionsKey = "*definitions*";

	EditSharpTheme theme;
	bool dirty;
	string selected;

	public override void _Ready()
	{
		theme = ThemeDB.GetProjectTheme() as EditSharpTheme;

		if (theme is null)
		{
			title.Text = "The project theme is not an EditSharpTheme";
			save.Disabled = true;
			revert.Disabled = true;
			return;
		}

		// the file's own contents, not the user's preset on top of them:
		// this edits the file
		theme.ReloadFrom(ThemePath);

		save.Pressed += Save;
		revert.Pressed += Revert;
		types.ItemSelected += () => Select(types.GetSelected()?.GetMetadata(0).AsString());
		inspector.Edited += (_, _) => SetDirty(true);

		BuildTree();
		SetDirty(false);
		Select(DefinitionsKey);
	}

	// ---- the list ----

	void BuildTree()
	{
		types.Clear();
		types.HideRoot = true;
		TreeItem root = types.CreateItem();

		TreeItem definitions = types.CreateItem(root);
		definitions.SetText(0, "Definitions");
		definitions.SetMetadata(0, DefinitionsKey);

		Dictionary<string, List<string>> groups = new()
		{
			["Controls"] = [],
			["Views"] = [],
			["Inspector"] = []
		};

		foreach (string type in theme.GetTypeList().OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
			groups[GroupOf(type)].Add(type);

		foreach ((string name, List<string> members) in groups)
		{
			if (members.Count == 0) continue;

			TreeItem group = types.CreateItem(root);
			group.SetText(0, name);
			group.SetSelectable(0, false);

			foreach (string type in members)
			{
				TreeItem item = types.CreateItem(group);
				item.SetText(0, type);
				item.SetMetadata(0, type);
			}
		}

		definitions.Select(0);
	}

	string GroupOf(string type)
	{
		if (type.StartsWith("Inspector", StringComparison.Ordinal) || type is "SpinSlider" or "AngleKnob") return "Inspector";
		if (theme.GetTypeVariationBase(type) != "" || type is "Clip" or "Node" or "Ruler" or "Timeline") return "Views";
		return "Controls";
	}

	// ---- the selected type ----

	// shows a type - or the definitions page, with DefinitionsKey - as if
	// it had been clicked in the list
	public void Select(string key)
	{
		if (string.IsNullOrEmpty(key)) return;

		selected = key;

		foreach (Node child in preview.GetChildren()) child.QueueFree();

		if (key == DefinitionsKey) ShowDefinitions();
		else ShowType(key);
	}

	void ShowDefinitions()
	{
		preview.AddChild(Heading("Definitions"));

		GridContainer grid = new() { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		preview.AddChild(grid);

		foreach (ThemeDefinition definition in Enum.GetValues<ThemeDefinition>())
		{
			if (definition is ThemeDefinition.None or ThemeDefinition.Transparent) continue;
			grid.AddChild(new ThemeSwatch { Theme = theme, Definition = definition, SizeFlagsHorizontal = SizeFlags.ExpandFill });
		}

		inspector.Show([new InspectorSectionSpec("Definitions", [new InspectorTarget(new DefinitionsPage(theme))], theme.AccentColor)]);
	}

	void ShowType(string type)
	{
		string[] styleboxes = theme.GetStyleboxList(type);
		string[] colours = theme.GetColorList(type);
		string[] constants = theme.GetConstantList(type);
		string baseType = theme.GetTypeVariationBase(type);

		preview.AddChild(Heading(baseType != "" ? $"{type}  (a {baseType})" : type));

		if (styleboxes.Length > 0)
		{
			preview.AddChild(Subheading("Styleboxes"));
			GridContainer grid = new() { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			preview.AddChild(grid);
			foreach (string item in styleboxes) grid.AddChild(new ThemeSwatch { Theme = theme, ThemeType = type, Item = item, SizeFlagsHorizontal = SizeFlags.ExpandFill });
		}

		if (colours.Length > 0)
		{
			preview.AddChild(Subheading("Colours"));
			GridContainer grid = new() { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
			preview.AddChild(grid);
			foreach (string item in colours) grid.AddChild(new ThemeSwatch { Theme = theme, ThemeType = type, Item = item, IsColor = true, SizeFlagsHorizontal = SizeFlags.ExpandFill });
		}

		Control sample = ThemeSamples.For(type, theme);

		if (sample is not null)
		{
			preview.AddChild(Subheading("Live"));
			CenterContainer centre = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0f, 40f) };
			centre.AddChild(sample);
			preview.AddChild(centre);
		}

		// the pages: the font first, then every stylebox, colour and constant
		List<InspectorSectionSpec> sections = [new("Font", [new InspectorTarget(new FontPage(theme, type))])];

		foreach (string item in styleboxes)
		{
			StyleBoxPage page = new(theme, type, item);
			page.StructureChanged += () => Callable.From(() => { SetDirty(true); Select(selected); }).CallDeferred();
			sections.Add(new(item, [new InspectorTarget(page)], theme.AccentColor));
		}

		foreach (string item in colours)
			sections.Add(new(item, [new InspectorTarget(new ColorItemPage(theme, type, item))], theme.Resolve(ThemeDefinition.FontColor2)));

		foreach (string item in constants)
			sections.Add(new(item, [new InspectorTarget(new ConstantPage(theme, type, item))]));

		inspector.Show(sections);
	}

	Label Heading(string text) => new() { Text = text, ThemeTypeVariation = "ClipName" };
	Label Subheading(string text) => new() { Text = text, ThemeTypeVariation = "InspectorLabel" };

	// ---- saving ----

	void SetDirty(bool value)
	{
		dirty = value;
		title.Text = dirty ? "Theme editor  •  unsaved" : "Theme editor";
		save.Disabled = !dirty;
		revert.Disabled = !dirty;
	}

	void Save()
	{
		theme.ApplyDefinitions();
		Error error = ResourceSaver.Save(theme, ThemePath);

		if (error != Error.Ok)
		{
			GD.PushError($"Could not save the theme: {error}");
			return;
		}

		SetDirty(false);
	}

	void Revert()
	{
		if (!theme.ReloadFrom(ThemePath))
		{
			GD.PushError("Could not reload the theme file.");
			return;
		}

		SetDirty(false);
		Select(selected);
	}
}
