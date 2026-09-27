using Halide.Scripts.Input;
using Godot;
using System;

namespace Halide.Scripts.UI.Settings;

// a binding shown as its keys; click it and press new keys to change it, Esc to cancel
[GlobalClass]
public partial class ShortcutKeyButton : Button
{
	// the keys pressed while recording
	public event Action<KeyCombo> Recorded;

	// the text shown while there's no combo, for the button that adds one
	[Export] public string Empty = "+";

	public KeyCombo? Combo
	{
		get;
		set
		{
			field = value;
			if (!recording) Text = Caption();
		}
	}

	bool recording;

	public override void _Ready()
	{
		Text = Caption();
		Pressed += Begin;
		FocusExited += Cancel;
	}

	public void Begin()
	{
		recording = true;
		Text = "Press keys…";
		GrabFocus();
	}

	void Cancel()
	{
		if (!recording) return;
		recording = false;
		Text = Caption();
	}

	string Caption() => Combo?.ToString() ?? Empty;

	// the key goes to the recording, never to a shortcut or the button itself
	public override void _GuiInput(InputEvent e)
	{
		if (!recording || e is not InputEventKey { Pressed: true, Echo: false } key) return;
		AcceptEvent();

		if (key.Keycode == Key.Escape) { Cancel(); return; }
		if (key.Keycode is Key.Ctrl or Key.Shift or Key.Alt or Key.Meta) return;

		recording = false;
		Text = Caption();
		Recorded?.Invoke(KeyCombo.From(key));
	}
}
