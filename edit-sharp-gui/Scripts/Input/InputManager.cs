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
public partial class InputManager : Node
{
    public static InputManager Singleton;

    public Mouse Mouse = new();
    public ModifierState Modifiers = new();

    public override void _Ready()
    {
        Singleton ??= this;

        // keep tracking input while the tree is paused, so pausing mid-drag
        // does not freeze a button in Dragging
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Notification(int what)
    {
        // the window can lose focus mid-gesture - alt-tab, a modal, the os taking
        // over. the matching release will never arrive, so end every gesture now
        // rather than leave a button stuck in Dragging for the rest of the session
        if (what == NotificationApplicationFocusOut || what == NotificationWMWindowFocusOut)
            Mouse.ForceReleaseAll();
    }

    public override void _Input(InputEvent @event)
    {
        Mouse.EventId++;

        // modifiers first, so a press in the same event latches the modifiers
        // that were actually held when it happened
        if (@event is InputEventWithModifiers mod) Modifiers = ModifierState.From(mod);

        if (@event is InputEventMouse m) Mouse.Handle(m, Modifiers);
    }
}
