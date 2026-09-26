using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using static EditSharpGUI.Scripts.UI.ContextMenu.Platform.Windows.Win32;

namespace EditSharpGUI.Scripts.UI.ContextMenu.Platform.Windows;

// the thread win32 menus run on. a popup menu runs a modal message loop on
// the thread that tracks it, so tracking on godot's thread would stop the
// frame loop for as long as the menu is up. here a thread of its own owns
// a hidden window, tracks every menu against it, draws the items itself,
// and hands each pick back to godot as a deferred call; godot keeps
// rendering, and a pick that keeps the menu open shows again once godot
// has applied it
static class MenuThread
{
    // what one showing needs from the main thread, gathered there
    public sealed record Showing(ContextMenu Menu, Snapshot Snapshot, Vector2I At, bool Dark, uint Dpi, nint MainWindow, bool HideOnItem, bool HideOnCheckable);

    static readonly BlockingCollection<Showing> requests = [];
    static Thread thread;
    static nint helper;
    static uint threadId;
    static MenuPainter painter;
    static WndProc wndProc;

    public static nint Helper => helper;

    public static void Show(Showing showing)
    {
        if (thread is null)
        {
            thread = new Thread(Run) { IsBackground = true, Name = "EditSharp-Menus" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        requests.Add(showing);
    }

    static void Run()
    {
        threadId = GetCurrentThreadId();
        CreateHelperWindow();

        foreach (Showing showing in requests.GetConsumingEnumerable())
        {
            try
            {
                Track(showing);
            }
            catch (Exception e)
            {
                GD.PushError($"Context menu failed: {e}");
                Closed(showing.Menu);
            }
        }
    }

    static void CreateHelperWindow()
    {
        wndProc = Handle;

        WNDCLASSEXW cls = new()
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc),
            hInstance = GetModuleHandleW(null),
            lpszClassName = "EditSharpMenuOwner",
        };
        RegisterClassExW(ref cls);

        helper = CreateWindowExW(WS_EX_TOOLWINDOW, cls.lpszClassName, "", WS_POPUP, 0, 0, 0, 0, 0, 0, cls.hInstance, 0);
    }

    static void Track(Showing showing)
    {
        uint mainThread = GetWindowThreadProcessId(showing.MainWindow, 0);
        Snapshot snapshot = showing.Snapshot;

        using MenuPainter menuPainter = new(helper, showing.Dpi, showing.Dark);
        painter = menuPainter;

        try
        {
            while (true)
            {
                MenuEntry entry;

                using (Built built = new(snapshot, menuPainter))
                {
                    // the tracking thread's window has to be in front for the menu to
                    // close on a click elsewhere; sharing input with godot's thread is
                    // what lets a background thread take the foreground
                    AttachThreadInput(threadId, mainThread, true);
                    SetForegroundWindow(helper);
                    uint id = TrackPopupMenuEx(built.Menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, showing.At.X, showing.At.Y, helper, 0);
                    SetForegroundWindow(showing.MainWindow);
                    AttachThreadInput(threadId, mainThread, false);

                    if (id == 0 || !built.Entries.TryGetValue(id, out entry))
                    {
                        Closed(showing.Menu);
                        return;
                    }
                }

                snapshot.Dispose();

                // the pick lands on godot's thread; when the menu stays open, a fresh
                // snapshot comes back once the pick has been applied
                TaskCompletionSource<Snapshot> next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                ContextMenu menu = showing.Menu;
                MenuEntry picked = entry;

                Callable.From(() =>
                {
                    try
                    {
                        bool hide = MenuModel.Activate(picked) ? showing.HideOnCheckable : showing.HideOnItem;
                        if (hide)
                        {
                            menu.EmitSignal(ContextMenu.SignalName.Closed);
                            next.SetResult(null);
                        }
                        else next.SetResult(Snapshot.Of(menu));
                    }
                    catch (Exception e)
                    {
                        next.SetException(e);
                    }
                }).CallDeferred();

                snapshot = next.Task.GetAwaiter().GetResult();
                if (snapshot is null) return;
            }
        }
        finally
        {
            painter = null;
            snapshot?.Dispose();
        }
    }

    static void Closed(ContextMenu menu) => Callable.From(() => menu.EmitSignal(ContextMenu.SignalName.Closed)).CallDeferred();

    // ---- the helper window: the owner the popup sends its drawing messages to ----

    static readonly HashSet<nint> styled = [];

    // a popup window exists from its WM_INITMENUPOPUP on, before it shows. on windows 11 dwm rounds its
    // corners, colours its border and casts its shadow, the parts of the frame the painter cannot reach.
    // that has to happen before the popup shows: once shown, the classic drop shadow (a tight dark rim)
    // is already up and can outlive the switch, so the class loses it for good
    static void StylePopups()
    {
        if (painter is null || System.Environment.OSVersion.Version.Build < 22000) return;

        for (nint popup = FindWindowExW(0, 0, "#32768", null); popup != 0; popup = FindWindowExW(0, popup, "#32768", null))
        {
            if (GetWindowThreadProcessId(popup, 0) != threadId || !styled.Add(popup)) continue;

            nuint style = GetClassLongPtrW(popup, GCL_STYLE);
            if ((style & CS_DROPSHADOW) != 0) SetClassLongPtrW(popup, GCL_STYLE, style & ~(nuint)CS_DROPSHADOW);

            uint corner = DWMWCP_ROUNDSMALL;
            uint border = painter.BorderColor;
            DwmSetWindowAttribute(popup, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(uint));
            DwmSetWindowAttribute(popup, DWMWA_BORDER_COLOR, ref border, sizeof(uint));
        }
    }

    static nint Handle(nint window, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (msg == WM_INITMENUPOPUP)
            {
                StylePopups();
            }
            else if (msg == WM_MEASUREITEM && painter is not null)
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
            else if (msg == WM_DRAWITEM && painter is not null)
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

        return DefWindowProcW(window, msg, wParam, lParam);
    }

    // ---- win32 the thread needs beyond the handler's ----

    const uint WS_POPUP = 0x80000000;
    const uint WS_EX_TOOLWINDOW = 0x80;

    delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public nint hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassExW(ref WNDCLASSEXW cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint attach, uint attachTo, bool attaching);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern nint GetModuleHandleW(string module);
}

// a menu as pure win32 can build it: the rows flattened on godot's thread,
// with every icon already turned into a bitmap there, since only that
// thread may read a texture
sealed class Snapshot : IDisposable
{
    public List<MenuItem> Items { get; }
    readonly Dictionary<Texture2D, nint> icons;

    Snapshot(List<MenuItem> items, Dictionary<Texture2D, nint> icons)
    {
        Items = items;
        this.icons = icons;
    }

    public nint IconOf(Texture2D texture) => texture is not null && icons.TryGetValue(texture, out nint bitmap) ? bitmap : 0;

    // main thread only
    public static Snapshot Of(ContextMenu menu)
    {
        List<MenuItem> items = MenuModel.Flatten(menu.Elements);
        Dictionary<Texture2D, nint> icons = [];
        int size = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForWindow((nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, (int)DisplayServer.MainWindowId)));

        void Collect(List<MenuItem> list)
        {
            foreach (MenuItem item in list)
            {
                if (item.Icon is not null && !icons.ContainsKey(item.Icon))
                {
                    nint bitmap = Built.IconBitmap(item.Icon, size);
                    if (bitmap != 0) icons[item.Icon] = bitmap;
                }

                if (item.Submenu is not null) Collect(item.Submenu);
            }
        }

        Collect(items);
        return new Snapshot(items, icons);
    }

    public void Dispose()
    {
        foreach (nint bitmap in icons.Values) DeleteObject(bitmap);
        icons.Clear();
    }
}
