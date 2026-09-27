using EditSharpGUI.Scripts.UI.Dialogs.Platform;
using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace EditSharpGUI.Scripts.UI.Dialogs;

// shows dialogs natively where the OS has them, as a themed window elsewhere
public static class Dialogs
{
	public static DialogHandler Handler { get; } = Create();

	// answers dialogs in place of the user when set: the Id of the button to close with; for tests
	public static Func<Dialog, string> Answering;

	static DialogHandler Create()
	{
		string forced = OS.GetEnvironment("EDITSHARP_DIALOG_HANDLER");
		if (forced == "godot") return new GodotDialogHandler();

		return OS.GetName() switch
		{
			"Windows" => new Platform.Windows.WindowsDialogHandler(),
			"macOS" when Platform.MacOS.MacOSDialogHandler.Available => new Platform.MacOS.MacOSDialogHandler(),
			_ => new GodotDialogHandler(),
		};
	}

	// shows the dialog over `owner`'s window and returns the button and every value
	public static async Task<DialogResult> Show(Dialog dialog, Node owner)
	{
		ArgumentNullException.ThrowIfNull(dialog);

		if (Answering is { } answering) return new DialogResult(answering(dialog) ?? dialog.Cancel?.Id ?? "", dialog.Values());

		dialog.Attach();
		try
		{
			string button = await Handler.ShowAsync(dialog, owner?.GetWindow());
			return new DialogResult(button ?? dialog.Cancel?.Id ?? "", dialog.Values());
		}
		finally
		{
			dialog.Release();
		}
	}

	// a message dialog with buttons given as (id, text, role)
	public static Dialog Question(string title, string message, string detail, params (string Id, string Text, DialogButtonRole Role)[] buttons)
	{
		Dialog dialog = new() { Title = title };
		dialog.Elements.Add(new DialogText { Id = "message", Text = message, Style = DialogText.TextStyle.Heading });
		if (!string.IsNullOrEmpty(detail)) dialog.Elements.Add(new DialogText { Id = "detail", Text = detail });
		foreach ((string id, string text, DialogButtonRole role) in buttons) dialog.Buttons.Add(new DialogButton { Id = id, Text = text, Role = role });
		return dialog;
	}
}
