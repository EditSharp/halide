using Godot;

namespace Halide.Scripts.UI.ContextMenu;

[GlobalClass]
public abstract partial class ContextBaseButton : ContextElement
{
    [Export] public ContextText Text;
    [Export] public ContextText ShortcutHint = null;
    [Export] public Texture2D Icon;

    [Export] public bool Enabled = true;
}
