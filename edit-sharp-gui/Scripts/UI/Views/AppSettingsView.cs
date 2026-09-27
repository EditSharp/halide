using EditSharp.Editing;
using EditSharpGUI.Scripts.UI.Settings;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// App Settings: sections on the left, the chosen page on the right, a search over every setting; laid out in AppSettings.tscn
public partial class AppSettingsView : PanelContainer
{
	[ExportGroup("Sidebar")]
	[Export] LineEdit search;
	[Export] Button appearance;
	[Export] Button shortcuts;
	[Export] Button media;
	[Export] Button projects;
	[Export] CheckButton advanced;

	[ExportGroup("Page")]
	[Export] Label noMatches;
	[Export] Inspector inspector;
	[Export] ShortcutsPage shortcutsPage;

	enum Section { Appearance, Shortcuts, Media, Projects }

	readonly AppearancePage appearancePage = new();
	readonly MediaPage mediaPage = new();
	readonly ProjectsPage projectsPage = new();

	// every setting marked advanced, by property name
	static readonly HashSet<string> Advanced = [.. new[] { typeof(AppearancePage), typeof(MediaPage), typeof(ProjectsPage) }
		.SelectMany(t => t.GetProperties())
		.Where(p => p.IsDefined(typeof(AdvancedSettingAttribute)))
		.Select(p => p.Name)];

	Section section = Section.Appearance;
	string query = "";

	public override void _Ready()
	{
		appearance.Pressed += () => Select(Section.Appearance);
		shortcuts.Pressed += () => Select(Section.Shortcuts);
		media.Pressed += () => Select(Section.Media);
		projects.Pressed += () => Select(Section.Projects);

		search.TextChanged += text => { query = text.Trim(); Refresh(); };

		advanced.ButtonPressed = AppSettings.Current.ShowAdvancedSettings;
		advanced.Toggled += on =>
		{
			AppSettings.Current.ShowAdvancedSettings = on;
			AppSettings.Current.Save();
			Refresh();
		};

		inspector.Filter = Shows;
		mediaPage.Measured += () => inspector.RefreshValues();
		mediaPage.Measure();

		Refresh();
	}

	// whether a property gets a row: advanced ones only with the toggle, and only what the search finds
	bool Shows(PropertyDescriptor d)
	{
		if (Advanced.Contains(d.Name) && !advanced.ButtonPressed) return false;
		if (query.Length == 0) return true;
		return d.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
			|| (d.Group?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
			|| (d.Tooltip?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
	}

	object PageOf(Section s) => s switch
	{
		Section.Appearance => appearancePage,
		Section.Media => mediaPage,
		Section.Projects => projectsPage,
		_ => null,
	};

	Button ButtonOf(Section s) => s switch
	{
		Section.Appearance => appearance,
		Section.Shortcuts => shortcuts,
		Section.Media => media,
		_ => projects,
	};

	// how many settings in a section the search and toggle leave
	int Count(Section s) => s == Section.Shortcuts ? shortcutsPage.Count(query) : Inspect.Of(PageOf(s)).Count(Shows);

	void Select(Section s)
	{
		section = s;
		ButtonOf(s).ButtonPressed = true;
		Show();
	}

	// sections with nothing to show hide while searching; the page moves to one that has
	void Refresh()
	{
		Section[] all = Enum.GetValues<Section>();
		foreach (Section s in all) ButtonOf(s).Visible = query.Length == 0 || Count(s) > 0;

		if (!ButtonOf(section).Visible && all.FirstOrDefault(s => ButtonOf(s).Visible) is var first && ButtonOf(first).Visible) section = first;
		ButtonOf(section).ButtonPressed = true;
		Show();
	}

	void Show()
	{
		bool any = Count(section) > 0;
		noMatches.Visible = !any;

		shortcutsPage.Visible = any && section == Section.Shortcuts;
		inspector.Visible = any && section != Section.Shortcuts;
		shortcutsPage.Filter(query);

		if (section != Section.Shortcuts)
			inspector.Show([new InspectorSectionSpec(ButtonOf(section).Text, [new InspectorTarget(PageOf(section))], Actions: ActionsOf(section))]);
	}

	IReadOnlyList<InspectorAction> ActionsOf(Section s) => s == Section.Media
		?
		[
			new InspectorAction("Proxies", "Show in File Manager", MediaPage.RevealProxies),
			new InspectorAction("Proxies", "Delete Proxies…", () => _ = mediaPage.ClearProxiesAsync(this)),
			new InspectorAction("Thumbnails & Waveforms", "Clear", mediaPage.ClearCaches),
		]
		: null;
}
