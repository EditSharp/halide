using System;
using System.IO;
using System.Threading;
using Halide.Scripts.UI.ContextMenu;
using Halide.Scripts.UI.Theming;
using Godot;

// opens a context menu through this OS's native handler, reports where it is, holds it, dismisses it and quits.
// driven by Tools/context_menu_preview.py, which captures the screen region it reports.
//
//   godot --path . res://Tools/Scenes/Tools/ContextMenuPreview.tscn -- [--menu=res://Some.tres] [--hold=2000] [--out=rect.txt]
//                                                                       [--hover=N] [--palette=res://Themes/Light.tres]
//
// --out receives "x y w h" in screen pixels once the popup is up, or "anchor x y" (where its top-left was
// asked to be) when the platform cannot tell its rect.
// without --menu a showcase of every item kind is shown
public partial class ContextMenuPreview : Control
{
	// the window settles before the menu opens, as it has by the time anyone clicks
	public override async void _Ready()
	{
		for (int i = 0; i < 30; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		string menuPath = null, outPath = null, palettePath = null;
		int hold = 2000, hover = 0;
		foreach (string arg in OS.GetCmdlineUserArgs())
		{
			if (arg.StartsWith("--menu=")) menuPath = arg["--menu=".Length..];
			else if (arg.StartsWith("--out=")) outPath = arg["--out=".Length..];
			else if (arg.StartsWith("--hold=")) hold = int.Parse(arg["--hold=".Length..]);
			else if (arg.StartsWith("--hover=")) hover = int.Parse(arg["--hover=".Length..]);
			else if (arg.StartsWith("--palette=")) palettePath = arg["--palette=".Length..];
		}

		// a palette swap restyles the menu too, so light and dark can both be previewed
		if (palettePath is not null && ThemeDB.GetProjectTheme() is EditSharpTheme theme) theme.Palette = GD.Load<ThemePalette>(palettePath);

		ContextMenu menu = menuPath is null ? Showcase() : GD.Load<ContextMenu>(menuPath);
		if (menu is null || ContextMenus.Handler is null)
		{
			GD.PrintErr(menu is null ? $"PREVIEW FAIL could not load {menuPath}" : $"PREVIEW FAIL no handler for {OS.GetName()}");
			Report(outPath, "fail");
			GetTree().Quit(1);
			return;
		}

		menu.Closed += () =>
		{
			GD.Print("PREVIEW closed");
			GetTree().Quit();
		};
		Vector2 at = new(40, 40);
		ContextMenus.ShowContextMenu(menu, this, at);

		// where the menu's top-left was asked to be, in the screen units the platform's capture tool uses:
		// pixels, or points on macos
		Vector2I anchor = DisplayServer.WindowGetPosition(GetWindow().GetWindowId()) + (Vector2I)(GetViewport().GetScreenTransform() * at).Round();
		if (OS.GetName() == "macOS") anchor = (Vector2I)((Vector2)anchor / (float)Math.Max(1.0, DisplayServer.ScreenGetMaxScale())).Round();

		// the native menu blocks the main thread, so the timing runs on another
		new Thread(() =>
		{
			Rect2I? rect = null;
			for (int i = 0; i < 100 && rect is null; i++)
			{
				Thread.Sleep(50);
				rect = ContextMenus.Handler.OpenMenuRect();
			}

			// --hover=N highlights the nth selectable item, to show the hot look
			for (int i = 0; i < hover; i++)
			{
				ContextMenus.Handler.HighlightNext();
				Thread.Sleep(30);
			}

			string report = rect is Rect2I r ? $"{r.Position.X} {r.Position.Y} {r.Size.X} {r.Size.Y}" : $"anchor {anchor.X} {anchor.Y}";
			GD.Print($"PREVIEW rect {report}");
			Report(outPath, report);

			Thread.Sleep(hold);
			ContextMenus.Handler.Dismiss();
		}).Start();
	}

	static void Report(string path, string text)
	{
		if (path is not null) File.WriteAllText(path, text);
	}

	static ContextMenu Showcase()
	{
		Texture2D icon = GD.Load<Texture2D>("res://icon.svg");
		return new()
		{
			Elements = [
				new ContextText("Header") { TextWeight = ContextText.Weight.Bold },
				new ContextButton { Text = new("Bold one") { TextWeight = ContextText.Weight.Bold }, Icon = icon },
				new ContextButton { Text = new("Bold two") { TextWeight = ContextText.Weight.Bold } },
				new ContextButton { Text = new("Plain"), ShortcutHint = new("Ctrl+P") },
				new ContextButton { Text = new("Disabled"), Enabled = false, Icon = icon },
				new ContextDivider(),
				new ContextButton { Text = new("Checked"), Type = ContextButton.CheckType.Check, Checked = true },
				new ContextButton { Text = new("Checked with icon"), Type = ContextButton.CheckType.Check, Checked = true, Icon = icon },
				new ContextButton { Text = new("Unchecked"), Type = ContextButton.CheckType.Check },
				new ContextDivider(),
				new ContextRadioList
				{
					Buttons = [new() { Text = new("Radio A") }, new() { Text = new("Radio B") }, new() { Text = new("Radio C"), Icon = icon }],
					SelectedButton = 1,
				},
				new ContextDivider(),
				new ContextSubmenu { Text = new("Submenu"), Icon = icon, Elements = [new ContextButton { Text = new("Inner") }] },
				new ContextSubmenu { Text = new("Disabled submenu"), Enabled = false, Elements = [new ContextButton { Text = new("Inner") }] },
			]
		};
	}
}
