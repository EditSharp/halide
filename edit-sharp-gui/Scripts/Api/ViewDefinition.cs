using EditSharpGUI.Scripts.UI.Docking;
using Godot;
using System;

namespace EditSharpGUI.Api;

/// <summary>A kind of dockable view: its id and title, how to make one for a project window, and where it first opens.</summary>
/// <param name="Id">Unique across the app, like <c>scopes.waveform</c>; also the View menu command's suffix.</param>
/// <param name="Title">What its tab and the View menu call it.</param>
/// <param name="Create">Makes the view for a project window; called once per window.</param>
/// <param name="Beside">The view it first opens beside; null for the largest pane.</param>
/// <param name="Side">Where beside it: among its tabs, or split off to a side.</param>
public sealed record ViewDefinition(string Id, string Title, Func<ProjectHandle, Control> Create, string Beside = null, DockSide Side = DockSide.Center);
