using EditSharp.Components.Media;
using EditSharpGUI.Api;
using EditSharpGUI.Api.Extensions;
using EditSharpGUI.Scripts.App.Commands;
using EditSharpGUI.Scripts.Input;
using Godot;
using System.Text.Json.Nodes;

namespace SampleExtension;

/// <summary>Every kind of addition, once each.</summary>
public sealed class Sample : IExtension
{
	readonly SampleSettings settings = new();

	public void Activate(ExtensionContext context)
	{
		// a command, in the Edit menu, with a default key
		context.AddCommand(new Command
		{
			Id = "editsharp.sample.hello",
			Title = "Say Hello",
			CanRun = ctx => ctx.Window is not null,
			Run = (ctx, args) =>
			{
				string message = $"{settings.Greeting} from {ctx.Session.FilePath ?? "an unsaved project"}";
				context.Log(message);
				return message;
			},
		});
		context.AddMenuItem("Edit", "editsharp.sample.hello");
		context.AddShortcut("editsharp.sample.hello", new KeyCombo(Key.H, Control: true, Alt: true));

		// a view, first opening as a tab beside the inspector; built from stock controls
		context.AddView(new ViewDefinition("editsharp.sample.notes", "Notes", project =>
		{
			VBoxContainer notes = new();
			notes.AddChild(new Label { Text = $"Notes for {project.Name}" });
			notes.AddChild(new TextEdit { SizeFlagsVertical = Control.SizeFlags.ExpandFill, PlaceholderText = "Write anything…" });
			return notes;
		}, Beside: "inspector"));

		// a settings page
		context.AddSettingsPage("Sample", settings);

		// .samplewav files come in as audio
		context.AddImporter([".samplewav"], path => new AudioMedia { Path = path });

		context.OnProjectOpened(project => context.Log($"{project.Name} opened"));
	}

	public void Deactivate() { }
}
