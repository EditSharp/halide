using Godot;
using System.Threading.Tasks;

namespace EditSharpGUI.Scripts.UI.Dialogs.Platform;

// one way of showing dialogs. ShowAsync puts the dialog up over its owner,
// follows the dialog's Changed while it's up, reports the user's edits
// through Dialog.UserEdited and list presses through DialogList.Press, and
// finishes with the Id of the button that closed it (null for dismissed)
public abstract class DialogHandler
{
	public abstract Task<string> ShowAsync(Dialog dialog, Window owner);
}
