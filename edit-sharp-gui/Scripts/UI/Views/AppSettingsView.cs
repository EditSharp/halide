using EditSharp.Editing;
using EditSharpGUI.Api;
using EditSharpGUI.Scripts.UI.Settings;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// App Settings: sections on the left, the chosen page on the right, a search over every setting; laid out in AppSettings.tscn.
// a section is an inspector over a page object, or a page of its own (shortcuts, extensions); extensions add pages too
public partial class AppSettingsView : PanelContainer
{
	[ExportGroup("Sidebar")]
	[Export] LineEdit search;
	[Export] Container sections;
	[Export] Button appearance;
	[Export] Button shortcuts;
	[Export] Button media;
	[Export] Button projects;
	[Export] Button extensions;
	[Export] CheckButton advanced;
	[Export] PackedScene sectionScene;

	[ExportGroup("Page")]
	[Export] Label noMatches;
	[Export] Inspector inspector;
	[Export] ShortcutsPage shortcutsPage;
	[Export] ExtensionsPage extensionsPage;

	// a sidebar entry: its button, and either the object its inspector shows or its own page
	sealed record Section(Button Button, object Target = null, ISettingsPage Custom = null, Func<IReadOnlyList<InspectorAction>> Actions = null);

	readonly AppearancePage appearancePage = new();
	readonly MediaPage mediaPage = new();
	readonly ProjectsPage projectsPage = new();

	readonly List<Section> all = [];
	readonly List<Section> added = [];
	Section section;
	string query = "";

	public override void _Ready()
	{
		all.Add(new Section(appearance, appearancePage));
		all.Add(new Section(shortcuts, Custom: shortcutsPage));
		all.Add(new Section(media, mediaPage, Actions: () =>
		[
			new InspectorAction("Proxies", "Show in File Manager", MediaPage.RevealProxies),
			new InspectorAction("Proxies", "Delete Proxies…", () => _ = mediaPage.ClearProxiesAsync(this)),
			new InspectorAction("Thumbnails & Waveforms", "Clear", mediaPage.ClearCaches),
		]));
		all.Add(new Section(projects, projectsPage));
		all.Add(new Section(extensions, Custom: extensionsPage));
		section = all[0];

		foreach (Section s in all) Wire(s);

		search.TextChanged += text => { query = text.Trim(); Refresh(); };

		advanced.ButtonPressed = AppSettings.Current.ShowAdvancedSettings;
		advanced.Toggled += on =>
		{
			AppSettings.Current.ShowAdvancedSettings = on;
			AppSettings.Current.Save();
			Refresh();
		};

		inspector.Filter = d => Shows(section.Target, d);
		mediaPage.Measured += () => inspector.RefreshValues();
		mediaPage.Measure();

		EditSharpApp.Instance.SettingsPages.Changed += AddExtensionPages;
		AddExtensionPages();
	}

	public override void _ExitTree() => EditSharpApp.Instance.SettingsPages.Changed -= AddExtensionPages;

	void Wire(Section s)
	{
		Section captured = s;
		s.Button.Pressed += () => Select(captured);
	}

	// pages extensions add sit between the built-in pages and Extensions
	void AddExtensionPages()
	{
		foreach (Section s in added)
		{
			all.Remove(s);
			s.Button.QueueFree();
		}
		added.Clear();

		foreach (SettingsPageRegistry.Page page in EditSharpApp.Instance.SettingsPages.Pages)
		{
			Button button = sectionScene.Instantiate<Button>();
			button.Text = page.Title;
			button.ButtonGroup = appearance.ButtonGroup;
			sections.AddChild(button);
			sections.MoveChild(button, extensions.GetIndex());

			Section s = new(button, page.Target);
			Wire(s);
			added.Add(s);
			all.Insert(all.IndexOf(all.First(x => x.Button == extensions)), s);
		}

		if (!all.Contains(section)) section = all[0];
		Refresh();
	}

	// whether a property of a page gets a row: advanced ones only with the toggle, and only what the search finds
	bool Shows(object page, PropertyDescriptor d)
	{
		if (!advanced.ButtonPressed && page?.GetType().GetProperty(d.Name)?.IsDefined(typeof(AdvancedSettingAttribute)) == true) return false;
		if (query.Length == 0) return true;
		return d.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
			|| (d.Group?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
			|| (d.Tooltip?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
	}

	// how many settings in a section the search and toggle leave
	int Count(Section s) => s.Custom?.Count(query) ?? Inspect.Of(s.Target).Count(d => Shows(s.Target, d));

	void Select(Section s)
	{
		section = s;
		s.Button.ButtonPressed = true;
		ShowPage();
	}

	// sections with nothing to show hide while searching; the page moves to one that has
	void Refresh()
	{
		foreach (Section s in all) s.Button.Visible = query.Length == 0 || Count(s) > 0;

		if (!section.Button.Visible && all.FirstOrDefault(s => s.Button.Visible) is Section first) section = first;
		section.Button.ButtonPressed = true;
		ShowPage();
	}

	void ShowPage()
	{
		bool any = Count(section) > 0;
		noMatches.Visible = !any;

		foreach (Section s in all.Where(s => s.Custom is Control))
		{
			((Control)s.Custom).Visible = any && s == section;
			s.Custom.Filter(query);
		}

		inspector.Visible = any && section.Custom is null;
		if (section.Custom is null)
			inspector.Show([new InspectorSectionSpec(section.Button.Text, [new InspectorTarget(section.Target)], Actions: section.Actions?.Invoke())]);
	}
}
