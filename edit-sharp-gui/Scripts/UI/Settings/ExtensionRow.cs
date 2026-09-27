using EditSharpGUI.Api.Extensions;
using Godot;
using System;

namespace EditSharpGUI.Scripts.UI.Settings;

// one extension on the Extensions page: what it is, how it stands, and switches for it; laid out in ExtensionRow.tscn
[GlobalClass]
public partial class ExtensionRow : VBoxContainer
{
	[Export] Label title;
	[Export] Label details;
	[Export] Label description;
	[Export] Label status;
	[Export] CheckButton enabled;
	[Export] Button reload;

	public event Action<bool> Toggled;
	public event Action ReloadPressed;

	public string SearchText { get; private set; } = "";

	public override void _Ready()
	{
		enabled.Toggled += on => Toggled?.Invoke(on);
		reload.Pressed += () => ReloadPressed?.Invoke();
	}

	public void Show(LoadedExtension extension)
	{
		title.Text = extension.Name;
		details.Text = extension.Manifest is ExtensionManifest m ? $"{m.Version} · {m.Id}" : extension.Id;
		description.Text = extension.Manifest?.Description ?? "";
		description.Visible = description.Text.Length > 0;

		status.Text = extension.State switch
		{
			ExtensionState.Enabled => "",
			ExtensionState.Disabled => "Disabled",
			ExtensionState.Untrusted => "New or changed: enable it to trust it",
			ExtensionState.Incompatible => extension.Error,
			ExtensionState.Failed => $"Stopped after an error: {extension.Error}",
			ExtensionState.Broken => $"Can't be read: {extension.Error}",
			_ => "",
		};
		status.Visible = status.Text.Length > 0;
		status.ThemeTypeVariation = extension.State is ExtensionState.Failed or ExtensionState.Broken or ExtensionState.Incompatible ? "ErrorLabel" : "InspectorLabel";

		enabled.SetPressedNoSignal(extension.State == ExtensionState.Enabled);
		enabled.Disabled = extension.State is ExtensionState.Broken or ExtensionState.Incompatible;

		SearchText = $"{extension.Name} {extension.Id} {description.Text}";
	}
}
