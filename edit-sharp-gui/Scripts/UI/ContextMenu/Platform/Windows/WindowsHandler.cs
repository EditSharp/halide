using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using EditSharpGUI.Scripts.UI.ContextMenu.Platform.Windows;
using EditSharpGUI.Scripts.UI.Theming;
using Godot;
using static EditSharpGUI.Scripts.UI.ContextMenu.Platform.Windows.Win32;

namespace EditSharpGUI.Scripts.UI.ContextMenu.Platform;

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
            if (id == 0 || !built.Entries.TryGetValue(id, out Entry entry)) break;
            if (Activate(entry) ? menu.HideOnCheckableItemSelect : menu.HideOnItemSelect) break;
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

    // applies the pick to the model and emits its signal; true when the item was checkable
    static bool Activate(Entry entry)
    {
        ContextButton button = entry.Button;
        switch (entry.Owner)
        {
            case ContextCheckList list:
                bool on = !list.CheckedButtons.Remove(entry.Index);
                if (on) list.CheckedButtons.Add(entry.Index);
                button.Checked = on;
                list.EmitSignal(ContextCheckList.SignalName.Toggled, button, on);
                return true;

            case ContextRadioList list:
                list.SelectedButton = entry.Index;
                foreach (ContextButton b in list.Buttons) b.Checked = b == button;
                list.EmitSignal(ContextRadioList.SignalName.Selected, button);
                return true;

            default:
                if (button.Type == ContextButton.CheckType.Check) button.Checked = !button.Checked;
                else if (button.Type == ContextButton.CheckType.Radio) button.Checked = true;
                button.EmitSignal(ContextButton.SignalName.Pressed);
                return button.Type != ContextButton.CheckType.None;
        }
    }

    // a pickable item: the button, the list it belongs to (if any) and its index there
    record Entry(ContextButton Button, ContextElement Owner, int Index);

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
        public readonly Dictionary<uint, Entry> Entries = [];

        readonly MenuPainter painter;
        readonly List<nint> bitmaps = [];
        uint nextId = 1;

        public Built(ContextMenu menu, MenuPainter painter)
        {
            this.painter = painter;
            painter.Clear();
            Menu = Fill(menu.Elements);

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

        nint Fill(Godot.Collections.Array<ContextElement> elements)
        {
            nint menu = CreatePopupMenu();
            bool hasIcons = AnyIcons(elements);
            uint at = 0;

            foreach (ContextElement element in elements)
            {
                if (element is null || !element.Visible) continue;

                switch (element)
                {
                    case ContextDivider:
                        Insert(menu, at++, new MenuPainter.Item { Kind = MenuPainter.Kind.Separator });
                        break;

                    case ContextText text:
                        Insert(menu, at++, new MenuPainter.Item { Kind = MenuPainter.Kind.Label, Text = text.Text, Bold = Bold(text), ColumnHasIcons = hasIcons });
                        break;

                    case ContextSubmenu submenu:
                        MenuPainter.Item item = Labelled(submenu, MenuPainter.Kind.Submenu, hasIcons);
                        Insert(menu, at++, item, submenu: Fill(submenu.Elements));
                        break;

                    case ContextButton button:
                        InsertButton(menu, at++, button, null, 0, button.Checked, button.Type, hasIcons);
                        break;

                    case ContextCheckList list:
                        for (int i = 0; i < list.Buttons.Count; i++)
                        {
                            ContextButton button = list.Buttons[i];
                            if (button is null || !button.Visible) continue;
                            button.Checked = list.CheckedButtons.Contains(i);
                            InsertButton(menu, at++, button, list, i, button.Checked, ContextButton.CheckType.Check, hasIcons);
                        }
                        break;

                    case ContextRadioList list:
                        for (int i = 0; i < list.Buttons.Count; i++)
                        {
                            ContextButton button = list.Buttons[i];
                            if (button is null || !button.Visible) continue;
                            button.Checked = i == list.SelectedButton;
                            InsertButton(menu, at++, button, list, i, button.Checked, ContextButton.CheckType.Radio, hasIcons);
                        }
                        break;
                }
            }

            return menu;
        }

        static bool AnyIcons(Godot.Collections.Array<ContextElement> elements)
        {
            foreach (ContextElement element in elements)
            {
                if (element is null || !element.Visible) continue;
                switch (element)
                {
                    case ContextBaseButton button when button.Icon is not null: return true;
                    case ContextCheckList list: foreach (ContextButton b in list.Buttons) if (b is { Visible: true, Icon: not null }) return true; break;
                    case ContextRadioList list: foreach (ContextButton b in list.Buttons) if (b is { Visible: true, Icon: not null }) return true; break;
                }
            }
            return false;
        }

        void InsertButton(nint menu, uint at, ContextButton button, ContextElement owner, int index, bool isChecked, ContextButton.CheckType type, bool hasIcons)
        {
            MenuPainter.Item item = Labelled(button, MenuPainter.Kind.Button, hasIcons);
            item.Checked = isChecked;
            item.Radio = type == ContextButton.CheckType.Radio;
            uint id = nextId++;
            Entries[id] = new Entry(button, owner, index);
            Insert(menu, at, item, id);
        }

        MenuPainter.Item Labelled(ContextBaseButton button, MenuPainter.Kind kind, bool hasIcons)
        {
            nint icon = IconBitmap(button.Icon, painter.IconSize);
            if (icon != 0) bitmaps.Add(icon);
            return new MenuPainter.Item
            {
                Kind = kind,
                Text = button.Text?.Text ?? "",
                Hint = button.ShortcutHint?.Text,
                Bold = Bold(button.Text),
                Enabled = button.Enabled,
                Icon = icon,
                ColumnHasIcons = hasIcons,
            };
        }

        static bool Bold(ContextText text) => text?.TextWeight == ContextText.Weight.Bold;

        void Insert(nint menu, uint at, MenuPainter.Item item, uint id = 0, nint submenu = 0)
        {
            MENUITEMINFO info = new()
            {
                cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(),
                fMask = MIIM_FTYPE | MIIM_STATE | MIIM_ID | MIIM_DATA,
                fType = MFT_OWNERDRAW | (item.Kind == MenuPainter.Kind.Separator ? MFT_SEPARATOR : 0) | (item.Radio ? MFT_RADIOCHECK : 0),
                fState = (item.Enabled && item.Kind != MenuPainter.Kind.Label ? 0 : MFS_DISABLED) | (item.Checked ? MFS_CHECKED : 0),
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
