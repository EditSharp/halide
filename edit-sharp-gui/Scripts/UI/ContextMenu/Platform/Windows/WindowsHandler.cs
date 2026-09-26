using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using EditSharpGUI.Scripts.UI.Theming;
using Godot;
using static EditSharpGUI.Scripts.UI.ContextMenu.Platform.Windows.Win32;

namespace EditSharpGUI.Scripts.UI.ContextMenu.Platform.Windows;

// a win32 popup menu with owner-drawn items, so bold, icons, checks and the app theme all follow the model.
// the menu is tracked on a thread of its own (MenuThread), so godot's frame loop runs on while it is up
public class WindowsHandler : PlatformHandler
{
    public override void HandleMenu(ContextMenu menu, Vector2? position = null)
    {
        int window = (int)DisplayServer.MainWindowId;
        nint hwnd = MainHwnd;
        Vector2I at = position is Vector2 p ? ToScreen(p, window) : DisplayServer.MouseGetPosition();
        bool dark = ApplyTheme();

        MenuThread.Show(new MenuThread.Showing(menu, Snapshot.Of(menu), at, dark, GetDpiForWindow(hwnd), hwnd, menu.HideOnItemSelect, menu.HideOnCheckableItemSelect));
    }

    public override Rect2I? OpenMenuRect()
    {
        // every popup menu is a "#32768" window; the thread keeps a hidden one around too
        for (nint popup = FindWindowExW(0, 0, "#32768", null); popup != 0; popup = FindWindowExW(0, popup, "#32768", null))
        {
            if (!IsWindowVisible(popup) || !GetWindowRect(popup, out RECT rect)) continue;
            return new Rect2I(rect.left, rect.top, rect.Width, rect.Height);
        }

        return null;
    }

    public override void Dismiss() => PostMessageW(MenuThread.Helper, WM_CANCELMODE, 0, 0);

    // the menu loop takes keyboard messages off its thread's queue, whichever window they name
    public override void HighlightNext()
    {
        PostMessageW(MenuThread.Helper, WM_KEYDOWN, VK_DOWN, 0);
        PostMessageW(MenuThread.Helper, WM_KEYUP, VK_DOWN, 0);
    }

    public override void ActivateHighlighted()
    {
        PostMessageW(MenuThread.Helper, WM_KEYDOWN, VK_RETURN, 0);
        PostMessageW(MenuThread.Helper, WM_KEYUP, VK_RETURN, 0);
    }

    const int VK_RETURN = 0x0D;

    static nint MainHwnd => (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, (int)DisplayServer.MainWindowId);

    static Vector2I ToScreen(Vector2 viewportPosition, int window)
    {
        Viewport root = ((SceneTree)Engine.GetMainLoop()).Root;
        Vector2 inWindow = root.GetScreenTransform() * viewportPosition;
        return DisplayServer.WindowGetPosition(window) + (Vector2I)inWindow.Round();
    }

    // win32 menus only go dark through undocumented uxtheme exports (windows 10 1903+); returns whether they did
    static bool ApplyTheme()
    {
        bool dark = (ThemeDB.GetProjectTheme() as EditSharpTheme)?.Palette?.Dark ?? false;
        if (System.Environment.OSVersion.Version.Build < 18362) return false;
        try
        {
            SetPreferredAppMode(dark ? PreferredAppMode.ForceDark : PreferredAppMode.ForceLight);
            FlushMenuThemes();
            return dark;
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }
}

// the HMENU tree built from a snapshot, plus everything that has to outlive it. menu thread only
sealed class Built : IDisposable
{
    public readonly nint Menu;
    public readonly Dictionary<uint, MenuEntry> Entries = [];

    // every row in tree order: which popup holds it, where, its id and its painted look
    public readonly List<(nint Menu, uint Position, uint Id, MenuPainter.Item Row)> Rows = [];

    readonly MenuPainter painter;
    readonly Snapshot snapshot;
    uint nextId = 1;

    public Built(Snapshot snapshot, MenuPainter painter)
    {
        this.painter = painter;
        this.snapshot = snapshot;
        painter.Clear();
        Menu = Fill(snapshot.Items);

        if (painter.BackgroundBrush != 0)
        {
            MENUINFO info = new() { cbSize = (uint)Marshal.SizeOf<MENUINFO>(), fMask = MIM_BACKGROUND | MIM_APPLYTOSUBMENUS, hbrBack = painter.BackgroundBrush };
            SetMenuInfo(Menu, ref info);
        }
    }

    public void Dispose() => DestroyMenu(Menu);

    nint Fill(List<MenuItem> items)
    {
        nint menu = CreatePopupMenu();
        bool hasIcons = MenuModel.AnyIcons(items);
        uint at = 0;

        foreach (MenuItem item in items)
        {
            MenuPainter.Item row = new()
            {
                Kind = item.Kind,
                Text = item.Text,
                Hint = item.Hint,
                Bold = item.Bold,
                Enabled = item.Enabled,
                Checked = item.Checked,
                Radio = item.Radio,
                Icon = snapshot.IconOf(item.Icon),
                ColumnHasIcons = hasIcons,
            };

            uint id = 0;
            if (item.Entry is not null)
            {
                id = nextId++;
                Entries[id] = item.Entry;
            }

            // the row goes in before its submenu's rows, in the order Update walks them
            Rows.Add((menu, at, id, row));
            nint submenu = item.Kind == MenuItemKind.Submenu ? Fill(item.Submenu) : 0;
            Insert(menu, at++, row, id, submenu);
        }

        return menu;
    }

    // the same menu after a pick that kept it open: every row takes the
    // state the fresh snapshot gives it, in place. false when the snapshot
    // no longer has the same rows, so the menu has to be built again
    public bool Update(Snapshot next)
    {
        List<MenuItem> flat = [];
        Walk(next.Items, flat);
        if (flat.Count != Rows.Count) return false;

        for (int i = 0; i < flat.Count; i++)
        {
            MenuItem item = flat[i];
            (nint menu, uint position, uint id, MenuPainter.Item row) = Rows[i];
            if (item.Kind != row.Kind || (item.Entry is null) != (id == 0)) return false;

            row.Text = item.Text;
            row.Hint = item.Hint;
            row.Bold = item.Bold;
            row.Enabled = item.Enabled;
            row.Checked = item.Checked;
            row.Radio = item.Radio;
            row.Icon = next.IconOf(item.Icon);

            if (item.Entry is not null) Entries[id] = item.Entry;

            MENUITEMINFO info = new()
            {
                cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
                fMask = MIIM_STATE,
                fState = (item.Enabled ? 0 : MFS_DISABLED) | (item.Checked ? MFS_CHECKED : 0),
            };
            SetMenuItemInfoW(menu, position, true, ref info);
        }

        return true;
    }

    static void Walk(List<MenuItem> items, List<MenuItem> into)
    {
        foreach (MenuItem item in items)
        {
            into.Add(item);
            if (item.Kind == MenuItemKind.Submenu) Walk(item.Submenu, into);
        }
    }

    void Insert(nint menu, uint at, MenuPainter.Item item, uint id, nint submenu)
    {
        MENUITEMINFO info = new()
        {
            cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
            fMask = MIIM_FTYPE | MIIM_STATE | MIIM_ID | MIIM_DATA,
            fType = MFT_OWNERDRAW | (item.Kind == MenuItemKind.Separator ? MFT_SEPARATOR : 0) | (item.Radio ? MFT_RADIOCHECK : 0),
            fState = (item.Enabled ? 0 : MFS_DISABLED) | (item.Checked ? MFS_CHECKED : 0),
            wID = id,
            dwItemData = painter.Register(item),
        };

        if (submenu != 0)
        {
            info.fMask |= MIIM_SUBMENU;
            info.hSubMenu = submenu;
        }

        InsertMenuItemW(menu, at, true, ref info);
    }

    // a premultiplied 32-bit DIB, which is what AlphaBlend needs. main thread: it reads the texture
    public static nint IconBitmap(Texture2D texture, int size)
    {
        Image source = texture?.GetImage();
        if (source is null || source.IsEmpty()) return 0;

        if (source.IsCompressed()) source.Decompress();
        source.Convert(Image.Format.Rgba8);

        // fit into the square, keeping aspect
        float scale = (float)size / Mathf.Max(source.GetWidth(), source.GetHeight());
        int w = Mathf.Max(1, Mathf.RoundToInt(source.GetWidth() * scale));
        int h = Mathf.Max(1, Mathf.RoundToInt(source.GetHeight() * scale));
        source.Resize(w, h, Image.Interpolation.Lanczos);

        Image icon = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        icon.BlitRect(source, new Rect2I(0, 0, w, h), new Vector2I((size - w) / 2, (size - h) / 2));

        byte[] rgba = icon.GetData();
        byte[] bgra = new byte[rgba.Length];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            int a = rgba[i + 3];
            bgra[i] = (byte)(rgba[i + 2] * a / 255);
            bgra[i + 1] = (byte)(rgba[i + 1] * a / 255);
            bgra[i + 2] = (byte)(rgba[i] * a / 255);
            bgra[i + 3] = (byte)a;
        }

        BITMAPINFOHEADER header = new()
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = size,
            biHeight = -size, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };

        nint bitmap = CreateDIBSection(0, ref header, DIB_RGB_COLORS, out nint bits, 0, 0);
        if (bitmap == 0) return 0;

        Marshal.Copy(bgra, 0, bits, bgra.Length);
        return bitmap;
    }
}
