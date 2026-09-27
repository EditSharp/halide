using EditSharpGUI.Scripts.UI.Theming;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// the drawn title bar: logo (Home), the host's menus and layout switcher, a centred title, and the caption buttons; laid out in TopBar.tscn
public partial class UITopBar : PanelContainer
{
	[ExportGroup("Parts")]
	[Export] Control leading;
	[Export] Button logo;
	[Export] HBoxContainer menus;
	[Export] Button layouts;
	[Export] Label title;
	[Export] HBoxContainer captionButtons;
	[Export] Button minimize;
	[Export] CaptionButton maximize;
	[Export] Button close;

	[ExportGroup("Scenes")]
	[Export] PackedScene menuButton;

	public event Action HomePressed;
	public event Action LayoutsPressed;

	// a menu's button was pressed; the host shows that menu under it
	public event Action<string, Button> MenuPressed;

	public string Title { get => title.Text; set => title.Text = value; }

	// room kept at the start for the OS's own buttons (macOS traffic lights)
	public float LeadingInset { get => leading.CustomMinimumSize.X; set => leading.CustomMinimumSize = new Vector2(value, 0); }

	// whether the bar draws minimize, maximize and close (not on macOS)
	public bool ShowsCaptionButtons { get => captionButtons.Visible; set => captionButtons.Visible = value; }

	public bool ShowsLogo { get => logo.Visible; set => logo.Visible = value; }

	// the OS's own caption glyphs from its icon font (Windows), rather than drawn ones
	public bool UseSystemGlyphs { get; set { field = value; ShowGlyphs(); } }

	// the bar's height and caption button width, in the bar's own units
	public void SetMetrics(float height, float captionWidth)
	{
		CustomMinimumSize = new Vector2(0, height);
		logo.CustomMinimumSize = new Vector2(height, height);
		foreach (Button b in new[] { minimize, maximize, close }) b.CustomMinimumSize = new Vector2(captionWidth, height);
	}

	// an inactive window's caption dims, as the OS's does
	public void SetActive(bool active)
	{
		float alpha = active ? 1f : (ThemeDB.GetProjectTheme() as EditSharpTheme)?.Palette?.DisabledAlpha ?? 0.5f;
		captionButtons.Modulate = new Color(1f, 1f, 1f, alpha);
		title.Modulate = new Color(1f, 1f, 1f, alpha);
	}

	public string LayoutName { get => layouts.Text; set { layouts.Text = value; layouts.Visible = value is not null; } }

	public override void _Ready()
	{
		logo.Pressed += () => HomePressed?.Invoke();
		layouts.Pressed += () => LayoutsPressed?.Invoke();
		layouts.Visible = false;

		minimize.Pressed += () => DisplayServer.WindowSetMode(DisplayServer.WindowMode.Minimized, GetWindow().GetWindowId());
		maximize.Pressed += ToggleMaximized;
		close.Pressed += () => GetWindow().EmitSignal(Window.SignalName.CloseRequested);

		window = GetWindow();
		window.SizeChanged += ShowMaximized;
		ShowMaximized();
	}

	Window window;

	public override void _ExitTree()
	{
		if (IsInstanceValid(window)) window.SizeChanged -= ShowMaximized;
	}

	public Button AddMenu(string name)
	{
		Button button = menuButton.Instantiate<Button>();
		button.Text = name;
		button.Pressed += () => MenuPressed?.Invoke(name, button);
		menus.AddChild(button);
		return button;
	}

	public void ToggleMaximized()
	{
		int id = GetWindow().GetWindowId();
		bool maximized = DisplayServer.WindowGetMode(id) == DisplayServer.WindowMode.Maximized;
		DisplayServer.WindowSetMode(maximized ? DisplayServer.WindowMode.Windowed : DisplayServer.WindowMode.Maximized, id);
	}

	bool Maximized => IsInsideTree() && DisplayServer.WindowGetMode(GetWindow().GetWindowId()) == DisplayServer.WindowMode.Maximized;

	void ShowMaximized()
	{
		if (!IsInsideTree()) return;
		maximize.TooltipText = Maximized ? "Restore" : "Maximize";
		ShowGlyphs();
	}

	// Segoe Fluent Icons' ChromeMinimize, ChromeMaximize, ChromeRestore and ChromeClose, or drawn glyphs elsewhere
	void ShowGlyphs()
	{
		if (minimize is null) return;
		bool maximized = Maximized;

		if (UseSystemGlyphs)
		{
			minimize.Text = "\uE921";
			maximize.Text = maximized ? "\uE923" : "\uE922";
			close.Text = "\uE8BB";
			foreach (Button b in new[] { minimize, maximize, close }) b.Icon = null;
		}
		else
		{
			// large enough to stay sharp scaled up; the theme caps their width
			minimize.Icon = IconRaster.Get(IconRaster.Shape.WindowMinimize, 30);
			maximize.Icon = IconRaster.Get(maximized ? IconRaster.Shape.WindowRestore : IconRaster.Shape.WindowMaximize, 30);
			close.Icon = IconRaster.Get(IconRaster.Shape.WindowClose, 30);
			foreach (Button b in new[] { minimize, maximize, close }) b.Text = "";
		}
	}

	// ---- for the window chrome: what's draggable and where the maximize button is ----

	public enum Region { None, Caption, Maximize }

	// what's under a point in the window's viewport: the bar's empty space drags the window
	public Region RegionAt(Vector2 at)
	{
		if (!IsVisibleInTree() || !Bounds(this).HasPoint(at)) return Region.None;
		if (maximize.IsVisibleInTree() && Bounds(maximize).HasPoint(at)) return Region.Maximize;
		return Interactive().Any(c => Bounds(c).HasPoint(at)) ? Region.None : Region.Caption;
	}

	// where a control is in the window, with the bar's own scale applied
	static Rect2 Bounds(Control c) => c.GetGlobalTransform() * new Rect2(Vector2.Zero, c.Size);

	IEnumerable<Control> Interactive()
	{
		foreach (Control c in new Control[] { logo, layouts, minimize, maximize, close }) if (c.IsVisibleInTree()) yield return c;
		foreach (Control c in menus.GetChildren().OfType<Control>()) if (c.IsVisibleInTree()) yield return c;
	}

	// the OS reports the pointer over maximize and presses on it, since those events never reach the bar
	public void ShowMaximizeHot(bool hot) => maximize.SetHot(hot);

	public void ShowMaximizePressed(bool pressed) => maximize.SetPressed(pressed);
}
