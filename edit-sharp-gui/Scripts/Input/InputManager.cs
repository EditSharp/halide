using Godot;
using ModifierState = EditSharpGUI.Scripts.Input.Modifiers;
using EditSharpGUI.Scripts.Input;

// central owner of input state. ui elements ask this what the mouse is doing
// instead of each parsing InputEvents and tracking their own press/drag state.
//
// this runs in _Input, which godot dispatches before any Control._GuiInput, so
// by the time an element is handed an event the state here is already current.
// that ordering is the whole design: nothing may call SetInputAsHandled() from
// an _Input override, or this node stops seeing events - and a swallowed
// release leaves a button stuck in a drag forever.
//
// keys are the other way round. a shortcut is dispatched from
// _UnhandledKeyInput, which godot only calls once the focused control has had
// the key and not used it - so a text field keeps its typing, a focused button
// keeps its space bar, and everything they leave alone climbs the tree to
// whoever registered for it. see Keyboard
public partial class InputManager : Node
{
    public static InputManager Singleton;

    public Mouse Mouse = new();
    public Keyboard Keyboard = new();
    public ModifierState Modifiers = new();

    public override void _EnterTree()
    {
        // here, not in _Ready: at startup godot runs every node's _EnterTree
        // before any node's _Ready, and the main scene's nodes reach for this
        // singleton as they enter
        Singleton ??= this;

        // an embedded window - a popup, a dialog - keeps its key input to
        // itself. every one that appears gets a relay so a key it does not
        // use still reaches the shortcuts. see ShortcutRelay
        GetTree().NodeAdded += OnNodeAdded;
    }

    public override void _ExitTree()
    {
        GetTree().NodeAdded -= OnNodeAdded;
    }

    public override void _Ready()
    {
        // keep tracking input while the tree is paused, so pausing mid-drag
        // does not freeze a button in Dragging
        ProcessMode = ProcessModeEnum.Always;

        // the user's own bindings, over the defaults. written out on first
        // run so there is a file to edit
        Keyboard.Shortcuts.Load();
    }

    void OnNodeAdded(Node node)
    {
        if (node is not Window window || window == GetTree().Root) return;
        if (window.HasNode(ShortcutRelay.NodeName)) return;

        // the window is mid-add; give it its relay once it has settled
        Callable.From(() => window.AddChild(new ShortcutRelay { Name = ShortcutRelay.NodeName })).CallDeferred();
    }

    public override void _Notification(int what)
    {
        // the window can lose focus mid-gesture - alt-tab, a modal, the os taking
        // over. the matching release will never arrive, so end every gesture now
        // rather than leave a button stuck in Dragging for the rest of the session
        if (what == NotificationApplicationFocusOut || what == NotificationWMWindowFocusOut)
            Mouse.ForceReleaseAll();
    }

    public override void _Input(InputEvent @event) => Feed(@event);

    // one event's worth of state, from the root window or relayed from another OS window
    public void Feed(InputEvent @event)
    {
        Mouse.EventId++;

        // modifiers first, so a press in the same event latches the modifiers
        // that were actually held when it happened
        if (@event is InputEventWithModifiers mod) Modifiers = ModifierState.From(mod);

        if (@event is InputEventMouse m) Mouse.Handle(m, Modifiers);
    }

    // a key nothing in the gui used. marking it handled once a shortcut takes
    // it is safe here - the event has already been seen by everything that
    // runs before this, and a key carries no gesture a swallow could strand
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey key && Keyboard.Dispatch(key, GetViewport())) GetViewport().SetInputAsHandled();
    }
}
