using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EditSharpGUI.Scripts.Input;

// the names of the shortcuts the app knows about. the map binds keys to
// these; nothing binds to a key directly
public static class Shortcuts
{
    public const string PlaybackToggle = "playback.toggle";
    public const string PlaybackForward = "playback.forward";
    public const string PlaybackReverse = "playback.reverse";
    public const string PlaybackStop = "playback.stop";
    public const string StepForward = "playback.stepForward";
    public const string StepBack = "playback.stepBack";
    public const string SelectAll = "selection.all";
    public const string Undo = "history.undo";
    public const string Redo = "history.redo";
    public const string Cut = "clips.cut";
    public const string Copy = "clips.copy";
    public const string Paste = "clips.paste";
    public const string Delete = "clips.delete";
    public const string RippleDelete = "clips.rippleDelete";
    public const string RippleDeleteAll = "clips.rippleDeleteAll";
    public const string Split = "clips.split";
    public const string SplitAll = "clips.splitAll";
    public const string MediaRename = "media.rename";
    public const string MediaImport = "media.import";
    public const string Save = "project.save";
    public const string SaveAs = "project.saveAs";
    public const string ShowHome = "app.home";
    public const string ShowSettings = "app.settings";
    public const string NewProject = "project.new";
    public const string OpenProject = "project.open";
    public const string CloseProject = "project.close";
    public const string Quit = "app.quit";
    public const string Duplicate = "clips.duplicate";
    public const string Deselect = "selection.none";
    public const string GoToStart = "playback.start";
    public const string GoToEnd = "playback.end";
    public const string Loop = "playback.loop";
    public const string ZoomIn = "timeline.zoomIn";
    public const string ZoomOut = "timeline.zoomOut";
    public const string ZoomFit = "timeline.zoomFit";
    public const string Fullscreen = "window.fullscreen";
    public const string FloatFocused = "layout.floatFocused";
    public const string ResetLayout = "layout.reset";

    // the first nine layouts in the switcher, by position
    public static string Layout(int number) => $"layout.{number}";

    // actions extensions add, listed after the app's own
    public static readonly List<(string Action, string Category, string Label)> Extra = [];

    // every action the settings list: the app's, then extensions'
    public static IEnumerable<(string Action, string Category, string Label)> All => Catalog.Concat(Extra);

    // every action as the settings list it: (action, category, label), in order
    public static readonly (string Action, string Category, string Label)[] Catalog =
    [
        (PlaybackToggle, "Playback", "Play / pause"),
        (PlaybackForward, "Playback", "Play forward, faster each press"),
        (PlaybackReverse, "Playback", "Play backward, faster each press"),
        (PlaybackStop, "Playback", "Stop"),
        (StepForward, "Playback", "Next frame"),
        (StepBack, "Playback", "Previous frame"),
        (GoToStart, "Playback", "Go to start"),
        (GoToEnd, "Playback", "Go to end"),
        (Loop, "Playback", "Loop playback"),
        (Undo, "Edit", "Undo"),
        (Redo, "Edit", "Redo"),
        (Cut, "Edit", "Cut"),
        (Copy, "Edit", "Copy"),
        (Paste, "Edit", "Paste"),
        (Duplicate, "Edit", "Duplicate"),
        (SelectAll, "Edit", "Select all"),
        (Deselect, "Edit", "Deselect all"),
        (Delete, "Clips", "Delete"),
        (RippleDelete, "Clips", "Ripple delete"),
        (RippleDeleteAll, "Clips", "Ripple delete on every channel"),
        (Split, "Clips", "Split at the playhead"),
        (SplitAll, "Clips", "Split every channel at the playhead"),
        (MediaRename, "Media", "Rename"),
        (MediaImport, "Media", "Import files"),
        (NewProject, "Project", "New project"),
        (OpenProject, "Project", "Open project"),
        (Save, "Project", "Save"),
        (SaveAs, "Project", "Save as"),
        (CloseProject, "Project", "Close project"),
        (ZoomIn, "View", "Zoom timeline in"),
        (ZoomOut, "View", "Zoom timeline out"),
        (ZoomFit, "View", "Fit timeline to view"),
        (Fullscreen, "View", "Toggle fullscreen"),
        (FloatFocused, "View", "Float the focused view"),
        (ResetLayout, "View", "Reset layout"),
        (Layout(1), "View", "Layout 1"),
        (Layout(2), "View", "Layout 2"),
        (Layout(3), "View", "Layout 3"),
        (Layout(4), "View", "Layout 4"),
        (Layout(5), "View", "Layout 5"),
        (Layout(6), "View", "Layout 6"),
        (Layout(7), "View", "Layout 7"),
        (Layout(8), "View", "Layout 8"),
        (Layout(9), "View", "Layout 9"),
        (ShowHome, "App", "Show Home"),
        (ShowSettings, "App", "App Settings"),
        (Quit, "App", "Quit"),
    ];
}

// one key plus the modifiers that have to be held with it. Control folds cmd
// on mac into the same flag as ctrl elsewhere, the same way Modifiers does
public readonly record struct KeyCombo(Key Key, bool Control = false, bool Shift = false, bool Alt = false)
{
    public static KeyCombo From(InputEventKey key)
        => new(key.Keycode, key.IsCommandOrControlPressed(), key.ShiftPressed, key.AltPressed);

    // "Ctrl+Shift+A" - what the shortcut file holds
    public override string ToString()
    {
        List<string> parts = [];

        if (Control) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        parts.Add(Key.ToString());

        return string.Join("+", parts);
    }

    public static bool TryParse(string text, out KeyCombo combo)
    {
        combo = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        bool control = false, shift = false, alt = false;
        Key? key = null;

        foreach (string raw in text.Split('+'))
        {
            string part = raw.Trim();

            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                case "cmd":
                    control = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                default:
                    if (!Enum.TryParse(part, ignoreCase: true, out Key parsed)) return false;
                    key = parsed;
                    break;
            }
        }

        if (key is null) return false;

        combo = new(key.Value, control, shift, alt);
        return true;
    }
}

// which key combos trigger which shortcut. an action may have more than one
// - redo answers to Ctrl+Y and Ctrl+Shift+Z alike. the defaults live here;
// the user's own choices live in a json file next to the other user data,
// written out on first run so there is something to edit
public sealed class ShortcutMap
{
    public const string DefaultPath = "user://shortcuts.json";

    // where Load and Save go when not told; tests point it elsewhere
    public static string FilePath { get; set; } = DefaultPath;

    static readonly Dictionary<string, KeyCombo[]> Defaults = new()
    {
        [Shortcuts.PlaybackToggle] = [new(Key.Space)],
        [Shortcuts.PlaybackForward] = [new(Key.L)],
        [Shortcuts.PlaybackReverse] = [new(Key.J)],
        [Shortcuts.PlaybackStop] = [new(Key.K)],
        [Shortcuts.StepForward] = [new(Key.Right)],
        [Shortcuts.StepBack] = [new(Key.Left)],
        [Shortcuts.SelectAll] = [new(Key.A, Control: true)],
        [Shortcuts.Undo] = [new(Key.Z, Control: true)],
        [Shortcuts.Redo] = [new(Key.Y, Control: true), new(Key.Z, Control: true, Shift: true)],
        [Shortcuts.Cut] = [new(Key.X, Control: true)],
        [Shortcuts.Copy] = [new(Key.C, Control: true)],
        [Shortcuts.Paste] = [new(Key.V, Control: true)],
        [Shortcuts.Delete] = [new(Key.Backspace)],
        [Shortcuts.RippleDelete] = [new(Key.Delete)],
        [Shortcuts.RippleDeleteAll] = [new(Key.Delete, Shift: true)],
        [Shortcuts.Split] = [new(Key.B, Control: true)],
        [Shortcuts.SplitAll] = [new(Key.B, Control: true, Shift: true)],
        [Shortcuts.MediaRename] = [new(Key.F2)],
        [Shortcuts.MediaImport] = [new(Key.I, Control: true)],
        [Shortcuts.Save] = [new(Key.S, Control: true)],
        [Shortcuts.SaveAs] = [new(Key.S, Control: true, Shift: true)],
        [Shortcuts.ShowHome] = [new(Key.H, Control: true, Shift: true)],
        [Shortcuts.ShowSettings] = [new(Key.Comma, Control: true)],
        [Shortcuts.NewProject] = [new(Key.N, Control: true)],
        [Shortcuts.OpenProject] = [new(Key.O, Control: true)],
        [Shortcuts.CloseProject] = [new(Key.W, Control: true)],
        [Shortcuts.Quit] = [new(Key.Q, Control: true)],
        [Shortcuts.Duplicate] = [new(Key.D, Control: true)],
        [Shortcuts.Deselect] = [new(Key.A, Control: true, Shift: true)],
        [Shortcuts.GoToStart] = [new(Key.Home)],
        [Shortcuts.GoToEnd] = [new(Key.End)],
        [Shortcuts.Loop] = [new(Key.L, Control: true)],
        [Shortcuts.ZoomIn] = [new(Key.Equal)],
        [Shortcuts.ZoomOut] = [new(Key.Minus)],
        [Shortcuts.ZoomFit] = [new(Key.Backslash)],
        [Shortcuts.Fullscreen] = [new(Key.F11)],
        [Shortcuts.Layout(1)] = [new(Key.Key1, Shift: true, Alt: true)],
        [Shortcuts.Layout(2)] = [new(Key.Key2, Shift: true, Alt: true)],
        [Shortcuts.Layout(3)] = [new(Key.Key3, Shift: true, Alt: true)],
        [Shortcuts.Layout(4)] = [new(Key.Key4, Shift: true, Alt: true)],
        [Shortcuts.Layout(5)] = [new(Key.Key5, Shift: true, Alt: true)],
        [Shortcuts.Layout(6)] = [new(Key.Key6, Shift: true, Alt: true)],
        [Shortcuts.Layout(7)] = [new(Key.Key7, Shift: true, Alt: true)],
        [Shortcuts.Layout(8)] = [new(Key.Key8, Shift: true, Alt: true)],
        [Shortcuts.Layout(9)] = [new(Key.Key9, Shift: true, Alt: true)],
    };

    readonly Dictionary<string, List<KeyCombo>> bindings = Defaults.ToDictionary(d => d.Key, d => d.Value.ToList());

    public IReadOnlyDictionary<string, List<KeyCombo>> Bindings => bindings;

    public void Bind(string action, params KeyCombo[] combos) => bindings[action] = [.. combos];

    public void Unbind(string action) => bindings.Remove(action);

    public IReadOnlyList<KeyCombo> Get(string action) => bindings.TryGetValue(action, out List<KeyCombo> combos) ? combos : [];

    // the keys an action has before anyone changes it
    public static IReadOnlyList<KeyCombo> DefaultsFor(string action) => Defaults.TryGetValue(action, out KeyCombo[] combos) ? combos : [];

    // every action bound to this combo. more than one is allowed - whoever
    // is asked first decides which of them it answers to
    public IEnumerable<string> ActionsFor(KeyCombo combo)
        => bindings.Where(b => b.Value.Contains(combo)).Select(b => b.Key);

    // the file is action -> "Ctrl+Y, Ctrl+Shift+Z". an entry that does not
    // parse is reported and skipped rather than taking the whole file down,
    // and an action the file does not mention keeps its default
    public void Load(string path = null)
    {
        path ??= FilePath;
        string file = ProjectSettings.GlobalizePath(path);

        if (!File.Exists(file))
        {
            // seed the file with the defaults so the user has something to edit
            Save(path);
            return;
        }

        try
        {
            Dictionary<string, string> entries =
                JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? [];

            foreach ((string action, string text) in entries)
            {
                List<KeyCombo> combos = [];
                bool readable = true;

                foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (KeyCombo.TryParse(part, out KeyCombo combo)) combos.Add(combo);
                    else readable = false;
                }

                if (readable) bindings[action] = combos;
                else GD.PushWarning($"Shortcut '{action}' has an unreadable binding '{text}' in {file}; keeping its default");
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"Could not read shortcuts from {file}: {e.Message}");
            return;
        }

        // write the merged set back, so an action added since the file was
        // made shows up in it with its default rather than staying invisible
        Save(path);
    }

    public void Save(string path = null)
    {
        path ??= FilePath;
        string file = ProjectSettings.GlobalizePath(path);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));

            Dictionary<string, string> entries = bindings.ToDictionary(b => b.Key, b => string.Join(", ", b.Value));
            // written plainly ("Ctrl+S", not "Ctrl+S"), since people edit this file by hand
            File.WriteAllText(file, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        }
        catch (Exception e)
        {
            GD.PushWarning($"Could not save shortcuts to {file}: {e.Message}");
        }
    }
}

public sealed class ShortcutEventArgs(string action, KeyCombo combo) : EventArgs
{
    public string Action { get; } = action;
    public KeyCombo Combo { get; } = combo;

    // set by whoever acts on it. that stops the climb, and keeps the key
    // from anything after it
    public bool Handled { get; set; }
}

// keyboard state, alongside Mouse.
//
// WHO GETS A KEY: the control with godot focus first - that happens in
// godot's own gui pass, before this ever sees the key, so a text field keeps
// its typing and a focused button keeps its space bar. a key they do not use
// arrives here, and climbs: from the focused control up through its
// ancestors, then from the captor (the view last clicked) up through its
// ancestors, and finally the root, offered to every node along the way that
// registered a handler, until one marks it handled. a view registers for the
// shortcuts it owns; the page registers for the ones that apply anywhere.
//
// CAPTURE: a view claims the keyboard when it is clicked, since it has no
// godot focus to speak of. claiming also takes godot's focus off whatever
// last had it, so a button clicked a minute ago does not go on answering
// the space bar
public class Keyboard
{
    // null once the node that claimed the keyboard has been freed, such as a closed window
    public Node Captor
    {
        get => GodotObject.IsInstanceValid(captor) ? captor : null;
        private set => captor = value;
    }

    Node captor;

    public ShortcutMap Shortcuts { get; } = new();

    readonly Dictionary<Node, Action<ShortcutEventArgs>> handlers = [];

    public void Register(Node node, Action<ShortcutEventArgs> handler) => handlers[node] = handler;

    public void Unregister(Node node) => handlers.Remove(node);

    public void Capture(Node node)
    {
        Captor = node;

        if (node is Control control && control.IsInsideTree()) control.GetViewport().GuiReleaseFocus();
    }

    // only the holder can let go - a stale release must not knock out
    // whoever claimed after it
    public void Release(Node node)
    {
        if (HasCapture(node)) Captor = null;
    }

    public bool HasCapture(Node node) => GodotObject.IsInstanceValid(Captor) && Captor == node;

    // runs an action as its key would, without one: offered from the focus in `from`'s window, the captor
    // when it's in that window, then `from` and its ancestors. returns whether something took it
    public bool Invoke(string action, Node from)
    {
        if (from is null || !from.IsInsideTree()) return false;

        Viewport viewport = from.GetViewport();
        Node captor = GodotObject.IsInstanceValid(Captor) && Captor.IsInsideTree() && Captor.GetWindow() == from.GetWindow() ? Captor : null;
        ShortcutEventArgs args = new(action, default);

        foreach (Node node in Chain(viewport.GuiGetFocusOwner(), captor, from, viewport.GetTree()?.Root))
        {
            if (!handlers.TryGetValue(node, out Action<ShortcutEventArgs> handler)) continue;

            handler(args);
            if (args.Handled) return true;
        }

        return false;
    }

    // whether a key bound to the action is down right now, whatever the modifiers
    public bool IsHeld(string action) => Shortcuts.Get(action).Any(c => Godot.Input.IsKeyPressed(c.Key));

    // offers a key nothing in the gui used to the handlers along the chain.
    // returns whether one of them took it, so the caller can keep it from
    // anything further down the line
    internal bool Dispatch(InputEventKey key, Viewport viewport)
    {
        // a held key repeats; a shortcut fires once per press
        if (!key.Pressed || key.Echo) return false;

        // a focused button activates on the release of the accept key but lets
        // the press through; answering the press as well would act on it twice
        if (viewport.GuiGetFocusOwner() is BaseButton { Disabled: false } && key.IsAction("ui_accept", exactMatch: true)) return false;

        KeyCombo combo = KeyCombo.From(key);
        List<string> actions = [.. Shortcuts.ActionsFor(combo)];
        if (actions.Count == 0) return false;

        List<Node> chain = Chain(viewport.GuiGetFocusOwner(), Captor, viewport.GetTree()?.Root);
        bool handled = false;

        foreach (string action in actions)
        {
            ShortcutEventArgs args = new(action, combo);

            foreach (Node node in chain)
            {
                if (!handlers.TryGetValue(node, out Action<ShortcutEventArgs> handler)) continue;

                handler(args);
                if (args.Handled) break;
            }

            handled |= args.Handled;
        }

        return handled;
    }

    // each start and then its ancestors, innermost first, without repeats
    static List<Node> Chain(params Node[] starts)
    {
        List<Node> chain = [];
        HashSet<Node> seen = [];

        foreach (Node start in starts)
        {
            for (Node node = start; node is not null && GodotObject.IsInstanceValid(node); node = node.GetParent())
            {
                if (seen.Add(node)) chain.Add(node);
            }
        }

        return chain;
    }
}
