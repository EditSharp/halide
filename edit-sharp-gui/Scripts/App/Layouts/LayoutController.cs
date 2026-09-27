using EditSharpGUI.Scripts.UI.Docking;
using System;
using System.Text.Json.Nodes;

namespace EditSharpGUI.Scripts.App.Layouts;

/// <summary>One project window's active layout: applies layouts to its docks and keeps the active one remembering how it's rearranged.</summary>
public sealed class LayoutController : IDisposable
{
	readonly DockManager docks;
	bool applying;

	public LayoutController(DockManager docks)
	{
		this.docks = docks;
		docks.Changed += Remember;
		LayoutStore.Changed += OnStoreChanged;
	}

	/// <summary>The layout the window is in.</summary>
	public string Active { get; private set; } = LayoutPresets.Editing;

	/// <summary>The active layout changed, or the list of layouts did.</summary>
	public event Action Changed;

	/// <summary>Arranges the window as the layout now stands.</summary>
	public bool Apply(string name)
	{
		if (LayoutStore.Current(name) is not JsonObject state) return false;

		Active = name;
		applying = true;
		try { docks.Apply(state); }
		finally { applying = false; }

		Changed?.Invoke();
		return true;
	}

	/// <summary>The layout at a place in the switcher, counting from one.</summary>
	public bool ApplyNumber(int number) => number >= 1 && number <= LayoutStore.Names.Count && Apply(LayoutStore.Names[number - 1]);

	/// <summary>The window's arrangement as a new layout, which becomes the active one.</summary>
	public bool SaveAsNew(string name)
	{
		if (!LayoutStore.Add(name, docks.Capture())) return false;

		Active = name.Trim();
		Changed?.Invoke();
		return true;
	}

	public bool CanEdit => !LayoutPresets.IsPreset(Active);

	public bool RenameActive(string to)
	{
		if (!LayoutStore.Rename(Active, to)) return false;

		Active = to.Trim();
		Changed?.Invoke();
		return true;
	}

	/// <summary>Deletes the active user layout and goes back to Editing.</summary>
	public bool DeleteActive()
	{
		if (!LayoutStore.Delete(Active)) return false;
		return Apply(LayoutPresets.Editing);
	}

	/// <summary>The active layout as it was saved, forgetting its rearrangements.</summary>
	public void ResetActive()
	{
		LayoutStore.Reset(Active);
		Apply(Active);
	}

	/// <summary>A reopened project's layout, whose arrangement the project restores itself.</summary>
	public void Resume(string name)
	{
		Active = name is not null && LayoutStore.Exists(name) ? name : LayoutPresets.Editing;
		Changed?.Invoke();
	}

	void Remember()
	{
		if (!applying && LayoutStore.Exists(Active)) LayoutStore.Remember(Active, docks.Capture());
	}

	void OnStoreChanged()
	{
		if (!LayoutStore.Exists(Active)) Active = LayoutPresets.Editing;
		Changed?.Invoke();
	}

	public void Dispose()
	{
		docks.Changed -= Remember;
		LayoutStore.Changed -= OnStoreChanged;
	}
}
