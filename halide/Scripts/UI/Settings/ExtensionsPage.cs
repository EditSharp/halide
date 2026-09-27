using Halide.Api;
using Halide.Api.Extensions;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halide.Scripts.UI.Settings;

// the Extensions page: every extension found, turned on and off and reloaded here; laid out in ExtensionsPage.tscn
[GlobalClass]
public partial class ExtensionsPage : VBoxContainer, ISettingsPage
{
	[Export] Container list;
	[Export] Label empty;
	[Export] Button openFolder;
	[Export] Button reloadAll;
	[Export] PackedScene rowScene;

	readonly List<(ExtensionRow Row, LoadedExtension Extension)> rows = [];
	string query = "";

	static ExtensionManager Manager => EditSharpApp.Instance.Extensions;

	public override void _Ready()
	{
		openFolder.Pressed += () => OS.ShellOpen(Manager.FolderPath);
		reloadAll.Pressed += () => _ = Manager.LoadAllAsync(this);
		Manager.Changed += Rebuild;
		Rebuild();
	}

	public override void _ExitTree() => Manager.Changed -= Rebuild;

	void Rebuild()
	{
		foreach ((ExtensionRow row, _) in rows) row.QueueFree();
		rows.Clear();

		foreach (LoadedExtension extension in Manager.All)
		{
			ExtensionRow row = rowScene.Instantiate<ExtensionRow>();
			list.AddChild(row);
			row.Show(extension);
			row.Toggled += on => Manager.SetEnabled(extension, on);
			row.ReloadPressed += () => _ = Manager.ReloadAsync(extension, this);
			rows.Add((row, extension));
		}

		Filter(query);
	}

	public int Count(string text) => text.Length == 0 ? Math.Max(rows.Count, 1) : rows.Count(r => Matches(r.Row, text));

	public int Filter(string text)
	{
		query = text;
		foreach ((ExtensionRow row, _) in rows) row.Visible = Matches(row, text);
		empty.Visible = rows.Count == 0;
		return rows.Count(r => r.Row.Visible);
	}

	static bool Matches(ExtensionRow row, string text) => text.Length == 0 || row.SearchText.Contains(text, StringComparison.OrdinalIgnoreCase);
}
