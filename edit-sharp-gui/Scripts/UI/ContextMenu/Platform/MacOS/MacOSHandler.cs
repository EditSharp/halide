using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using EditSharpGUI.Scripts.UI.Theming;
using Godot;
using static EditSharpGUI.Scripts.UI.ContextMenu.Platform.MacOS.ObjC;

namespace EditSharpGUI.Scripts.UI.ContextMenu.Platform.MacOS;

// a real NSMenu built through the objective-c runtime: bold via attributed titles, NSImage icons,
// the system's check mark for checks and radios alike (as apple's guidelines have it), key equivalents
// from hints, and the app palette deciding light or dark
public class MacOSHandler : PlatformHandler
{
    const long NSControlStateValueOn = 1;
    const long NSEventModifierFlagShift = 1 << 17, NSEventModifierFlagControl = 1 << 18, NSEventModifierFlagOption = 1 << 19, NSEventModifierFlagCommand = 1 << 20;
    const double IconPoints = 16;

    static nint targetClass;
    static readonly ActionImp onAction = OnAction;   // kept alive for the runtime
    static long picked;
    static nint currentMenu;

    public override void HandleMenu(ContextMenu menu, Vector2? position = null)
    {
        EnsureTargetClass();
        ApplyAppearance();

        NSPoint at = position is Vector2 p ? ScreenPoint(p) : SendPoint(Class("NSEvent"), Sel("mouseLocation"));

        // appkit closes the menu on a pick, so staying open means showing it again
        while (true)
        {
            using Built built = new(menu);
            picked = 0;
            currentMenu = built.Menu;
            SendBool(built.Menu, Sel("popUpMenuPositioningItem:atLocation:inView:"), 0, at, 0);
            currentMenu = 0;

            if (picked == 0 || !built.Entries.TryGetValue(picked, out MenuEntry entry)) break;
            if (MenuModel.Activate(entry) ? menu.HideOnCheckableItemSelect : menu.HideOnItemSelect) break;
        }

        menu.EmitSignal(ContextMenu.SignalName.Closed);
    }

    // the tracking session runs on the main thread, which also drains main-thread performs
    public override void Dismiss()
    {
        nint menu = currentMenu;
        if (menu != 0) SendVoid(menu, Sel("performSelectorOnMainThread:withObject:waitUntilDone:"), Sel("cancelTracking"), 0, false);
    }

    // the menu's tracking loop reads the app's own event queue, so a posted key event moves its highlight
    // without any accessibility permission. postEvent:atStart: may be called from any thread
    public override void HighlightNext()
    {
        if (currentMenu == 0) return;
        const nuint NSEventTypeKeyDown = 10, NSEventTypeKeyUp = 11;
        const ushort DownArrowKeyCode = 125;
        nint app = Send(Class("NSApplication"), Sel("sharedApplication"));
        nint chars = NSString("");   // NSDownArrowFunctionKey
        foreach (nuint type in new[] { NSEventTypeKeyDown, NSEventTypeKeyUp })
        {
            nint e = SendKeyEvent(Class("NSEvent"), Sel("keyEventWithType:location:modifierFlags:timestamp:windowNumber:context:characters:charactersIgnoringModifiers:isARepeat:keyCode:"),
                type, new NSPoint(0, 0), 0, 0, 0, 0, chars, chars, false, DownArrowKeyCode);
            if (e != 0) SendVoid(app, Sel("postEvent:atStart:"), e, false);
        }
    }

    // godot screen pixels (top-left origin) to appkit screen points (bottom-left origin), via where the mouse is in both
    static NSPoint ScreenPoint(Vector2 viewportPosition)
    {
        Viewport root = ((SceneTree)Engine.GetMainLoop()).Root;
        int window = (int)DisplayServer.MainWindowId;
        Vector2I target = DisplayServer.WindowGetPosition(window) + (Vector2I)(root.GetScreenTransform() * viewportPosition).Round();
        Vector2I mouse = DisplayServer.MouseGetPosition();
        NSPoint mouseNs = SendPoint(Class("NSEvent"), Sel("mouseLocation"));
        double scale = Math.Max(1.0, DisplayServer.ScreenGetMaxScale());
        return new NSPoint(mouseNs.X + (target.X - mouse.X) / scale, mouseNs.Y - (target.Y - mouse.Y) / scale);
    }

    static void ApplyAppearance()
    {
        bool dark = (ThemeDB.GetProjectTheme() as EditSharpTheme)?.Palette?.Dark ?? false;
        nint app = Send(Class("NSApplication"), Sel("sharedApplication"));
        nint appearance = Send(Class("NSAppearance"), Sel("appearanceNamed:"), NSString(dark ? "NSAppearanceNameDarkAqua" : "NSAppearanceNameAqua"));
        Send(app, Sel("setAppearance:"), appearance);
    }

    // an NSObject subclass whose itemPicked: records the sender's tag
    static void EnsureTargetClass()
    {
        if (targetClass != 0) return;
        targetClass = objc_allocateClassPair(Class("NSObject"), "EditSharpMenuTarget", 0);
        class_addMethod(targetClass, Sel("itemPicked:"), Marshal.GetFunctionPointerForDelegate(onAction), "v@:@");
        objc_registerClassPair(targetClass);
    }

    static void OnAction(nint self, nint sel, nint sender) => picked = SendLong(sender, Sel("tag"));

    // the NSMenu tree plus the objects it does not own
    sealed class Built : IDisposable
    {
        public readonly nint Menu;
        public readonly Dictionary<long, MenuEntry> Entries = [];

        readonly nint target;
        readonly List<nint> owned = [];
        long nextTag = 1;

        public Built(ContextMenu menu)
        {
            target = Send(Send(targetClass, Sel("alloc")), Sel("init"));
            owned.Add(target);
            Menu = Fill(MenuModel.Flatten(menu.Elements));
        }

        public void Dispose()
        {
            Release(Menu);
            foreach (nint obj in owned) Release(obj);
        }

        nint Fill(List<MenuItem> items)
        {
            nint menu = Send(Send(Class("NSMenu"), Sel("alloc")), Sel("initWithTitle:"), NSString(""));
            Send(menu, Sel("setAutoenablesItems:"), false);

            foreach (MenuItem item in items)
            {
                if (item.Kind == MenuItemKind.Separator)
                {
                    Send(menu, Sel("addItem:"), Send(Class("NSMenuItem"), Sel("separatorItem")));
                    continue;
                }

                (string key, long mask) = KeyEquivalent(item.Hint);
                nint action = item.Entry is not null ? Sel("itemPicked:") : 0;
                nint mi = Send(Send(Class("NSMenuItem"), Sel("alloc")), Sel("initWithTitle:action:keyEquivalent:"), NSString(item.Text), action, NSString(key));

                if (item.Entry is not null)
                {
                    long tag = nextTag++;
                    Entries[tag] = item.Entry;
                    Send(mi, Sel("setTag:"), tag);
                    Send(mi, Sel("setTarget:"), target);
                }

                Send(mi, Sel("setEnabled:"), item.Enabled);
                if (item.Checked) Send(mi, Sel("setState:"), NSControlStateValueOn);
                if (mask != 0) Send(mi, Sel("setKeyEquivalentModifierMask:"), mask);
                if (item.Bold) Send(mi, Sel("setAttributedTitle:"), BoldTitle(item.Text));

                nint image = IconImage(item.Icon);
                if (image != 0)
                {
                    Send(mi, Sel("setImage:"), image);
                    owned.Add(image);
                }

                if (item.Kind == MenuItemKind.Submenu) Send(mi, Sel("setSubmenu:"), Fill(item.Submenu));

                Send(menu, Sel("addItem:"), mi);
                owned.Add(mi);
            }

            return menu;
        }

        // the title in the menu font's bold face
        nint BoldTitle(string text)
        {
            double size = SendDouble(Class("NSFont"), Sel("systemFontSize"));
            nint font = Send(Class("NSFont"), Sel("boldSystemFontOfSize:"), size);
            nint attributes = Send(Class("NSDictionary"), Sel("dictionaryWithObject:forKey:"), font, NSString("NSFont"));
            nint title = Send(Send(Class("NSAttributedString"), Sel("alloc")), Sel("initWithString:attributes:"), NSString(text), attributes);
            owned.Add(title);
            return title;
        }

        // an NSImage of the texture at menu icon size
        static nint IconImage(Texture2D texture)
        {
            Image source = texture?.GetImage();
            if (source is null || source.IsEmpty()) return 0;
            if (source.IsCompressed()) source.Decompress();

            nint data = NSData(source.SavePngToBuffer());
            nint image = Send(Send(Class("NSImage"), Sel("alloc")), Sel("initWithData:"), data);
            Release(data);
            if (image == 0) return 0;

            Send(image, Sel("setSize:"), new NSSize(IconPoints, IconPoints));
            return image;
        }

        // "Ctrl+Shift+X" as a key equivalent: ctrl becomes command, the mac's primary modifier
        static (string key, long mask) KeyEquivalent(string hint)
        {
            if (!MenuModel.TryParseHint(hint, out Key key, out KeyModifierMask modifiers)) return ("", 0);

            string equivalent = key switch
            {
                >= Key.A and <= Key.Z => ((char)('a' + (key - Key.A))).ToString(),
                >= Key.Key0 and <= Key.Key9 => ((char)('0' + (key - Key.Key0))).ToString(),
                Key.Space => " ",
                Key.Enter => "\r",
                Key.Escape => "\u001b",
                Key.Tab => "\t",
                Key.Delete => "\u007f",
                Key.Backspace => "\b",
                Key.Left => "",
                Key.Right => "",
                Key.Up => "",
                Key.Down => "",
                >= Key.F1 and <= Key.F12 => ((char)(0xF704 + (key - Key.F1))).ToString(),
                _ => OS.GetKeycodeString(key) is { Length: 1 } single ? single.ToLowerInvariant() : "",
            };
            if (equivalent.Length == 0) return ("", 0);

            long mask = 0;
            if (modifiers.HasFlag(KeyModifierMask.MaskCtrl) || modifiers.HasFlag(KeyModifierMask.MaskMeta)) mask |= NSEventModifierFlagCommand;
            if (modifiers.HasFlag(KeyModifierMask.MaskShift)) mask |= NSEventModifierFlagShift;
            if (modifiers.HasFlag(KeyModifierMask.MaskAlt)) mask |= NSEventModifierFlagOption;
            return (equivalent, mask);
        }
    }
}
