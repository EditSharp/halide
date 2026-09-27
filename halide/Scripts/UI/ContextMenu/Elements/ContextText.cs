using Godot;

namespace Halide.Scripts.UI.ContextMenu;

// on its own in Elements it is a greyed, unselectable label
[GlobalClass]
public partial class ContextText : ContextElement
{
    [Export] public string Text = "";

    [Export] public Weight TextWeight = Weight.Normal;

    public enum Weight { Normal, Bold }

    public ContextText() { }
    public ContextText(string text) => Text = text;
}
