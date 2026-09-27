using EditSharpGUI.Scripts.App.Commands;
using EditSharpGUI.Scripts.Input;
using EditSharpGUI.Scripts.UI.ContextMenu;
using Godot;
using System.Collections.Generic;
using System.Linq;

namespace EditSharpGUI.Scripts.App.Chrome.Platform;

// the macOS menu bar: the app menu, then File, Edit, View and Playback from the same menus and commands as the
// in-window bar, each rebuilt as it opens so what's greyed out and ticked is current
static class MacGlobalMenu
{
	static readonly Dictionary<string, Rid> menus = [];
	static readonly Dictionary<string, List<Rid>> submenus = [];
	static bool installed;

	public static void Install()
	{
		if (installed || !NativeMenu.HasFeature(NativeMenu.Feature.GlobalMenu)) return;
		installed = true;

		Rid app = NativeMenu.GetSystemMenu(NativeMenu.SystemMenus.ApplicationMenuId);
		NativeMenu.AddItem(app, "About EditSharp", Callable.From((Variant _) => ShowAbout()), index: 0);
		NativeMenu.AddSeparator(app, 1);
		NativeMenu.AddItem(app, "Settings…", Callable.From((Variant _) => Commands.Commands.Run(Shortcuts.ShowSettings)), accelerator: Key.Comma | (Key)KeyModifierMask.MaskCmdOrCtrl, index: 2);
		NativeMenu.AddSeparator(app, 3);

		Rid main = NativeMenu.GetSystemMenu(NativeMenu.SystemMenus.MainMenuId);
		foreach ((string name, Key _) in BarMenus.Menus)
		{
			Rid menu = NativeMenu.CreateMenu();
			menus[name] = menu;
			string captured = name;
			NativeMenu.SetPopupOpenCallback(menu, Callable.From(() => Rebuild(captured)));
			NativeMenu.AddSubmenuItem(main, name, menu);
			Rebuild(name);
		}
	}

	static void Rebuild(string name)
	{
		Rid menu = menus[name];
		NativeMenu.Clear(menu);

		// the last build's submenus go with it
		foreach (Rid old in submenus.GetValueOrDefault(name) ?? []) NativeMenu.FreeMenu(old);
		submenus[name] = [];

		Add(menu, BarMenus.Build(name, CommandContext.Current).Elements, submenus[name]);
	}

	static void Add(Rid menu, IEnumerable<ContextElement> elements, List<Rid> made)
	{
		foreach (ContextElement element in elements.Where(e => e.Visible))
		{
			switch (element)
			{
				case ContextDivider:
					NativeMenu.AddSeparator(menu);
					break;

				case ContextSubmenu sub:
					Rid child = NativeMenu.CreateMenu();
					made.Add(child);
					Add(child, sub.Elements, made);
					NativeMenu.AddSubmenuItem(menu, sub.Text?.Text ?? "", child);
					break;

				case ContextButton button:
				{
					Callable press = Callable.From((Variant _) => button.EmitSignal(ContextButton.SignalName.Pressed));
					Key accel = Accelerator(button.Id);
					int index = button.Type == ContextButton.CheckType.None
						? NativeMenu.AddItem(menu, button.Text?.Text ?? "", press, accelerator: accel)
						: NativeMenu.AddCheckItem(menu, button.Text?.Text ?? "", press, accelerator: accel);
					NativeMenu.SetItemChecked(menu, index, button.Checked);
					NativeMenu.SetItemDisabled(menu, index, !button.Enabled);
					break;
				}

				case ContextRadioList list:
					for (int i = 0; i < list.Buttons.Count; i++)
					{
						ContextButton button = list.Buttons[i];
						int picked = i;
						Callable pick = Callable.From((Variant _) =>
						{
							list.SelectedButton = picked;
							foreach (ContextButton b in list.Buttons) b.Checked = b == button;
							list.EmitSignal(ContextRadioList.SignalName.Selected, button);
						});
						int index = NativeMenu.AddRadioCheckItem(menu, button.Text?.Text ?? "", pick, accelerator: Accelerator(button.Id));
						NativeMenu.SetItemChecked(menu, index, i == list.SelectedButton);
						NativeMenu.SetItemDisabled(menu, index, !button.Enabled);
					}
					break;

				case ContextText text:
					NativeMenu.SetItemDisabled(menu, NativeMenu.AddItem(menu, text.Text), true);
					break;
			}
		}
	}

	// the first key bound to the command, as the menu shows it
	static Key Accelerator(string id)
	{
		if (InputManager.Singleton?.Keyboard.Shortcuts.Get(id) is not { Count: > 0 } combos) return Key.None;

		KeyCombo combo = combos[0];
		Key key = combo.Key;
		if (combo.Control) key |= (Key)KeyModifierMask.MaskCmdOrCtrl;
		if (combo.Shift) key |= (Key)KeyModifierMask.MaskShift;
		if (combo.Alt) key |= (Key)KeyModifierMask.MaskAlt;
		return key;
	}

	static void ShowAbout()
	{
		string version = ProjectSettings.GetSetting("application/config/version", "").AsString();
		_ = UI.Dialogs.Dialogs.Show(UI.Dialogs.Dialogs.Question("About EditSharp", "EditSharp", string.IsNullOrEmpty(version) ? "A node-based video editor." : $"Version {version}",
			("ok", "OK", UI.Dialogs.DialogButtonRole.Default)), ((SceneTree)Engine.GetMainLoop()).Root);
	}
}
