using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Halide.Scripts.App.Layouts;

/// <summary>Every layout, app-wide: the presets and the user's own, each with the state it was saved in and the state it's been rearranged to since.</summary>
public static class LayoutStore
{
	public const string DefaultPath = "user://layouts.json";

	sealed class Entry
	{
		public string Name;
		public JsonObject Saved;
		public JsonObject Current;
	}

	static readonly List<Entry> user = [];
	static readonly Dictionary<string, JsonObject> presetEdits = [];
	static bool loaded, writeQueued;

	/// <summary>The file layouts live in; a test points it somewhere else.</summary>
	public static string FilePath { get; set; } = DefaultPath;

	/// <summary>A layout was added, renamed or removed.</summary>
	public static event Action Changed;

	/// <summary>Every layout in switcher order: the presets, then the user's in the order they were made.</summary>
	public static IReadOnlyList<string> Names
	{
		get
		{
			Load();
			return [.. LayoutPresets.Names, .. user.Select(u => u.Name)];
		}
	}

	public static bool Exists(string name) => Names.Contains(name);

	/// <summary>A layout as it stands now, rearrangements included; null when there's no such layout.</summary>
	public static JsonObject Current(string name)
	{
		Load();
		if (LayoutPresets.IsPreset(name)) return (presetEdits.TryGetValue(name, out JsonObject edited) ? edited : LayoutPresets.State(name))?.DeepClone().AsObject();
		return user.FirstOrDefault(u => u.Name == name)?.Current?.DeepClone().AsObject();
	}

	/// <summary>A layout as it was saved, before any rearranging.</summary>
	public static JsonObject Saved(string name)
	{
		Load();
		if (LayoutPresets.IsPreset(name)) return LayoutPresets.State(name);
		return user.FirstOrDefault(u => u.Name == name)?.Saved?.DeepClone().AsObject();
	}

	/// <summary>Remembers a layout's new arrangement; written out once the frame is done.</summary>
	public static void Remember(string name, JsonObject state)
	{
		Load();
		if (LayoutPresets.IsPreset(name)) presetEdits[name] = state.DeepClone().AsObject();
		else if (user.FirstOrDefault(u => u.Name == name) is Entry entry) entry.Current = state.DeepClone().AsObject();
		else return;

		WriteLater();
	}

	/// <summary>Back to how it was saved.</summary>
	public static void Reset(string name)
	{
		Load();
		if (LayoutPresets.IsPreset(name)) presetEdits.Remove(name);
		else if (user.FirstOrDefault(u => u.Name == name) is Entry entry) entry.Current = entry.Saved.DeepClone().AsObject();
		WriteLater();
	}

	/// <summary>A new layout from an arrangement; false when the name is empty or taken.</summary>
	public static bool Add(string name, JsonObject state)
	{
		Load();
		name = name?.Trim();
		if (string.IsNullOrEmpty(name) || Exists(name)) return false;

		user.Add(new Entry { Name = name, Saved = state.DeepClone().AsObject(), Current = state.DeepClone().AsObject() });
		WriteLater();
		Changed?.Invoke();
		return true;
	}

	/// <summary>A user layout renamed; presets keep their names.</summary>
	public static bool Rename(string name, string to)
	{
		Load();
		to = to?.Trim();
		if (string.IsNullOrEmpty(to) || Exists(to) || user.FirstOrDefault(u => u.Name == name) is not Entry entry) return false;

		entry.Name = to;
		WriteLater();
		Changed?.Invoke();
		return true;
	}

	/// <summary>A user layout removed; presets can't be.</summary>
	public static bool Delete(string name)
	{
		Load();
		if (user.RemoveAll(u => u.Name == name) == 0) return false;

		WriteLater();
		Changed?.Invoke();
		return true;
	}

	/// <summary>Forgets what's in memory, so the next use reads <see cref="FilePath"/> again.</summary>
	public static void Reload()
	{
		loaded = false;
		user.Clear();
		presetEdits.Clear();
		Changed?.Invoke();
	}

	static void Load()
	{
		if (loaded) return;
		loaded = true;

		string file = ProjectSettings.GlobalizePath(FilePath);
		if (!File.Exists(file)) return;

		try
		{
			JsonObject root = JsonNode.Parse(File.ReadAllText(file))?.AsObject();

			foreach ((string name, JsonNode state) in root?["presets"]?.AsObject() ?? [])
				if (LayoutPresets.IsPreset(name) && state is JsonObject o) presetEdits[name] = o.DeepClone().AsObject();

			foreach (JsonNode node in root?["layouts"]?.AsArray() ?? [])
			{
				if ((string)node?["name"] is not string name || string.IsNullOrWhiteSpace(name) || LayoutPresets.IsPreset(name)) continue;
				if (node["saved"] is not JsonObject saved) continue;
				user.Add(new Entry { Name = name, Saved = saved.DeepClone().AsObject(), Current = (node["current"] as JsonObject ?? saved).DeepClone().AsObject() });
			}
		}
		catch (Exception e)
		{
			GD.PushWarning($"Could not read layouts from {file}: {e.Message}");
		}
	}

	static void WriteLater()
	{
		if (writeQueued) return;
		writeQueued = true;
		Callable.From(Write).CallDeferred();
	}

	static void Write()
	{
		writeQueued = false;
		string file = ProjectSettings.GlobalizePath(FilePath);

		JsonObject root = new()
		{
			["presets"] = new JsonObject(presetEdits.Select(p => KeyValuePair.Create(p.Key, (JsonNode)p.Value.DeepClone()))),
			["layouts"] = new JsonArray([.. user.Select(u => (JsonNode)new JsonObject
			{
				["name"] = u.Name,
				["saved"] = u.Saved.DeepClone(),
				["current"] = u.Current.DeepClone(),
			})]),
		};

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(file)!);
			File.WriteAllText(file, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
		}
		catch (Exception e)
		{
			GD.PushWarning($"Could not save layouts to {file}: {e.Message}");
		}
	}
}
