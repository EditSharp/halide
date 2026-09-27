using EditSharpGUI.Scripts.Input;
using Godot;
using System;
using System.Collections.Generic;

namespace EditSharpGUI.Scripts.UI.Settings;

// one action on the shortcuts page: its keys, a button to add one, reset, and a line for conflicts
[GlobalClass]
public partial class ShortcutRow : VBoxContainer
{
	[ExportGroup("Parts")]
	[Export] Label label;
	[Export] HBoxContainer keys;
	[Export] ShortcutKeyButton add;
	[Export] Button reset;
	[Export] Control conflict;
	[Export] Label conflictText;
	[Export] Button reassign;
	[Export] Button keep;

	[ExportGroup("Scenes")]
	[Export] PackedScene keyScene;

	public string Action { get; private set; }
	public string Category { get; private set; }
	public string Text { get; private set; }

	// a combo recorded for binding `index`, or -1 for a new one
	public event Action<ShortcutRow, int, KeyCombo> Recorded;
	public event Action<ShortcutRow, int> Removed;
	public event Action<ShortcutRow> ResetRequested;

	Action onReassign, onKeep;

	public void Setup(string action, string category, string text)
	{
		Action = action;
		Category = category;
		Text = text;
		label.Text = text;

		add.Recorded += combo => Recorded?.Invoke(this, -1, combo);
		reset.Pressed += () => ResetRequested?.Invoke(this);
		reassign.Pressed += () => { conflict.Visible = false; onReassign?.Invoke(); };
		keep.Pressed += () => { conflict.Visible = false; onKeep?.Invoke(); };
	}

	// the action's current keys, and whether they're its defaults
	public void ShowKeys(IReadOnlyList<KeyCombo> combos, bool isDefault)
	{
		foreach (Node child in keys.GetChildren())
		{
			keys.RemoveChild(child);
			child.QueueFree();
		}

		for (int i = 0; i < combos.Count; i++)
		{
			Control key = keyScene.Instantiate<Control>();
			ShortcutKeyButton button = key.GetNode<ShortcutKeyButton>("%Key");
			button.Combo = combos[i];
			int index = i;
			button.Recorded += combo => Recorded?.Invoke(this, index, combo);
			key.GetNode<Button>("%Remove").Pressed += () => Removed?.Invoke(this, index);
			keys.AddChild(key);
		}

		reset.Disabled = isDefault;
	}

	// asks whether to take a combo from another action
	public void AskConflict(string text, Action yes, Action no)
	{
		conflictText.Text = text;
		onReassign = yes;
		onKeep = no;
		conflict.Visible = true;
	}

	// whether a search finds this row, by its words or its keys
	public bool Matches(string query, IReadOnlyList<KeyCombo> combos)
	{
		if (string.IsNullOrWhiteSpace(query)) return true;
		if (Text.Contains(query, StringComparison.OrdinalIgnoreCase) || Category.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
		foreach (KeyCombo combo in combos) if (combo.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}
}
