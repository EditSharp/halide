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

    static readonly Dictionary<string, KeyCombo[]> Defaults = new()
    {
        [Shortcuts.PlaybackToggle] = [new(Key.Space)],
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
    };

    readonly Dictionary<string, List<KeyCombo>> bindings = Defaults.ToDictionary(d => d.Key, d => d.Value.ToList());

    public IReadOnlyDictionary<string, List<KeyCombo>> Bindings => bindings;

    public void Bind(string action, params KeyCombo[] combos) => bindings[action] = [.. combos];

    public void Unbind(string action) => bindings.Remove(action);

    public IReadOnlyList<KeyCombo> Get(string action) => bindings.TryGetValue(action, out List<KeyCombo> combos) ? combos : [];

    // every action bound to this combo. more than one is allowed - whoever
    // is asked first decides which of them it answers to
    public IEnumerable<string> ActionsFor(KeyCombo combo)
        => bindings.Where(b => b.Value.Contains(combo)).Select(b => b.Key);

    // the file is action -> "Ctrl+Y, Ctrl+Shift+Z". an entry that does not
    // parse is reported and skipped rather than taking the whole file down,
    // and an action the file does not mention keeps its default
    public void Load(string path = DefaultPath)
    {
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

    public void Save(string path = DefaultPath)
    {
        string file = ProjectSettings.GlobalizePath(path);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));

            Dictionary<string, string> entries = bindings.ToDictionary(b => b.Key, b => string.Join(", ", b.Value));
            File.WriteAllText(file, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
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
    public Node Captor { get; private set; }

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

    // offers a key nothing in the gui used to the handlers along the chain.
    // returns whether one of them took it, so the caller can keep it from
    // anything further down the line
    internal bool Dispatch(InputEventKey key, Viewport viewport)
    {
        // a held key repeats; a shortcut fires once per press
        if (!key.Pressed || key.Echo) return false;

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
