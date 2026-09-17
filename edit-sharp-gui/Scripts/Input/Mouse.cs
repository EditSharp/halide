using Godot;

namespace EditSharpGUI.Scripts.Input;

// what a button is doing right now, carried across frames
public enum MouseButtonClickState
{
    Released,
    Clicking,
    Dragging
}

// what a button just did, for the event currently being dispatched.
// this is the part a plain state snapshot cannot give you: by the time a ui
// element sees a release, ClickState is already back to Released, so the fact
// that it *was* a drag only survives as an action
public enum MouseAction
{
    None,
    Press,
    DoubleClick,
    DragStart,
    DragMove,
    Click,
    DragEnd
}

// implemented by anything that captures a drag and has visual state to undo if
// that drag never gets a release of its own - see Mouse.ForceRelease
public interface IDragCancellable
{
    void CancelDrag(MouseButtonState button);
}

public class Mouse
{
    // bumped once per input event. an action is only valid for the event that
    // produced it - see MouseButtonState.Action
    public ulong EventId { get; internal set; }

    public MouseButtonState LeftButton, MiddleButton, RightButton;

    public Mouse()
    {
        LeftButton = new(this);
        MiddleButton = new(this);
        RightButton = new(this);
    }

    public Vector2 CurrentPosition;

    public const int MIN_DRAG_PIXELS = 2;

    Vector2 pendingMotionDelta;
    ulong motionEventId;

    // how far the cursor moved during the event being dispatched, in screen
    // pixels. zero on any event that was not motion
    public Vector2 MotionDelta => motionEventId == EventId ? pendingMotionDelta : Vector2.Zero;

    Vector2 pendingScroll;
    ulong scrollEventId;

    // wheel movement for the event being dispatched, in the same screen-space
    // sense as everything else here: +Y scrolls down, +X scrolls right. zero on
    // any event that was not a wheel event. trackpads report fractional amounts
    public Vector2 Scroll => scrollEventId == EventId ? pendingScroll : Vector2.Zero;

    public bool IsScrolling => Scroll != Vector2.Zero;

    // wheel and extra buttons have no press/drag state worth tracking,
    // so they map to null and get ignored
    public MouseButtonState Get(MouseButton button) => button switch
    {
        MouseButton.Left => LeftButton,
        MouseButton.Middle => MiddleButton,
        MouseButton.Right => RightButton,
        _ => null
    };

    // the whole distance travelled since the button went down
    public Vector2 GetDragDelta(MouseButtonState button) => CurrentPosition - button.ClickStartPosition;

    // just this one event worth of movement. this is what anything that
    // accumulates wants - panning a view by the total drag on every event would
    // re-apply the entire distance each time and fly off
    public Vector2 GetDragStepDelta(MouseButtonState button)
        => button.ClickState == MouseButtonClickState.Dragging ? MotionDelta : Vector2.Zero;

    public bool IsDragging(MouseButtonState button)
    {
        Vector2 delta = GetDragDelta(button);
        return Mathf.Abs(delta.X) > MIN_DRAG_PIXELS || Mathf.Abs(delta.Y) > MIN_DRAG_PIXELS;
    }

    internal void Handle(InputEventMouse m, Modifiers modifiers)
    {
        if (m is InputEventMouseButton mb) HandleButton(mb, modifiers);
        else if (m is InputEventMouseMotion mm) HandleMotion(mm);
    }

    void HandleButton(InputEventMouseButton mb, Modifiers modifiers)
    {
        CurrentPosition = mb.GlobalPosition;

        // the wheel arrives as a button pressed and released in the same breath.
        // it has no press/drag life of its own, only a direction and an amount
        if (IsWheel(mb.ButtonIndex))
        {
            // Factor is the scroll amount, but not every platform fills it in
            if (mb.Pressed) SetScroll(WheelDirection(mb.ButtonIndex) * (mb.Factor != 0f ? mb.Factor : 1f));
            return;
        }

        MouseButtonState button = Get(mb.ButtonIndex);
        if (button is null) return;

        if (mb.Pressed)
        {
            button.ClickStartPosition = mb.GlobalPosition;
            button.PressModifiers = modifiers;
            // a fresh press belongs to nobody until an element claims it
            button.ReleaseCapture();
            button.ClickState = MouseButtonClickState.Clicking;
            button.SetAction(mb.DoubleClick ? MouseAction.DoubleClick : MouseAction.Press);
        }
        else
        {
            // release the button before reporting it. a handler that throws must
            // not leave this button stuck in a drag it can never end
            bool wasDragging = button.ClickState == MouseButtonClickState.Dragging;
            button.ClickState = MouseButtonClickState.Released;
            button.SetAction(wasDragging ? MouseAction.DragEnd : MouseAction.Click);
            // Captor deliberately survives this event - the DragEnd/Click
            // handlers about to run still need to recognise themselves
        }
    }

    void HandleMotion(InputEventMouseMotion mm)
    {
        CurrentPosition = mm.GlobalPosition;
        SetMotionDelta(mm.Relative);

        Reconcile(mm.ButtonMask);

        // controls keep receiving motion while a button is held no matter where
        // the cursor goes, so a drag can run past the edge of the element
        Advance(LeftButton);
        Advance(MiddleButton);
        Advance(RightButton);

        void Advance(MouseButtonState button)
        {
            if (button.ClickState == MouseButtonClickState.Clicking)
            {
                if (!IsDragging(button)) return;

                button.ClickState = MouseButtonClickState.Dragging;
                button.SetAction(MouseAction.DragStart);
            }
            else if (button.ClickState == MouseButtonClickState.Dragging)
            {
                button.SetAction(MouseAction.DragMove);
            }
        }
    }

    // a release can go missing - alt-tab, a modal grabbing the pointer, another
    // node swallowing the event. every motion event carries which buttons the os
    // thinks are really down, so believe that over what we think
    void Reconcile(MouseButtonMask mask)
    {
        Check(LeftButton, MouseButtonMask.Left);
        Check(MiddleButton, MouseButtonMask.Middle);
        Check(RightButton, MouseButtonMask.Right);

        void Check(MouseButtonState button, MouseButtonMask flag)
        {
            if (button.ClickState == MouseButtonClickState.Released) return;
            if ((mask & flag) != 0) return;

            ForceRelease(button);
        }
    }

    // end a gesture that will never get a release of its own. deliberately sets
    // no action: the captor is told directly, and emitting DragEnd as well would
    // have it both finish and cancel the same drag
    internal void ForceRelease(MouseButtonState button)
    {
        if (button.ClickState == MouseButtonClickState.Released) return;

        bool wasDragging = button.ClickState == MouseButtonClickState.Dragging;
        button.ClickState = MouseButtonClickState.Released;

        if (wasDragging && button.Captor is IDragCancellable captor) captor.CancelDrag(button);
    }

    internal void ForceReleaseAll()
    {
        ForceRelease(LeftButton);
        ForceRelease(MiddleButton);
        ForceRelease(RightButton);
    }

    static bool IsWheel(MouseButton button) => button
        is MouseButton.WheelUp or MouseButton.WheelDown
        or MouseButton.WheelLeft or MouseButton.WheelRight;

    static Vector2 WheelDirection(MouseButton button) => button switch
    {
        MouseButton.WheelUp => Vector2.Up,
        MouseButton.WheelDown => Vector2.Down,
        MouseButton.WheelLeft => Vector2.Left,
        MouseButton.WheelRight => Vector2.Right,
        _ => Vector2.Zero
    };

    void SetMotionDelta(Vector2 delta)
    {
        pendingMotionDelta = delta;
        motionEventId = EventId;
    }

    void SetScroll(Vector2 scroll)
    {
        pendingScroll = scroll;
        scrollEventId = EventId;
    }
}

public class MouseButtonState(Mouse mouse)
{
    public MouseButtonClickState ClickState = MouseButtonClickState.Released;

    public Vector2 ClickStartPosition;

    // modifiers as they were when the button went down. deciding a click from
    // release-time modifiers means letting go of shift mid-gesture silently
    // changes what the gesture meant
    public Modifiers PressModifiers;

    // the node that claimed this press. only the captor acts on the drag or
    // click that follows, so two elements can never disagree about who the
    // mouse is acting on
    public Node Captor { get; private set; }

    MouseAction pendingAction;
    ulong actionEventId;

    // stamped rather than cleared: plenty of events reach the manager that no
    // _GuiInput ever consumes, and a leftover action would still read as live
    // from _Process or a signal callback
    public MouseAction Action => actionEventId == mouse.EventId ? pendingAction : MouseAction.None;

    internal void SetAction(MouseAction action)
    {
        pendingAction = action;
        actionEventId = mouse.EventId;
    }

    internal void ReleaseCapture() => Captor = null;

    // godot dispatches gui input from the deepest control outwards, so the
    // first claimant is the innermost one - exactly who should win
    public bool Capture(Node node)
    {
        if (GodotObject.IsInstanceValid(Captor)) return Captor == node;

        Captor = node;
        return true;
    }

    public bool HasCapture(Node node) => GodotObject.IsInstanceValid(Captor) && Captor == node;
}
