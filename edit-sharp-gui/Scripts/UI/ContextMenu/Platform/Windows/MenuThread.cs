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
// has applied it. native dialogs live here too (see WindowsDialog): the
// thread runs a message loop, and work is posted to it
static class MenuThread
{
    // what one showing needs from the main thread, gathered there
    public sealed record Showing(ContextMenu Menu, Snapshot Snapshot, Vector2I At, bool Dark, uint Dpi, nint MainWindow, bool HideOnItem, bool HideOnCheckable, bool Fade)
    {
        // a menu bar's menus, when this is one of them: the pointer moving onto another's button opens that one
        public IReadOnlyList<BarItem> Bar { get; init; }
        public int BarIndex { get; init; }
    }

    // one of a menu bar's menus: its latest snapshot, where it opens and its button, in win32 screen pixels
    public sealed class BarItem(ContextMenu menu, Snapshot snapshot, Vector2I at, Rect2I button)
    {
        public ContextMenu Menu => menu;
        public Snapshot Snapshot { get; set; } = snapshot;
        public Vector2I At => at;
        public Rect2I Button => button;
    }

    // set by the filter: the bar menu to open once this one has closed, or that its own button closed it
    static int barSwitch = -1;
    static bool barClosed;

    // work for the thread, run from its message loop in order. a menu tracks
    // modally, so work that arrives meanwhile waits until the menu closes
    static readonly ConcurrentQueue<Action> work = new();
    static bool working;
    const uint WM_APP_WORK = 0x8000 + 1;

    static Thread thread;
    static readonly object startLock = new();
    static volatile nint helper;
    static uint threadId;
    static MenuPainter painter;
    static WndProc wndProc;

    public static nint Helper => helper;

    static readonly bool Debug = OS.HasEnvironment("EDITSHARP_MENU_DEBUG");

    public static void Show(Showing showing) => Post(() =>
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
    });

    // runs an action on this thread, after everything posted before it
    public static void Post(Action action)
    {
        lock (startLock)
        {
            if (thread is null)
            {
                thread = new Thread(Run) { IsBackground = true, Name = "EditSharp-Menus" };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }
        }

        work.Enqueue(action);
        if (helper != 0) PostMessageW(helper, WM_APP_WORK, 0, 0);
    }

    // a window's messages first go through here, so a dialog gets tab, enter and escape
    public static Func<MSG, bool> PreTranslate;

    static void Run()
    {
        threadId = GetCurrentThreadId();
        CreateHelperWindow();

        // whatever was posted before the window existed
        PostMessageW(helper, WM_APP_WORK, 0, 0);

        while (GetMessageW(out MSG msg, 0, 0, 0) > 0)
        {
            if (PreTranslate?.Invoke(msg) == true) continue;
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    static void RunWork()
    {
        // a menu tracking now got here through its own message loop: it goes on, and so does this afterwards
        if (working) return;
        working = true;

        try
        {
            while (work.TryDequeue(out Action action))
            {
                try { action(); }
                catch (Exception e) { GD.PushError($"Menu thread work failed: {e}"); }
            }
        }
        finally
        {
            working = false;
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

    // what the message filter needs while a menu is up
    static Showing current;
    static Built currentBuilt;
    static Snapshot currentSnapshot;
    static (nint Menu, int Index) highlighted = (0, -1);

    static void Track(Showing showing)
    {
        uint mainThread = GetWindowThreadProcessId(showing.MainWindow, 0);
        Snapshot snapshot = showing.Snapshot;

        using MenuPainter menuPainter = new(helper, showing.Dpi, showing.Dark);
        painter = menuPainter;
        current = showing;
        barSwitch = -1;
        barClosed = false;

        try
        {
            while (true)
            {
                MenuEntry entry;

                using (Built built = new(snapshot, menuPainter))
                {
                    currentBuilt = built;
                    currentSnapshot = snapshot;
                    highlighted = (0, -1);
                    filterHook ??= Filter;
                    nint filter = SetWindowsHookExW(WH_MSGFILTER, filterHook, 0, threadId);

                    // the tracking thread's window has to be in front for the menu to
                    // close on a click elsewhere; sharing input with godot's thread is
                    // what lets a background thread take the foreground
                    // nothing left over from before may reach the menu loop, or it closes at once
                    Drain(showing.MainWindow, forwardReleases: false, out _);

                    // a low-level mouse hook, alive for the menu's lifetime, remembers the
                    // press that dismisses it, since the menu loop swallows that press
                    pressSeen = 0;
                    mouseHook ??= Hook;
                    nint hook = SetWindowsHookExW(WH_MOUSE_LL, mouseHook, GetModuleHandleW(null), 0);

                    // a menu with no window showing (the tray's) only borrows the foreground in TakeForeground:
                    // joined to a hidden window's thread, its input waits behind that window's forever
                    bool attached = IsWindowVisible(showing.MainWindow);
                    if (attached) AttachThreadInput(threadId, mainThread, true);
                    TakeForeground(mainThread);
                    long opened = System.Environment.TickCount64;
                    uint flags = TPM_RETURNCMD | TPM_RIGHTBUTTON | (showing.Fade ? 0 : TPM_NOANIMATION);
                    uint id = TrackPopupMenuEx(built.Menu, flags, showing.At.X, showing.At.Y, helper, 0);
                    if (Debug) GD.Print($"MENU track returned id={id} after {System.Environment.TickCount64 - opened}ms error={Marshal.GetLastWin32Error()} at={showing.At} pressSeen={pressSeen}");

                    // the menu loop has to see one more message to wind down fully (TrackPopupMenu docs)
                    PostMessageW(helper, WM_NULL, 0, 0);

                    // a hidden main window (the tray's) is never handed the foreground
                    if (attached)
                    {
                        SetForegroundWindow(showing.MainWindow);
                        AttachThreadInput(threadId, mainThread, false);
                    }
                    if (hook != 0) UnhookWindowsHookEx(hook);
                    if (filter != 0) UnhookWindowsHookEx(filter);
                    currentBuilt = null;

                    // the pointer went onto another bar menu: it opens in this same loop, without a trip through godot
                    if (id == 0 && barSwitch >= 0 && showing.Bar is { } bar)
                    {
                        BarItem item = bar[barSwitch];
                        showing = showing with
                        {
                            Menu = item.Menu, Snapshot = item.Snapshot, At = item.At, BarIndex = barSwitch,
                            HideOnItem = item.Menu.HideOnItemSelect, HideOnCheckable = item.Menu.HideOnCheckableItemSelect,
                        };
                        current = showing;
                        snapshot = item.Snapshot;
                        barSwitch = -1;
                        continue;
                    }

                    // its own button was clicked: that click only closes it, as a menu bar's does
                    if (id == 0 && barClosed)
                    {
                        Drain(showing.MainWindow, forwardReleases: false, out _);
                        Closed(showing.Menu);
                        return;
                    }

                    // a pick handled inside the loop that could not update the menu in
                    // place closed it; the snapshot it left is built again
                    if (id == 0 && rebuildWith is not null)
                    {
                        snapshot = rebuildWith;
                        rebuildWith = null;
                        Keep(showing, snapshot);
                        continue;
                    }

                    if (id == 0 || !built.Entries.TryGetValue(id, out entry))
                    {
                        ReplayDismissingClick(showing.MainWindow);
                        Closed(showing.Menu);
                        return;
                    }
                }

                if (showing.Bar is null) snapshot.Dispose();

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
                Keep(showing, snapshot);
            }
        }
        finally
        {
            painter = null;
            current = null;
            currentSnapshot = null;
            snapshot?.Dispose();
            if (showing.Bar is { } bar) foreach (BarItem item in bar) item.Snapshot.Dispose();
        }
    }

    // a bar menu's newest snapshot, so switching back to it later shows it as it is now
    static void Keep(Showing showing, Snapshot snapshot)
    {
        if (showing.Bar is { } bar) bar[showing.BarIndex].Snapshot = snapshot;
    }

    // the bar menu whose button holds the point; -1 for none
    static int BarAt(Showing showing, POINT pt)
    {
        if (showing.Bar is not { } bar) return -1;
        for (int i = 0; i < bar.Count; i++)
            if (bar[i].Button.HasPoint(new Vector2I(pt.x, pt.y))) return i;
        return -1;
    }

    // ---- picks that keep the menu open, handled inside the menu loop ----

    static HookProc filterHook;
    static Snapshot rebuildWith;

    // the row a message names: the one under the cursor for a mouse message
    // (by position), the highlighted one for a key (by identifier, which is
    // how WM_MENUSELECT names a plain item)
    static bool RowOf(in MSG msg, out nint menu, out uint item, out bool byPosition)
    {
        menu = 0;
        item = 0;
        byPosition = true;

        if (msg.message is WM_LBUTTONUP or WM_RBUTTONUP or WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_LBUTTONDBLCLK or WM_RBUTTONDBLCLK)
        {
            nint popup = WindowFromPoint(msg.pt);
            if (popup == 0 || GetWindowThreadProcessId(popup, 0) != threadId) return false;

            menu = SendMessageW(popup, MN_GETHMENU, 0, 0);
            if (menu == 0) return false;

            int index = MenuItemFromPoint(0, menu, msg.pt);
            item = (uint)index;
            return index >= 0;
        }

        if (msg.message == WM_KEYDOWN && ((int)msg.wParam is VK_RETURN or VK_SPACE) && highlighted.Menu != 0)
        {
            (menu, int id) = highlighted;
            item = (uint)id;
            byPosition = false;
            return id > 0;
        }

        return false;
    }

    // the message filter runs for every message the menu loop takes. a pick
    // on a row that keeps the menu open is applied here and swallowed, so
    // the popup and any submenu stay up; everything else passes
    static nint Filter(int code, nint wParam, nint lParam)
    {
        if (code == MSGF_MENU && currentBuilt is Built built && current is Showing showing)
        {
            MSG msg = Marshal.PtrToStructure<MSG>(lParam);

            // bar switching/closing is handled from the low-level mouse hook, not here (see Hook):
            // calling EndMenu() from this filter, while AttachThreadInput is active and a WH_MOUSE_LL
            // hook is also firing for every system mouse move, could hang the whole app

            if (RowOf(msg, out nint menu, out uint item, out bool byPosition))
            {
                MENUITEMINFO info = new() { cbSize = (uint)Marshal.SizeOf<MENUITEMINFO>(), fMask = MIIM_ID | MIIM_STATE | MIIM_SUBMENU };

                if (GetMenuItemInfoW(menu, item, byPosition, ref info) && info.hSubMenu == 0 && (info.fState & MFS_DISABLED) == 0
                    && built.Entries.TryGetValue(info.wID, out MenuEntry entry) && StaysOpen(entry, showing))
                {
                    // the press never reaches the menu: once it has seen a button go down it picks the
                    // row itself when the button comes up, whatever happens to the release message
                    if (msg.message is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_LBUTTONDBLCLK or WM_RBUTTONDBLCLK) return 1;

                    Apply(entry, showing, built);
                    return 1;
                }
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    // the helper to the front; when another app has it (a tray click leaves the
    // taskbar there) its thread is joined for the moment it takes to switch
    static void TakeForeground(uint mainThread)
    {
        nint front = GetForegroundWindow();
        uint frontThread = front != 0 ? GetWindowThreadProcessId(front, 0) : 0;
        bool borrow = frontThread != 0 && frontThread != mainThread && frontThread != threadId;

        if (borrow) AttachThreadInput(threadId, frontThread, true);
        bool taken = SetForegroundWindow(helper);
        if (borrow) AttachThreadInput(threadId, frontThread, false);

        if (Debug) GD.Print($"MENU foreground taken={taken} borrowed={borrow} now={GetForegroundWindow() == helper}");
    }

    static bool StaysOpen(MenuEntry entry, Showing showing)
    {
        bool checkable = entry.Owner is not null || entry.Button.Type != ContextButton.CheckType.None;
        return !(checkable ? showing.HideOnCheckable : showing.HideOnItem);
    }

    // the pick lands on godot's thread; the fresh snapshot it returns
    // restyles the rows in place and the popups repaint
    static void Apply(MenuEntry entry, Showing showing, Built built)
    {
        TaskCompletionSource<Snapshot> next = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ContextMenu menu = showing.Menu;

        Callable.From(() =>
        {
            try
            {
                MenuModel.Activate(entry);
                next.SetResult(Snapshot.Of(menu));
            }
            catch (Exception e)
            {
                next.SetException(e);
            }
        }).CallDeferred();

        Snapshot snapshot;
        try { snapshot = next.Task.GetAwaiter().GetResult(); }
        catch (Exception e) { GD.PushError($"Context menu pick failed: {e}"); return; }

        if (built.Update(snapshot))
        {
            currentSnapshot?.Dispose();
            currentSnapshot = snapshot;
            RepaintPopups();
            return;
        }

        // the rows changed shape: the menu closes and comes back built from the new snapshot
        rebuildWith = snapshot;
        PostMessageW(helper, WM_CANCELMODE, 0, 0);
    }

    static void RepaintPopups()
    {
        for (nint popup = FindWindowExW(0, 0, "#32768", null); popup != 0; popup = FindWindowExW(0, popup, "#32768", null))
        {
            if (GetWindowThreadProcessId(popup, 0) != threadId || !IsWindowVisible(popup)) continue;
            // no erase: the items paint every pixel of themselves, and blanking first is a visible flash
            RedrawWindow(popup, 0, 0, RDW_INVALIDATE | RDW_UPDATENOW | RDW_ALLCHILDREN);
        }
    }

    static void Closed(ContextMenu menu) => Callable.From(() => menu.EmitSignal(ContextMenu.SignalName.Closed)).CallDeferred();

    // the click that dismisses a menu is swallowed by the menu loop, so a
    // click on another button while a menu is up would only close the menu.
    // when the menu closed under a mouse button that is still down over the
    // window, the press is handed to the window, and the release that
    // follows makes it a click
    // the press the hook saw while the menu was up, if any, and where
    static uint pressSeen;
    static POINT pressPoint;
    static LowLevelMouseProc mouseHook;

    static nint Hook(int code, nint wParam, nint lParam)
    {
        if (code < 0) return CallNextHookEx(0, code, wParam, lParam);

        uint msg = (uint)wParam;

        if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN)
        {
            POINT pt = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam).pt;

            // a click on one of the bar's own buttons: handled entirely here (switch or close), by
            // posting the same cancel the menu loop already listens for. never treated as a plain
            // dismissing click, so it can't reopen the menu through a replay
            if (msg == WM_LBUTTONDOWN && current is Showing showing && BarAt(showing, pt) is int over and >= 0)
            {
                if (over == showing.BarIndex) barClosed = true;
                else barSwitch = over;
                PostMessageW(helper, WM_CANCELMODE, 0, 0);
                return 1;
            }

            pressSeen = msg;
            pressPoint = pt;
        }
        else if (msg == WM_MOUSEMOVE && current is Showing showing && showing.Bar is not null)
        {
            POINT pt = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam).pt;
            int over = BarAt(showing, pt);

            // asked for at most once per target: posting the cancel again while it's still winding down would
            // just queue up redundant switches
            if (over >= 0 && over != showing.BarIndex && barSwitch != over)
            {
                barSwitch = over;
                PostMessageW(helper, WM_CANCELMODE, 0, 0);
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    static void ReplayDismissingClick(nint window)
    {
        // a release that reached this thread's queue instead of the window is dropped here;
        // the window gets a whole click below
        Drain(window, forwardReleases: false, out _);

        if (pressSeen == 0) return;
        if (WindowFromPoint(pressPoint) != window) return;

        bool left = pressSeen == WM_LBUTTONDOWN;
        bool stillDown = (GetAsyncKeyState(left ? VK_LBUTTON : VK_RBUTTON) & 0x8000) != 0;

        POINT client = pressPoint;
        ScreenToClient(window, ref client);
        nint at = (nint)((client.y << 16) | (client.x & 0xFFFF));

        if (Debug) GD.Print($"MENU replaying {(left ? "left" : "right")} click at {client.x},{client.y} stillDown={stillDown}");

        PostMessageW(window, left ? WM_LBUTTONDOWN : WM_RBUTTONDOWN, left ? MK_LBUTTON : MK_RBUTTON, at);
        if (!stillDown) PostMessageW(window, left ? WM_LBUTTONUP : WM_RBUTTONUP, 0, at);
    }

    // empties this thread's message queue: stale mouse, key and cancel
    // messages are dropped (a button release is reported, or forwarded to
    // the window when asked), everything else is dispatched as usual
    static void Drain(nint window, bool forwardReleases, out uint release)
    {
        release = 0;

        while (PeekMessageW(out MSG m, 0, 0, 0, PM_REMOVE))
        {
            if (m.message is WM_LBUTTONUP or WM_RBUTTONUP)
            {
                release = m.message;
                if (forwardReleases)
                {
                    POINT client = m.pt;
                    ScreenToClient(window, ref client);
                    PostMessageW(window, m.message, 0, (nint)((client.y << 16) | (client.x & 0xFFFF)));
                }
                continue;
            }

            if (m.message is >= WM_MOUSEFIRST and <= WM_MOUSELAST) continue;
            if (m.message is >= WM_KEYFIRST and <= WM_KEYLAST) continue;
            if (m.message == WM_CANCELMODE) continue;

            TranslateMessage(ref m);
            DispatchMessageW(ref m);
        }
    }

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
            uint noTransitions = 1;
            DwmSetWindowAttribute(popup, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(uint));
            DwmSetWindowAttribute(popup, DWMWA_BORDER_COLOR, ref border, sizeof(uint));
            // stops dwm's own fade for this window's shadow, on top of TPM_NOANIMATION, so
            // switching bar menus (destroy one popup, create the next) doesn't flash the shadow
            DwmSetWindowAttribute(popup, DWMWA_TRANSITIONS_FORCEDISABLED, ref noTransitions, sizeof(uint));
        }
    }

    static nint Handle(nint window, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (msg == WM_APP_WORK)
            {
                RunWork();
                return 0;
            }

            if (msg == WM_MENUSELECT)
            {
                uint flags = (uint)((long)wParam >> 16) & 0xFFFF;
                int item = (int)((long)wParam & 0xFFFF);
                highlighted = (flags & MF_POPUP) != 0 || lParam == 0 ? (0, -1) : (lParam, item);
            }
            else if (msg == WM_INITMENUPOPUP)
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

    const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02;
    const uint WM_MOUSEMOVE = 0x200;
    const uint WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_LBUTTONDBLCLK = 0x203, WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205, WM_RBUTTONDBLCLK = 0x206;
    const uint WM_MOUSEFIRST = 0x200, WM_MOUSELAST = 0x20D, WM_KEYFIRST = 0x100, WM_KEYLAST = 0x109;
    const uint PM_REMOVE = 0x1;
    const nint MK_LBUTTON = 0x1, MK_RBUTTON = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    const int WH_MOUSE_LL = 14, WH_MSGFILTER = -1, MSGF_MENU = 2;
    const uint WM_MENUSELECT = 0x11F, MN_GETHMENU = 0x1E1, MF_POPUP = 0x10;
    const int VK_RETURN = 0x0D, VK_SPACE = 0x20;
    const uint RDW_INVALIDATE = 0x1, RDW_ERASE = 0x4, RDW_ALLCHILDREN = 0x80, RDW_UPDATENOW = 0x100;

    delegate nint HookProc(int code, nint wParam, nint lParam);

    [DllImport("user32.dll")] static extern nint SetWindowsHookExW(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] static extern nint SendMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] static extern int MenuItemFromPoint(nint hwnd, nint menu, POINT point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMenuItemInfoW(nint menu, uint item, bool byPosition, ref MENUITEMINFO info);
    [DllImport("user32.dll")] static extern bool RedrawWindow(nint hwnd, nint rect, nint region, uint flags);

    delegate nint LowLevelMouseProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [DllImport("user32.dll")] static extern nint SetWindowsHookExW(int id, LowLevelMouseProc proc, nint module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] static extern bool PeekMessageW(out MSG msg, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern int GetMessageW(out MSG msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern nint DispatchMessageW(ref MSG msg);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    const uint WM_NULL = 0x0000;
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] static extern nint WindowFromPoint(POINT point);
    [DllImport("user32.dll")] static extern bool ScreenToClient(nint window, ref POINT point);
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
