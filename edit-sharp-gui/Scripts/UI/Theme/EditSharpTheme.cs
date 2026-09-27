using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace EditSharpGUI.Scripts.UI.Theming;

// the app's theme: an ordinary godot theme whose styleboxes are
// ThemedStyleBoxes resolving through a palette when they draw, plus the
// palette itself and the bindings for the items godot reads as bare
// values - colours and fonts - which this writes from the palette. that is
// all it does: no item is generated, derived or regenerated here. items
// are authored in godot's theme editor; the palette is a resource of its
// own; swapping the palette restyles everything
[Tool, GlobalClass]
public partial class EditSharpTheme : Theme
{
	public EditSharpTheme()
	{
		Callable on = new(this, MethodName.OnChanged);
		if (!IsConnected(Resource.SignalName.Changed, on)) Connect(Resource.SignalName.Changed, on);

		// while the file is being read every setter fires on a theme whose
		// items are not all there yet; the bindings are written once after
		QueueApply(this);
	}

	// ---- the palette ----

	ThemePalette _palette;

	// every ThemeStyle among the items follows the theme's palette, so a
	// swap here is a swap everywhere
	[Export] public ThemePalette Palette
	{
		get => _palette;
		set
		{
			if (_palette == value) return;
			Callable on = new(this, MethodName.OnPaletteChanged);
			if (_palette is not null && _palette.IsConnected(Resource.SignalName.Changed, on)) _palette.Disconnect(Resource.SignalName.Changed, on);
			_palette = value;
			if (_palette is not null && !_palette.IsConnected(Resource.SignalName.Changed, on)) _palette.Connect(Resource.SignalName.Changed, on);

			if (!loaded) return;

			RepointStyles();
			ApplyBindings();
		}
	}

	// the styles found among this theme's stylebox items
	public IEnumerable<ThemeStyle> Styles()
	{
		HashSet<ThemeStyle> seen = [];

		foreach (string type in GetTypeList())
		{
			foreach (string item in GetStyleboxList(type))
			{
				if (GetStylebox(item, type) is ThemedStyleBox box && box.Style is ThemeStyle style && seen.Add(style)) yield return style;
			}
		}
	}

	void RepointStyles()
	{
		foreach (ThemeStyle style in Styles()) style.Palette = _palette;
	}

	// the colour a definition resolves to under the current palette
	public Color Resolve(ThemeDefinition definition, ThemeShade shade = ThemeShade.None, float alpha = 1f)
		=> _palette?.Resolve(definition, shade, alpha) ?? Colors.Magenta;

	public Color Resolve(ClipSwatch swatch) => _palette?.Resolve(swatch) ?? Colors.Magenta;

	public Color Resolve(NodeSwatch swatch) => _palette?.Resolve(swatch) ?? Colors.Magenta;

	// ---- bindings: the items godot reads as values ----

	[ExportGroup("Bindings")]
	[Export] public Array<ColorBinding> ColorBindings { get; set; } = new();
	[Export] public Array<FontBinding> FontBindings { get; set; } = new();

	public ColorBinding FindColorBinding(string type, string item)
	{
		foreach (ColorBinding b in ColorBindings) if (b is not null && b.ThemeType == type && b.Item == item) return b;
		return null;
	}

	public FontBinding FindFontBinding(string type)
	{
		foreach (FontBinding b in FontBindings) if (b is not null && b.ThemeType == type) return b;
		return null;
	}

	bool applying;
	bool loaded;
	bool dying;

	// writes every bound colour and font item from the palette. only what
	// differs, since every write re-themes the controls that use the item
	public void ApplyBindings()
	{
		if (applying || dying || _palette is null) return;
		applying = true;
		loaded = true;

		try
		{
			Font regular = _palette.ResolveFont(ThemeFontFamily.UI, ThemeFontWeight.Regular);
			if (regular is not null && DefaultFont != regular) DefaultFont = regular;
			if (DefaultFontSize != _palette.FontSize) DefaultFontSize = _palette.FontSize;

			foreach (ColorBinding binding in ColorBindings) binding?.Apply(this, _palette);
			foreach (FontBinding binding in FontBindings) binding?.Apply(this, _palette);
		}
		finally
		{
			applying = false;
		}
	}

	void OnPaletteChanged() => QueueApply(this);

	// an item edited - a binding in the inspector, say - rewrites the bound
	// items, once, at the end of the frame
	void OnChanged()
	{
		if (applying || dying || !loaded) return;
		QueueApply(this);
	}

	public override void _Notification(int what)
	{
		if (what == NotificationPredelete)
		{
			dying = true;
			Callable on = new(this, MethodName.OnChanged);
			if (IsConnected(Resource.SignalName.Changed, on)) Disconnect(Resource.SignalName.Changed, on);
			Callable onPalette = new(this, MethodName.OnPaletteChanged);
			if (_palette is not null && _palette.IsConnected(Resource.SignalName.Changed, onPalette)) _palette.Disconnect(Resource.SignalName.Changed, onPalette);
		}

		base._Notification(what);
	}

	// ---- deferred applies ----

	// queued by object and method name, never by delegate: the message queue
	// resolves it when it runs, so a theme freed before then is skipped
	// quietly and an assembly reload in the editor cannot leave it dangling
	bool flushQueued;

	static void QueueApply(EditSharpTheme theme)
	{
		if (theme.flushQueued || theme.dying) return;
		theme.flushQueued = true;
		new Callable(theme, MethodName.FlushPending).CallDeferred();
	}

	void FlushPending()
	{
		flushQueued = false;
		if (dying) return;

		loaded = true;
		RepointStyles();
		ApplyBindings();
	}

	// ---- reloading from disk ----

	// loads the file afresh and becomes it - items, palette, bindings - so
	// every control already using this theme follows
	public bool ReloadFrom(string path)
	{
		if (ResourceLoader.Load(path, "", ResourceLoader.CacheMode.Ignore) is not EditSharpTheme fresh) return false;

		applying = true;

		try
		{
			Clear();
			MergeWith(fresh);
			ColorBindings = new Array<ColorBinding>(fresh.ColorBindings);
			FontBindings = new Array<FontBinding>(fresh.FontBindings);
		}
		finally
		{
			applying = false;
		}

		// the merge shares the copy's items with this theme; the copy must
		// let go of them - and of everything else - before it dies, or its
		// connections to them outlive it
		fresh.dying = true;
		fresh.applying = true;
		fresh.Palette = null;
		Callable on = new(fresh, MethodName.OnChanged);
		if (fresh.IsConnected(Resource.SignalName.Changed, on)) fresh.Disconnect(Resource.SignalName.Changed, on);
		fresh.Clear();
		fresh.ColorBindings = new Array<ColorBinding>();
		fresh.FontBindings = new Array<FontBinding>();
		fresh.Dispose();

		RepointStyles();
		ApplyBindings();
		return true;
	}

	// ---- the user's choice of palette and accent, kept between runs ----

	public const string SettingsPath = "user://theme.json";

	// where the user's choice is kept; tests point it elsewhere
	public static string SettingsFile { get; set; } = SettingsPath;

	sealed class Settings
	{
		public string Palette { get; set; } = "";

		// an accent the user picked over the palette's own; empty for the palette's
		public string Accent { get; set; } = "";
	}

	string userAccent = "";

	public const string DarkPalette = "res://Themes/Dark.tres", LightPalette = "res://Themes/Light.tres", SystemPalette = "system";

	// the palette picked: a palette path, SystemPalette to follow the OS, or empty for the theme's own
	public string PaletteChoice { get; private set; } = "";

	// the accent picked over the palette's own, or null
	public Color? UserAccent => Color.HtmlIsValid(userAccent) ? Color.FromHtml(userAccent) : null;

	// the palette's accent as saved, ignoring any pick
	public Color PaletteAccent => _palette is not null && ResourceLoader.Load(_palette.ResourcePath, cacheMode: ResourceLoader.CacheMode.Ignore) is ThemePalette saved ? saved.AccentColor : Colors.White;

	// picks a palette and saves the choice
	public void ChoosePalette(string choice)
	{
		PaletteChoice = choice ?? "";
		ApplyPaletteChoice();
		SaveUserSettings();
	}

	// picks an accent, or goes back to the palette's with null, and saves it
	public void ChooseAccent(Color? accent)
	{
		userAccent = accent is Color c ? "#" + c.ToHtml(false) : "";
		if (_palette is not null) _palette.AccentColor = accent ?? PaletteAccent;
		SaveUserSettings();
	}

	// the OS switched between dark and light; a system choice follows it
	public void SystemThemeChanged()
	{
		if (PaletteChoice == SystemPalette) ApplyPaletteChoice();
	}

	void ApplyPaletteChoice()
	{
		string path = PaletteChoice == SystemPalette ? (DisplayServer.IsDarkMode() ? DarkPalette : LightPalette) : PaletteChoice;
		if (string.IsNullOrWhiteSpace(path) || !ResourceLoader.Exists(path) || ResourceLoader.Load(path) is not ThemePalette chosen) return;

		if (chosen != _palette) Palette = chosen;
		_palette.AccentColor = UserAccent ?? PaletteAccent;
	}

	// swaps in the palette the user chose and sets their accent on it; seeds
	// the file on first run so there is something to edit
	public void LoadUserSettings()
	{
		string file = ProjectSettings.GlobalizePath(SettingsFile);

		if (!File.Exists(file)) { SaveUserSettings(); return; }

		try
		{
			Settings settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(file)) ?? new();

			PaletteChoice = settings.Palette ?? "";
			if (!string.IsNullOrWhiteSpace(settings.Accent) && Color.HtmlIsValid(settings.Accent)) userAccent = settings.Accent;
			ApplyPaletteChoice();
			if (_palette is not null && UserAccent is Color accent) _palette.AccentColor = accent;
		}
		catch (Exception e)
		{
			GD.PushWarning($"Could not read the theme settings from {file}: {e.Message}");
		}

		loaded = true;
		RepointStyles();
		ApplyBindings();
	}

	public void SaveUserSettings()
	{
		string file = ProjectSettings.GlobalizePath(SettingsFile);

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(file));

			Settings settings = new()
			{
				Palette = PaletteChoice.Length > 0 ? PaletteChoice : _palette?.ResourcePath ?? "",
				Accent = userAccent
			};

			File.WriteAllText(file, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
		}
		catch (Exception e)
		{
			GD.PushWarning($"Could not save the theme settings to {file}: {e.Message}");
		}
	}
}
