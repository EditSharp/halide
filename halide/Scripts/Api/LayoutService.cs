using Halide.Scripts.App.Layouts;
using Halide.Scripts.UI.Docking;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Halide.Api;

/// <summary>A project window's views and layouts: opening, closing, floating and docking views, and switching layouts.</summary>
public sealed class LayoutService
{
	readonly ProjectHandle project;

	internal LayoutService(ProjectHandle project) => this.project = project;

	DockManager Docks => project.Editor.Docks;
	LayoutController Controller => project.Editor.Layouts;

	/// <summary>Every view the window has, by id and title.</summary>
	public IReadOnlyList<(string Id, string Title)> Views => [.. Docks.Views];

	public bool IsOpen(string view) => Docks.IsOpen(view);

	/// <summary>Whether the view is in a floating window rather than the project window.</summary>
	public bool IsFloating(string view) => Docks.IsFloating(view);

	/// <summary>Opens a view where it last was, or its usual place; brings it to the front when it's open.</summary>
	public void Open(string view) => Docks.Open(view);

	public void Close(string view) => Docks.Close(view);

	/// <summary>Floats a view in a window of its own, at <paramref name="rect"/> in screen pixels or over where it was.</summary>
	public void Float(string view, Rect2I? rect = null) => Docks.Float(view, rect);

	/// <summary>Docks a view beside another: among its tabs (<see cref="DockSide.Center"/>) or split off to a side.</summary>
	public bool Dock(string view, string beside, DockSide side = DockSide.Center) => Docks.DockBeside(view, beside, side);

	/// <summary>The window's arrangement as JSON: its dock tree, split ratios and floats.</summary>
	public JsonObject Capture() => Docks.Capture();

	// ---- layouts ----

	/// <summary>Every layout in switcher order.</summary>
	public IReadOnlyList<string> Layouts => LayoutStore.Names;

	/// <summary>The layout the window is in.</summary>
	public string Active => Controller.Active;

	/// <summary>Arranges the window as a layout now stands; false when there's no such layout.</summary>
	public bool Apply(string layout) => Controller.Apply(layout);

	/// <summary>The window's arrangement as a new layout, which becomes active; false when the name is empty or taken.</summary>
	public bool SaveAsNew(string name) => Controller.SaveAsNew(name);

	/// <summary>Renames the active layout; presets can't be renamed.</summary>
	public bool RenameActive(string name) => Controller.RenameActive(name);

	/// <summary>Deletes the active layout and goes back to Editing; presets can't be deleted.</summary>
	public bool DeleteActive() => Controller.DeleteActive();

	/// <summary>The active layout as it was saved, forgetting its rearrangements.</summary>
	public void ResetActive() => Controller.ResetActive();

	/// <summary>The arrangement changed, or the active layout did.</summary>
	public event Action Changed
	{
		add
		{
			Docks.Changed += value;
			Controller.Changed += value;
		}
		remove
		{
			Docks.Changed -= value;
			Controller.Changed -= value;
		}
	}
}
