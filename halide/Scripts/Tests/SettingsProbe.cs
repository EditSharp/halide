using Halide.Scripts.App.Platform;
using Halide.Scripts.Input;
using Halide.Scripts.UI.Settings;
using Halide.Scripts.UI.Theming;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

// App Settings driven from code with its files kept apart from the user's; prints SETTINGS OK
public partial class SettingsProbe : Node
{
	bool ok = true;

	void Check(bool condition, string what)
	{
		ok &= condition;
		GD.Print($"PROBE {(condition ? "ok  " : "BAD ")} {what}");
	}

	public override async void _Ready()
	{
		Window root = GetTree().Root;
		root.GuiEmbedSubwindows = false;
		HostWindow.Hide(root);

		string folder = Path.Combine(Path.GetTempPath(), $"editsharp-settings-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		AppSettings.UseFile(Path.Combine(folder, "settings.json"));
		AppSettings.Current.OnLastWindowClosed = LastWindowAction.StayInTray;
		RecentProjects.UseFile(Path.Combine(folder, "projects.json"));
		ShortcutMap.FilePath = Path.Combine(folder, "shortcuts.json");
		EditSharpTheme.SettingsFile = Path.Combine(folder, "theme.json");
		EditSharpTheme theme = ThemeDB.GetProjectTheme() as EditSharpTheme;
		string paletteWas = theme?.PaletteChoice ?? "";
		await Frames(3);

		ProjectManager.Singleton.ShowSettings();
		await Frames(10);
		SettingsWindow window = root.GetChildren().OfType<SettingsWindow>().First();
		AppSettingsView view = Descendants(window).OfType<AppSettingsView>().First();
		ProjectManager.Singleton.ShowSettings();
		await Frames(3);
		Check(root.GetChildren().OfType<SettingsWindow>().Count() == 1, "opening it again keeps one window");

		Button Section(string text) => Descendants(view).OfType<Button>().First(b => b.ThemeTypeVariation == "SettingsSection" && b.Text == text);
		bool RowShown(string label) => Descendants(view).OfType<Label>().Any(l => l.Text == label && l.IsVisibleInTree());

		await Settle();
		Shot(window, "appearance");
		Check(RowShown("Theme") && RowShown("Accent") && RowShown("Interface scale"), "Appearance shows theme, accent and scale");

		Section("Media & Cache").EmitSignal(BaseButton.SignalName.Pressed);
		await Frames(5);
		Check(RowShown("Proxy folder") && !RowShown("FFmpeg"), "Media hides the advanced tools");
		CheckButton advanced = Descendants(view).OfType<CheckButton>().First(c => c.Text == "Show advanced");
		advanced.ButtonPressed = true;
		await Frames(5);
		Check(RowShown("FFmpeg") && RowShown("Audio latency"), "Show advanced brings them in");
		await Settle();
		Shot(window, "media");
		advanced.ButtonPressed = false;

		Section("Projects & Autosave").EmitSignal(BaseButton.SignalName.Pressed);
		await Frames(5);
		await Settle();
		Shot(window, "projects");

		// search narrows the sections to the ones with matches
		LineEdit search = Descendants(view).OfType<LineEdit>().First(l => l.PlaceholderText == "Search settings");
		search.Text = "autosave";
		search.EmitSignal(LineEdit.SignalName.TextChanged, "autosave");
		await Frames(5);
		Check(Section("Projects & Autosave").Visible && !Section("Appearance").Visible && !Section("Media & Cache").Visible, "a search leaves only the sections it finds");
		search.Text = "";
		search.EmitSignal(LineEdit.SignalName.TextChanged, "");
		await Frames(3);

		// shortcuts: a new combo, a conflict taken over, then everything back
		Section("Keyboard Shortcuts").EmitSignal(BaseButton.SignalName.Pressed);
		await Frames(5);
		ShortcutMap map = InputManager.Singleton.Keyboard.Shortcuts;
		ShortcutRow save = Descendants(view).OfType<ShortcutRow>().First(r => r.Action == Shortcuts.Save);
		ShortcutRow saveAs = Descendants(view).OfType<ShortcutRow>().First(r => r.Action == Shortcuts.SaveAs);

		await Press(window, AddButton(save), new InputEventKey { Keycode = Key.K, CtrlPressed = true, ShiftPressed = true, Pressed = true });
		Check(map.Get(Shortcuts.Save).Contains(new KeyCombo(Key.K, Control: true, Shift: true)), "pressing keys on + adds a shortcut");

		await Press(window, AddButton(saveAs), new InputEventKey { Keycode = Key.S, CtrlPressed = true, Pressed = true });
		Label conflict = Descendants(saveAs).OfType<Label>().First(l => l.ThemeTypeVariation == "ErrorLabel");
		Check(conflict.IsVisibleInTree() && conflict.Text.Contains("Save"), $"a combo in use asks first: {conflict.Text}");
		await Settle();
		Shot(window, "shortcuts");
		Descendants(saveAs).OfType<Button>().First(b => b.Text == "Reassign").EmitSignal(BaseButton.SignalName.Pressed);
		await Frames(3);
		KeyCombo ctrlS = new(Key.S, Control: true);
		Check(map.Get(Shortcuts.SaveAs).Contains(ctrlS) && !map.Get(Shortcuts.Save).Contains(ctrlS), "Reassign moves it from Save to Save as");
		Check(File.ReadAllText(ShortcutMap.FilePath).Contains("Ctrl+S"), "and the change is saved");

		Descendants(view).OfType<Button>().First(b => b.Text == "Reset All").EmitSignal(BaseButton.SignalName.Pressed);
		await Frames(3);
		Check(map.Get(Shortcuts.Save).SequenceEqual(ShortcutMap.DefaultsFor(Shortcuts.Save)) && map.Get(Shortcuts.SaveAs).SequenceEqual(ShortcutMap.DefaultsFor(Shortcuts.SaveAs)), "Reset All puts every shortcut back");

		// interface scale follows the setting in every window
		AppSettings.Current.InterfaceScale = InterfaceScale.Percent150;
		AppSettings.Current.Save();
		await Frames(3);
		Check(Mathf.IsEqualApprox(window.ContentScaleFactor, 1.5f), $"Interface scale reaches the window: {window.ContentScaleFactor}");
		AppSettings.Current.InterfaceScale = InterfaceScale.Percent100;
		AppSettings.Current.Save();
		await Frames(3);

		// Follow system after an explicit pick lands on the OS's own
		bool osDark = DisplayServer.IsDarkMode();
		theme?.ChoosePalette(osDark ? EditSharpTheme.LightPalette : EditSharpTheme.DarkPalette);
		await Frames(3);
		theme?.ChoosePalette(EditSharpTheme.SystemPalette);
		await Frames(3);
		string expected = osDark ? EditSharpTheme.DarkPalette : EditSharpTheme.LightPalette;
		Check(theme?.Palette?.ResourcePath == expected, $"Follow system after {(osDark ? "Light" : "Dark")} switches to {expected}: os dark {osDark}, now {theme?.Palette?.ResourcePath}");

		// the same through the Theme dropdown
		Section("Appearance").EmitSignal(BaseButton.SignalName.Pressed);
		await Frames(5);
		OptionButton ThemeDropdown() => Descendants(view).OfType<OptionButton>().First(o => o.IsVisibleInTree() && Enumerable.Range(0, o.ItemCount).Any(i => o.GetItemText(i) == "Dark"));
		ThemeDropdown().EmitSignal(OptionButton.SignalName.ItemSelected, osDark ? 2 : 1);
		await Frames(5);
		GD.Print($"PROBE after picking explicit: {theme?.Palette?.ResourcePath}, choice {theme?.PaletteChoice}, shown {ThemeDropdown().Text}");
		ThemeDropdown().EmitSignal(OptionButton.SignalName.ItemSelected, 0);
		await Frames(5);
		GD.Print($"PROBE after picking follow: {theme?.Palette?.ResourcePath}, choice {theme?.PaletteChoice}, shown {ThemeDropdown().Text}");
		Check(theme?.Palette?.ResourcePath == expected, "and through the dropdown");

		// and with the real mouse and keys: open the dropdown, arrow to an item, Enter
		async Task Pick(int index)
		{
			OptionButton dropdown = ThemeDropdown();
			Vector2I at = Halide.Scripts.UI.ContextMenu.ContextMenus.ToScreen(dropdown.GetViewport(), dropdown.GetGlobalRect().GetCenter());
			GetCursorPos(out POINT now);
			Vector2I offset = new Vector2I(now.x, now.y) - DisplayServer.MouseGetPosition();
			window.GrabFocus();
			await Frames(3);
			SetCursorPos(at.X + offset.X, at.Y + offset.Y);
			await Frames(3);
			mouse_event(2, 0, 0, 0, 0); await Frames(3); mouse_event(4, 0, 0, 0, 0);
			await Frames(10);
			// the open list: items share its height evenly
			PopupMenu popup = dropdown.GetPopup();
			float row = popup.Size.Y / (float)popup.ItemCount;
			Vector2 item = new(popup.Position.X + popup.Size.X / 2f, popup.Position.Y + row * (index + 0.5f));
			Vector2I itemAt = Halide.Scripts.UI.ContextMenu.ContextMenus.ToScreen(window, item / window.ContentScaleFactor);
			GD.Print($"PROBE popup visible {popup.Visible} at {popup.Position} size {popup.Size}, clicking {index} at {itemAt}");
			SetCursorPos(itemAt.X + offset.X, itemAt.Y + offset.Y);
			await Frames(3);
			mouse_event(2, 0, 0, 0, 0); await Frames(3); mouse_event(4, 0, 0, 0, 0);
			await Frames(10);
			GD.Print($"PROBE real pick {index}: {theme?.Palette?.ResourcePath}, choice {theme?.PaletteChoice}, shown {ThemeDropdown().Text}");
		}
		await Pick(osDark ? 2 : 1);
		await Settle();
		Shot(window, "picked-explicit");
		await Pick(0);
		await Settle();
		Shot(window, "picked-follow");
		Check(theme?.Palette?.ResourcePath == expected, "and with the real mouse and keys");

		// the light theme, for the eye
		Section("Appearance").EmitSignal(BaseButton.SignalName.Pressed);
		theme?.ChoosePalette(EditSharpTheme.LightPalette);
		await Frames(5);
		await Settle();
		Shot(window, "appearance-light");
		Section("Keyboard Shortcuts").EmitSignal(BaseButton.SignalName.Pressed);
		await Frames(5);
		await Settle();
		Shot(window, "shortcuts-light");
		theme?.ChoosePalette(paletteWas);

		window.EmitSignal(Window.SignalName.CloseRequested);
		await Frames(3);
		try { Directory.Delete(folder, true); } catch (IOException) { }

		GD.Print(ok ? "SETTINGS OK" : "SETTINGS FAILED");
		GetTree().Quit();
	}

	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct POINT { public int x, y; }
	[System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
	[System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
	[System.Runtime.InteropServices.DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, nuint extra);
	[System.Runtime.InteropServices.DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);

	static ShortcutKeyButton AddButton(ShortcutRow row) => Descendants(row).OfType<ShortcutKeyButton>().Last();

	// a key pressed on a button the way the window would deliver it
	async Task Press(Window window, ShortcutKeyButton button, InputEventKey key)
	{
		button.Begin();
		await Frames(2);
		window.PushInput(key);
		await Frames(3);
	}

	void Shot(Window window, string name)
	{
		string file = ProjectSettings.GlobalizePath($"user://probe-settings-{name}.png");
		window.GetTexture().GetImage().SavePng(file);
		GD.Print($"PROBE shot {file}");
	}

	async Task Settle() => await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);

	async Task Frames(int count)
	{
		for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	static IEnumerable<Node> Descendants(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			yield return child;
			foreach (Node below in Descendants(child)) yield return below;
		}
	}
}
