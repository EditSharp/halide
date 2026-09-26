using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using EditSharpGUI.Scripts.UI.Theming;
using Godot;
using static EditSharpGUI.Scripts.UI.ContextMenu.Platform.Windows.Win32;

namespace EditSharpGUI.Scripts.UI.ContextMenu.Platform.Windows;

// a win32 popup menu with owner-drawn items, so bold, icons, checks and the app theme all follow the model
public class WindowsHandler : PlatformHandler
{
    public override void HandleMenu(ContextMenu menu, Vector2? position = null)
    {
        int window = (int)DisplayServer.MainWindowId;
        nint hwnd = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, window);
        Vector2I at = position is Vector2 p ? ToScreen(p, window) : DisplayServer.MouseGetPosition();
        bool dark = ApplyTheme();

        using MenuPainter painter = new(hwnd, GetDpiForWindow(hwnd), dark);
        using Subclass subclass = new(hwnd, painter);

        // win32 always closes the popup on a pick, so staying open means showing it again
        while (true)
        {
            using Built built = new(menu, painter);
            SetForegroundWindow(hwnd);
            uint id = TrackPopupMenuEx(built.Menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, at.X, at.Y, hwnd, 0);
            if (id == 0 || !built.Entries.TryGetValue(id, out MenuEntry entry)) break;
            if (MenuModel.Activate(entry) ? menu.HideOnCheckableItemSelect : menu.HideOnItemSelect) break;
        }

        menu.EmitSignal(ContextMenu.SignalName.Closed);
    }

    public override Rect2I? OpenMenuRect()
    {
        // every popup menu is a "#32768" window; the thread keeps a hidden one around too
        for (nint popup = FindWindowExW(0, 0, "#32768", null); popup != 0; popup = FindWindowExW(0, popup, "#32768", null))
        {
            if (IsWindowVisible(popup) && GetWindowRect(popup, out RECT r)) return new Rect2I(r.left, r.top, r.Width, r.Height);
        }
        return null;
    }

    public override void Dismiss() => PostMessageW(MainHwnd, WM_CANCELMODE, 0, 0);

    // the menu loop takes keyboard messages off the thread queue, whichever window they name
    public override void HighlightNext()
    {
        PostMessageW(MainHwnd, WM_KEYDOWN, VK_DOWN, 0);
        PostMessageW(MainHwnd, WM_KEYUP, VK_DOWN, 0);
    }

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

    // routes the owner-draw messages the popup sends its owner window to the painter
    sealed class Subclass : IDisposable
    {
        readonly nint hwnd;
        readonly MenuPainter painter;
        readonly SubclassProc proc;   // kept alive for as long as it is installed

        public Subclass(nint hwnd, MenuPainter painter)
        {
            this.hwnd = hwnd;
            this.painter = painter;
            proc = Handle;
            SetWindowSubclass(hwnd, proc, 1, 0);
        }

        public void Dispose() => RemoveWindowSubclass(hwnd, proc, 1);

        readonly HashSet<nint> styled = [];

        // a popup window exists from its WM_INITMENUPOPUP on, before it shows. on windows 11 dwm rounds its
        // corners, colours its border and casts its shadow, the parts of the frame the painter cannot reach.
        // that has to happen before the popup shows: once shown, the classic drop shadow (a tight dark rim)
        // is already up and can outlive the switch, so the class loses it for good
        void StylePopups()
        {
            if (System.Environment.OSVersion.Version.Build < 22000) return;
            uint thread = GetCurrentThreadId();
            for (nint popup = FindWindowExW(0, 0, "#32768", null); popup != 0; popup = FindWindowExW(0, popup, "#32768", null))
            {
                if (GetWindowThreadProcessId(popup, 0) != thread || !styled.Add(popup)) continue;

                nuint style = GetClassLongPtrW(popup, GCL_STYLE);
                if ((style & CS_DROPSHADOW) != 0) SetClassLongPtrW(popup, GCL_STYLE, style & ~(nuint)CS_DROPSHADOW);

                uint corner = DWMWCP_ROUNDSMALL;
                uint border = painter.BorderColor;
                DwmSetWindowAttribute(popup, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(uint));
                DwmSetWindowAttribute(popup, DWMWA_BORDER_COLOR, ref border, sizeof(uint));
            }
        }

        nint Handle(nint window, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
        {
            try
            {
                if (msg == WM_INITMENUPOPUP)
                {
                    StylePopups();
                }
                else if (msg == WM_MEASUREITEM)
                {
                    StylePopups();
                    MEASUREITEMSTRUCT m = Marshal.PtrToStructure<MEASUREITEMSTRUCT>(lParam);
                    if (m.CtlType == ODT_MENU && painter.TryGet(m.itemData, out MenuPainter.Item item))
                    {
                        painter.Measure(item, out m.itemWidth, out m.itemHeight);
                        Marshal.StructureToPtr(m, lParam, false);
                        return 1;
                    }
                }
                else if (msg == WM_DRAWITEM)
                {
                    DRAWITEMSTRUCT d = Marshal.PtrToStructure<DRAWITEMSTRUCT>(lParam);
                    if (d.CtlType == ODT_MENU && painter.TryGet(d.itemData, out MenuPainter.Item item))
                    {
                        StylePopups();
                        painter.Draw(item, d);
                        return 1;
                    }
                }
            }
            catch (Exception e)
            {
                GD.PushError(e.ToString());
            }

            return DefSubclassProc(window, msg, wParam, lParam);
        }
    }

    // the HMENU tree plus everything that has to outlive it
    sealed class Built : IDisposable
    {
        public readonly nint Menu;
        public readonly Dictionary<uint, MenuEntry> Entries = [];

        readonly MenuPainter painter;
        readonly List<nint> bitmaps = [];
        uint nextId = 1;

        public Built(ContextMenu menu, MenuPainter painter)
        {
            this.painter = painter;
            painter.Clear();
            Menu = Fill(MenuModel.Flatten(menu.Elements));

            if (painter.BackgroundBrush != 0)
            {
                MENUINFO info = new() { cbSize = (uint)Marshal.SizeOf<MENUINFO>(), fMask = MIM_BACKGROUND | MIM_APPLYTOSUBMENUS, hbrBack = painter.BackgroundBrush };
                SetMenuInfo(Menu, ref info);
            }
        }

        public void Dispose()
        {
            DestroyMenu(Menu);
            foreach (nint bitmap in bitmaps) DeleteObject(bitmap);
        }

        nint Fill(List<MenuItem> items)
        {
            nint menu = CreatePopupMenu();
            bool hasIcons = MenuModel.AnyIcons(items);
            uint at = 0;

            foreach (MenuItem item in items)
            {
                nint icon = IconBitmap(item.Icon, painter.IconSize);
                if (icon != 0) bitmaps.Add(icon);

                MenuPainter.Item row = new()
                {
                    Kind = item.Kind,
                    Text = item.Text,
                    Hint = item.Hint,
                    Bold = item.Bold,
                    Enabled = item.Enabled,
                    Checked = item.Checked,
                    Radio = item.Radio,
                    Icon = icon,
                    ColumnHasIcons = hasIcons,
                };

                uint id = 0;
                if (item.Entry is not null)
                {
                    id = nextId++;
                    Entries[id] = item.Entry;
                }

                nint submenu = item.Kind == MenuItemKind.Submenu ? Fill(item.Submenu) : 0;
                Insert(menu, at++, row, id, submenu);
            }

            return menu;
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

        // a premultiplied 32-bit DIB, which is what AlphaBlend needs
        static nint IconBitmap(Texture2D texture, int size)
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
}
