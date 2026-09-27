using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Halide.Scripts.UI.Theming;
using Godot;

namespace Halide.Scripts.UI.ContextMenu.Platform.MacOS;

// real NSMenus shown by a helper process (Tools/MacOS/menuhost.m), so the
// modal tracking loop pauses that process and not the app. the menu is
// sent as json, picks come back as events read on a task and handed to
// godot's thread; a pick that keeps the menu open sends the menu again
public class MacOSProcessHandler : PlatformHandler
{
    // the helper: beside the executable in a bundle, or in the project's tools for a dev run
    public static string HelperPath
    {
        get
        {
            string beside = Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? "", "editsharp-menu");
            if (File.Exists(beside)) return beside;

            string tools = ProjectSettings.GlobalizePath("res://Tools/MacOS/editsharp-menu");
            return File.Exists(tools) ? tools : null;
        }
    }

    public static bool Available => OS.GetName() == "macOS" && HelperPath is not null;

    static readonly bool Debug = OS.HasEnvironment("EDITSHARP_MENU_DEBUG");

    // stderr is unbuffered, so a trace survives the process being killed
    static void Trace(string message)
    {
        if (Debug) Console.Error.WriteLine($"menuapp: {message}");
    }

    Process process;
    StreamWriter input;
    Showing showing;
    long nextId;

    // the open menu's frame as the helper reported it: points, top-left origin, which is
    // what the mac's screen capture takes. written on the main thread, read from any
    Rect2I? openRect;

    sealed record Showing(long Id, ContextMenu Menu, Dictionary<long, MenuEntry> Entries);

    public override void HandleMenu(ContextMenu menu, Window owner, Vector2I? at)
    {
        Trace("HandleMenu");
        if (!Ensure()) { GD.PushWarning("The macOS menu helper is not available; the menu was not shown."); menu.EmitSignal(ContextMenu.SignalName.Closed); return; }

        ObjC.NSPoint point = at is Vector2I screen ? MacOSHandler.ScreenPointOf(screen) : ObjC.SendPoint(ObjC.Class("NSEvent"), ObjC.Sel("mouseLocation"));
        bool dark = (ThemeDB.GetProjectTheme() as EditSharpTheme)?.Palette?.Dark ?? false;

        // a menu still up is replaced: it closes as far as its owner knows
        if (showing is { } previous)
        {
            showing = null;
            previous.Menu.EmitSignal(ContextMenu.SignalName.Closed);
        }

        Dictionary<long, MenuEntry> entries = [];
        JsonArray items = Items(MenuModel.Flatten(menu.Elements), entries);
        showing = new Showing(++nextId, menu, entries);
        openRect = null;

        Trace("yielding");
        YieldActivation();
        Trace("sending show");
        Send(new JsonObject { ["cmd"] = "show", ["id"] = showing.Id, ["x"] = point.X, ["y"] = point.Y, ["dark"] = dark, ["items"] = items });
    }

    // macOS 14 on: an app only becomes active when the active one yields to it, so the
    // helper can take the keyboard for the menu's arrows and return. older systems skip this
    void YieldActivation()
    {
        nint app = ObjC.Send(ObjC.Class("NSApplication"), ObjC.Sel("sharedApplication"));
        if ((ObjC.Send(app, ObjC.Sel("respondsToSelector:"), ObjC.Sel("yieldActivationToApplication:")) & 0xFF) == 0) return;

        nint helper = ObjC.Send(ObjC.Class("NSRunningApplication"), ObjC.Sel("runningApplicationWithProcessIdentifier:"), (long)process.Id);
        if (helper != 0) ObjC.Send(app, ObjC.Sel("yieldActivationToApplication:"), helper);
    }

    public override Rect2I? OpenMenuRect() => openRect;

    public override void Dismiss() => Send(new JsonObject { ["cmd"] = "dismiss" });

    public override void HighlightNext() => Send(new JsonObject { ["cmd"] = "highlightNext" });

    public override void ActivateHighlighted() => Send(new JsonObject { ["cmd"] = "activate" });

    // ---- the helper process ----

    bool Ensure()
    {
        if (process is { HasExited: false }) return true;

        string path = HelperPath;
        if (path is null) return false;

        try
        {
            process = new Process
            {
                StartInfo = new ProcessStartInfo(path, OS.GetProcessId().ToString())
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true,
            };

            Trace($"starting {path}");
            process.Start();
            Trace($"started pid {process.Id}");
            input = process.StandardInput;
            input.AutoFlush = true;

            StreamReader output = process.StandardOutput;
            _ = Task.Run(async () =>
            {
                string line;
                while ((line = await output.ReadLineAsync()) is not null) Callable.From(() => OnEvent(line)).CallDeferred();
                Callable.From(OnExited).CallDeferred();
            });

            return true;
        }
        catch (Exception e)
        {
            GD.PushWarning($"Could not start the macOS menu helper: {e.Message}");
            process = null;
            return false;
        }
    }

    void Send(JsonObject command)
    {
        try { input?.WriteLine(command.ToJsonString()); }
        catch (IOException e) { GD.PushWarning($"The macOS menu helper went away: {e.Message}"); }
    }

    // main thread
    void OnEvent(string line)
    {
        Trace($"event {line}");
        JsonNode node;
        try { node = JsonNode.Parse(line); }
        catch (JsonException) { return; }

        string kind = node?["event"]?.GetValue<string>();

        // events of a showing that has since been replaced are old news
        if (showing is null || node?["id"]?.GetValue<long>() != showing.Id) return;

        if (kind == "opened")
        {
            openRect = new Rect2I(
                (int)Math.Round(node["x"].GetValue<double>()), (int)Math.Round(node["y"].GetValue<double>()),
                (int)Math.Round(node["w"].GetValue<double>()), (int)Math.Round(node["h"].GetValue<double>()));
        }
        else if (kind == "picked" && showing.Entries.TryGetValue(node["tag"]?.GetValue<long>() ?? 0, out MenuEntry entry))
        {
            openRect = null;
            ContextMenu menu = showing.Menu;
            bool hide = MenuModel.Activate(entry) ? menu.HideOnCheckableItemSelect : menu.HideOnItemSelect;

            if (hide)
            {
                showing = null;
                menu.EmitSignal(ContextMenu.SignalName.Closed);
                return;
            }

            // appkit closed the menu on the pick: shown again at the same spot, as it is now
            Dictionary<long, MenuEntry> entries = [];
            JsonArray items = Items(MenuModel.Flatten(menu.Elements), entries);
            showing = new Showing(++nextId, menu, entries);
            YieldActivation();
            Send(new JsonObject { ["cmd"] = "update", ["id"] = showing.Id, ["items"] = items });
        }
        else if (kind == "closed")
        {
            openRect = null;
            ContextMenu menu = showing.Menu;
            showing = null;
            menu.EmitSignal(ContextMenu.SignalName.Closed);

            // the press that dismissed the menu never reached the app; it's played here,
            // so a click on another button while a menu is up still presses it
            string click = node["click"]?.GetValue<string>();
            if (click is "left" or "right") ReplayClick(click == "left" ? MouseButton.Left : MouseButton.Right);
        }
    }

    static void ReplayClick(MouseButton button)
    {
        Viewport root = ((SceneTree)Engine.GetMainLoop()).Root;
        Vector2 at = root.GetMousePosition();
        MouseButtonMask mask = button == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right;

        Godot.Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = mask });
        Godot.Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = at, GlobalPosition = at });
    }

    void OnExited()
    {
        if (showing is { } open)
        {
            showing = null;
            open.Menu.EmitSignal(ContextMenu.SignalName.Closed);
        }

        process = null;
        input = null;
    }

    // ---- the menu as json ----

    static JsonArray Items(List<MenuItem> items, Dictionary<long, MenuEntry> entries)
    {
        JsonArray array = [];

        foreach (MenuItem item in items)
        {
            JsonObject o = new()
            {
                ["kind"] = item.Kind switch
                {
                    MenuItemKind.Separator => "separator",
                    MenuItemKind.Label => "label",
                    MenuItemKind.Submenu => "submenu",
                    _ => "button",
                },
                ["text"] = item.Text ?? "",
                ["bold"] = item.Bold,
                ["enabled"] = item.Enabled,
                ["checked"] = item.Checked,
            };

            if (item.Entry is not null)
            {
                long tag = entries.Count + 1;
                entries[tag] = item.Entry;
                o["tag"] = tag;
            }

            (string key, long mask) = MacOSHandler.KeyEquivalentOf(item.Hint);
            o["key"] = key;
            o["mask"] = mask;

            if (item.Icon?.GetImage() is Image icon && !icon.IsEmpty())
            {
                if (icon.IsCompressed()) icon.Decompress();
                o["icon"] = Convert.ToBase64String(icon.SavePngToBuffer());
            }

            if (item.Kind == MenuItemKind.Submenu) o["items"] = Items(item.Submenu, entries);

            array.Add(o);
        }

        return array;
    }
}
