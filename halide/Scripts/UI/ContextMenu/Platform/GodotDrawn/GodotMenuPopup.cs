using System;
using System.Collections.Generic;
using Halide.Scripts.UI.Theming;
using Godot;

namespace Halide.Scripts.UI.ContextMenu.Platform.GodotDrawn;

// a context menu drawn by godot itself: a PopupPanel of button rows that follow the app theme. the linux
// menu, and the fallback wherever no native popup exists. rows wear the theme type variation "ContextMenuItem"
// and the panel "ContextMenuPanel", so the theme can style them; without those they look like Button and PopupPanel
public partial class GodotMenuPopup : PopupPanel
{
    // a pickable row was activated. submenus open instead of firing this
    public event Action<MenuItem> Picked;

    public const int IconSize = 16;
    public const float SubmenuHoverDelay = 0.25f;

    readonly List<Row> rows = [];
    readonly VBoxContainer column = new();
    readonly GodotMenuPopup parentMenu;
    GodotMenuPopup submenu;
    Row submenuRow;
    Timer hoverTimer;

    public GodotMenuPopup(List<MenuItem> items, GodotMenuPopup parentMenu = null)
    {
        this.parentMenu = parentMenu;
        ThemeTypeVariation = "ContextMenuPanel";
        WrapControls = true;

        column.AddThemeConstantOverride("separation", 0);
        AddChild(column);
        Fill(items);

        hoverTimer = new Timer { OneShot = true, WaitTime = SubmenuHoverDelay };
        hoverTimer.Timeout += () => { if (submenuRow is not null) OpenSubmenu(submenuRow); };
        AddChild(hoverTimer);

        PopupHide += OnHide;
        WindowInput += OnInput;
    }

    // ---- rows ----

    void Fill(List<MenuItem> items)
    {
        bool hasIcons = MenuModel.AnyIcons(items);

        foreach (MenuItem item in items)
        {
            if (item.Kind == MenuItemKind.Separator)
            {
                column.AddChild(new HSeparator { MouseFilter = Control.MouseFilterEnum.Ignore });
                continue;
            }

            Row row = new(item, hasIcons);
            row.Pressed += () => Activate(row);
            row.MouseEntered += () => Hover(row);
            row.FocusEntered += () => Hover(row);
            rows.Add(row);
            column.AddChild(row);
        }
    }

    // the same menu after a pick that kept it open: check states follow the model
    public void Refresh(List<MenuItem> items)
    {
        int i = 0;
        foreach (MenuItem item in items)
        {
            if (item.Kind == MenuItemKind.Separator) continue;
            if (i < rows.Count) rows[i].Update(item);
            i++;
        }
    }

    void Activate(Row row)
    {
        if (row.Item.Kind == MenuItemKind.Submenu)
        {
            OpenSubmenu(row);
            return;
        }
        Root.Picked?.Invoke(row.Item);
    }

    GodotMenuPopup Root => parentMenu?.Root ?? this;

    // hovering a row arms its submenu, or closes the one another row had open
    void Hover(Row row)
    {
        hoverTimer.Stop();
        if (submenu is not null && submenuRow != row) CloseSubmenu();
        if (row.Item.Kind == MenuItemKind.Submenu && row.Item.Enabled && submenu is null)
        {
            submenuRow = row;
            hoverTimer.Start();
        }
    }

    void OpenSubmenu(Row row)
    {
        hoverTimer.Stop();
        if (submenu is not null)
        {
            if (submenuRow == row) return;
            CloseSubmenu();
        }

        submenuRow = row;
        submenu = new GodotMenuPopup(row.Item.Submenu, this);
        submenu.PopupHide += () => { if (submenu is not null && !submenu.Visible) DropSubmenu(); };
        AddChild(submenu);

        // to the right of the row, in the same space this popup is positioned in
        Vector2I at = Position + (Vector2I)row.GlobalPosition.Round() + new Vector2I((int)row.Size.X, -(int)column.Position.Y);
        submenu.Popup(new Rect2I(at, Vector2I.Zero));
        submenu.KeepOnScreen();
    }

    // the popup's top-left stays where it was asked for unless that would put
    // part of it off the screen (or the main window, when embedded); then it
    // moves the least distance that brings it inside
    public void KeepOnScreen()
    {
        Rect2I bounds;
        if (IsEmbedded()) bounds = new Rect2I(Vector2I.Zero, (Vector2I)GetTree().Root.GetVisibleRect().Size);
        else bounds = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen(GetWindowId()));

        Vector2I position = Position;
        Vector2I size = Size;

        if (position.X + size.X > bounds.End.X) position.X = bounds.End.X - size.X;
        if (position.Y + size.Y > bounds.End.Y) position.Y = bounds.End.Y - size.Y;
        if (position.X < bounds.Position.X) position.X = bounds.Position.X;
        if (position.Y < bounds.Position.Y) position.Y = bounds.Position.Y;

        if (position != Position) Position = position;
    }

    void CloseSubmenu()
    {
        if (submenu is null) return;
        submenu.Hide();
        DropSubmenu();
    }

    void DropSubmenu()
    {
        submenu?.QueueFree();
        submenu = null;
        submenuRow = null;
    }

    void OnHide()
    {
        hoverTimer.Stop();
        CloseSubmenu();
    }

    // ---- keyboard ----

    void OnInput(InputEvent e)
    {
        if (!e.IsPressed() || e.IsEcho()) return;

        if (e.IsActionPressed("ui_cancel"))
        {
            Root.Hide();
            SetInputAsHandled();
        }
        else if (e.IsActionPressed("ui_down") && !FocusInside)
        {
            FocusNext();
            SetInputAsHandled();
        }
        else if (e.IsActionPressed("ui_up") && !FocusInside)
        {
            FocusRow(rows.Count - 1, -1);
            SetInputAsHandled();
        }
        else if (e.IsActionPressed("ui_right") && GuiGetFocusOwner() is Row focused && focused.Item.Kind == MenuItemKind.Submenu && focused.Item.Enabled)
        {
            OpenSubmenu(focused);
            submenu?.FocusNext();
            SetInputAsHandled();
        }
        else if (e.IsActionPressed("ui_left") && parentMenu is not null)
        {
            Row back = parentMenu.submenuRow;
            Hide();
            back?.GrabFocus();
            SetInputAsHandled();
        }
    }

    bool FocusInside => GuiGetFocusOwner() is Row;

    // the next enabled row after the focused one, wrapping; the first when nothing is focused
    public void FocusNext()
    {
        int from = GuiGetFocusOwner() is Row current ? rows.IndexOf(current) + 1 : 0;
        FocusRow(from, +1);
    }

    void FocusRow(int from, int step)
    {
        if (rows.Count == 0) return;
        for (int n = 0, i = ((from % rows.Count) + rows.Count) % rows.Count; n < rows.Count; n++, i = ((i + step) % rows.Count + rows.Count) % rows.Count)
        {
            if (!rows[i].Item.Enabled) continue;
            rows[i].GrabFocus();
            return;
        }
    }

    // ---- for previews ----

    // the focused row, picked as enter would pick it
    public void ActivateFocused()
    {
        foreach (Row row in rows) if (row.HasFocus()) { Activate(row); return; }
    }

    // where this popup was on screen last frame, in screen pixels; safe to read from any thread
    public Rect2I LastScreenRect { get; private set; }

    public override void _Process(double delta)
    {
        if (Visible) LastScreenRect = ScreenRect();
    }

    // where this popup is on screen, in screen pixels
    public Rect2I ScreenRect()
    {
        if (!IsEmbedded()) return new Rect2I(Position, Size);
        Window main = GetTree().Root;
        Transform2D t = main.GetScreenTransform();
        Vector2 topLeft = t * (Vector2)Position;
        Vector2 bottomRight = t * (Vector2)(Position + Size);
        return new Rect2I(DisplayServer.WindowGetPosition(main.GetWindowId()) + (Vector2I)topLeft.Round(), (Vector2I)(bottomRight - topLeft).Round());
    }

    // ---- a row ----

    // a Button carrying its own content, so it has the button's hover, pressed, focus and disabled looks
    // while showing a check gutter, an icon, bold text, a hint and a submenu arrow
    public partial class Row : Button
    {
        public MenuItem Item { get; private set; }

        readonly HBoxContainer box = new() { MouseFilter = MouseFilterEnum.Ignore };
        readonly StyleBox flat;   // the row's own normal look when the theme has none for it
        readonly TextureRect check, icon, arrow;
        readonly Label text, hint;

        public Row(MenuItem item, bool hasIcons)
        {
            Item = item;
            ThemeTypeVariation = "ContextMenuItem";
            Disabled = !item.Enabled;
            FocusMode = item.Enabled ? FocusModeEnum.All : FocusModeEnum.None;
            MouseDefaultCursorShape = CursorShape.Arrow;

            // focus reads as hover, like a native menu, instead of an outline
            AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

            // until the theme styles ContextMenuItem, rows are flat: only hover and pressed show a box
            if (ThemeDB.GetProjectTheme()?.HasStylebox("normal", "ContextMenuItem") != true)
            {
                flat = new StyleBoxEmpty();
                AddThemeStyleboxOverride("normal", flat);
                AddThemeStyleboxOverride("disabled", flat);
            }

            box.AddThemeConstantOverride("separation", 8);
            AddChild(box);

            check = Slot();
            box.AddChild(check);

            if (hasIcons)
            {
                icon = Slot();
                icon.Texture = item.Icon;
                box.AddChild(icon);
            }

            text = new Label { Text = item.Text, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
            if (item.Bold && BoldFont() is Font bold) text.AddThemeFontOverride("font", bold);
            box.AddChild(text);

            if (item.Hint is not null)
            {
                hint = new Label { Text = item.Hint, MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                box.AddChild(new Control { CustomMinimumSize = new Vector2(16, 0), MouseFilter = MouseFilterEnum.Ignore });
                box.AddChild(hint);
            }

            if (item.Kind == MenuItemKind.Submenu)
            {
                arrow = Slot();
                box.AddChild(arrow);
            }

            MouseEntered += Recolor;
            MouseExited += Recolor;
            FocusEntered += Recolor;
            FocusExited += Recolor;

            // a button sizes itself natively, so the content it carries goes in as its custom minimum
            box.MinimumSizeChanged += Fit;

            Update(item);
            Fit();
        }

        public void Update(MenuItem item)
        {
            Item = item;
            check.Texture = item.Checked ? (item.Radio ? Mark("radio_checked", IconRaster.Shape.Bullet) : Mark("checked", IconRaster.Shape.Check)) : null;
            if (arrow is not null) arrow.Texture = Mark("submenu", IconRaster.Shape.Chevron);
            Recolor();
        }

        // the theme's icon for ContextMenuItem when it has one, else a rasterised glyph tinted like the text
        Texture2D Mark(string name, IconRaster.Shape fallback)
            => ThemeDB.GetProjectTheme()?.HasIcon(name, "ContextMenuItem") == true ? GetThemeIcon(name) : IconRaster.Get(fallback, IconSize);

        static TextureRect Slot() => new()
        {
            CustomMinimumSize = new Vector2(IconSize, IconSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };

        static Font BoldFont() => (ThemeDB.GetProjectTheme() as EditSharpTheme)?.Palette?.ResolveFont(ThemeFontFamily.UI, ThemeFontWeight.Bold);

        // the button's own text colours, applied to the labels it carries
        void Recolor()
        {
            bool hot = !Disabled && (IsHovered() || HasFocus());
            Color color = Disabled ? GetThemeColor("font_disabled_color") : hot ? GetThemeColor("font_hover_color") : GetThemeColor("font_color");
            text.AddThemeColorOverride("font_color", color);
            hint?.AddThemeColorOverride("font_color", color);
            check.Modulate = color;
            if (arrow is not null) arrow.Modulate = color;
            if (icon is not null) icon.Modulate = Disabled ? new Color(1, 1, 1, 0.5f) : Colors.White;

            // keyboard focus shows the hover look
            if (HasFocus() && !IsHovered()) AddThemeStyleboxOverride("normal", GetThemeStylebox("hover"));
            else if (flat is not null) AddThemeStyleboxOverride("normal", flat);
            else RemoveThemeStyleboxOverride("normal");
        }

        // the content sits at a fixed inset whatever stylebox is drawn, so text never shifts between states
        static readonly Vector2 Inset = new(8, 4);

        void Fit() => CustomMinimumSize = box.GetCombinedMinimumSize() + Inset * 2;

        bool restyling;

        public override void _Notification(int what)
        {
            if (what != NotificationResized && what != NotificationThemeChanged && what != NotificationReady) return;

            box.Position = Inset;
            box.Size = Size - Inset * 2;

            // the overrides Update sets raise this same notification
            if (what == NotificationThemeChanged && !restyling)
            {
                restyling = true;
                Update(Item);
                Fit();
                restyling = false;
            }
        }
    }
}
