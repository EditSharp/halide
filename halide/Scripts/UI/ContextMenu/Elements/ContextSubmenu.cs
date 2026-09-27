using Godot;
using Godot.Collections;

namespace Halide.Scripts.UI.ContextMenu;

[GlobalClass]
public partial class ContextSubmenu : ContextBaseButton
{
    [Export] public Array<ContextElement> Elements = [];
}
