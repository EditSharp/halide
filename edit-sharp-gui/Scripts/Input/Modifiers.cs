using Godot;

namespace EditSharpGUI.Scripts.Input;

public struct Modifiers
{
    public bool Shift = false;
    public bool Control = false;
    public bool Alt = false;

    public Modifiers()
    {
    }

    public static Modifiers From(InputEventWithModifiers modifiers) => new()
    {
        Shift = modifiers.ShiftPressed,
        // folds cmd on mac into the same flag as ctrl elsewhere
        Control = modifiers.IsCommandOrControlPressed(),
        Alt = modifiers.AltPressed,
    };
}
