using Godot;
using System.Threading.Tasks;

namespace EditSharpGUI.Scripts.UI.Dialogs.Platform;

// one way of showing dialogs; finishes with the closing button's Id, or null when dismissed
public abstract class DialogHandler
{
	public abstract Task<string> ShowAsync(Dialog dialog, Window owner);
}
