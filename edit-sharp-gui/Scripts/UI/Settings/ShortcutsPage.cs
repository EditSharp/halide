using EditSharpGUI.Scripts.Input;
using Godot;
using System.Collections.Generic;
using System.Linq;

namespace EditSharpGUI.Scripts.UI.Settings;

// App Settings' Keyboard Shortcuts page: every action by category, rebound by pressing keys; saved as it changes
[GlobalClass]
public partial class ShortcutsPage : VBoxContainer
{
	[ExportGroup("Parts")]
	[Export] Button resetAll;
	[Export] VBoxContainer list;

	[ExportGroup("Scenes")]
	[Export] PackedScene categoryScene;
	[Export] PackedScene rowScene;

	readonly List<ShortcutRow> rows = [];
	readonly Dictionary<string, Control> categories = [];
	string query = "";

	static ShortcutMap Map => InputManager.Singleton.Keyboard.Shortcuts;

	public override void _Ready()
	{
		resetAll.Pressed += ResetAll;

		foreach ((string action, string category, string text) in Shortcuts.Catalog)
		{
			if (!categories.ContainsKey(category))
			{
				Label header = categoryScene.Instantiate<Label>();
				header.Text = category;
				categories[category] = header;
				list.AddChild(header);
			}

			ShortcutRow row = rowScene.Instantiate<ShortcutRow>();
			list.AddChild(row);
			row.Setup(action, category, text);
			row.Recorded += OnRecorded;
			row.Removed += OnRemoved;
			row.ResetRequested += r => { Map.Bind(r.Action, [.. ShortcutMap.DefaultsFor(r.Action)]); Changed(); };
			rows.Add(row);
		}

		Refresh();
	}

	// narrows the list to the actions a search finds; returns how many
	public int Filter(string text)
	{
		query = text ?? "";
		return Refresh();
	}

	// how many actions a search would find, without changing what's shown
	public int Count(string text) => rows.Count(r => r.Matches(text, Map.Get(r.Action)));

	int Refresh()
	{
		int shown = 0;
		foreach (ShortcutRow row in rows)
		{
			IReadOnlyList<KeyCombo> combos = Map.Get(row.Action);
			row.ShowKeys(combos, combos.SequenceEqual(ShortcutMap.DefaultsFor(row.Action)));
			row.Visible = row.Matches(query, combos);
			if (row.Visible) shown++;
		}

		foreach ((string category, Control header) in categories) header.Visible = rows.Any(r => r.Category == category && r.Visible);
		resetAll.Disabled = Shortcuts.Catalog.All(c => Map.Get(c.Action).SequenceEqual(ShortcutMap.DefaultsFor(c.Action)));
		return shown;
	}

	void OnRecorded(ShortcutRow row, int index, KeyCombo combo)
	{
		string other = Map.ActionsFor(combo).FirstOrDefault(a => a != row.Action);
		if (other is null) { Bind(row.Action, index, combo); return; }

		string name = Shortcuts.Catalog.FirstOrDefault(c => c.Action == other).Label ?? other;
		row.AskConflict($"{combo} is used by {name}.", () =>
		{
			Map.Bind(other, [.. Map.Get(other).Where(c => c != combo)]);
			Bind(row.Action, index, combo);
		}, () => Refresh());
	}

	void Bind(string action, int index, KeyCombo combo)
	{
		List<KeyCombo> combos = [.. Map.Get(action)];
		if (index >= 0 && index < combos.Count) combos[index] = combo;
		else if (!combos.Contains(combo)) combos.Add(combo);
		Map.Bind(action, [.. combos]);
		Changed();
	}

	void OnRemoved(ShortcutRow row, int index)
	{
		List<KeyCombo> combos = [.. Map.Get(row.Action)];
		if (index < combos.Count) combos.RemoveAt(index);
		Map.Bind(row.Action, [.. combos]);
		Changed();
	}

	void ResetAll()
	{
		foreach ((string action, _, _) in Shortcuts.Catalog) Map.Bind(action, [.. ShortcutMap.DefaultsFor(action)]);
		Changed();
	}

	void Changed()
	{
		Map.Save();
		Refresh();
	}
}
