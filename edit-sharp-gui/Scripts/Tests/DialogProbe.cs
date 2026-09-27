using EditSharpGUI.Scripts.UI.Theming;
using EditSharpGUI.Scripts.UI.Dialogs;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

// every dialog element through this platform's handler; prints DIALOGS OK
public partial class DialogProbe : Node
{
	bool ok = true;
	string shotSuffix = "";

	void Check(bool condition, string what)
	{
		ok &= condition;
		GD.Print($"PROBE {(condition ? "ok  " : "BAD ")} {what}");
	}

	public override async void _Ready()
	{
		GetTree().Root.GuiEmbedSubwindows = false;
		AppSettings.UseFile(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"editsharp-settings-{Guid.NewGuid():N}.json"));
		AppSettings.Current.OnLastWindowClosed = LastWindowAction.StayInTray;
		GD.Print($"PROBE handler {Dialogs.Handler.GetType().Name}");

		// --palette=res://Themes/Light.tres: the light look, its captures named -light
		string palette = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--palette="))?["--palette=".Length..];
		if (palette is not null && ThemeDB.GetProjectTheme() is EditSharpTheme theme)
		{
			theme.Palette = GD.Load<ThemePalette>(palette);
			shotSuffix = theme.Palette.Dark ? "" : "-light";
		}

		Window owner = new() { Visible = false, Title = "Owner", Size = new(800, 600), ForceNative = true };
		AddChild(owner);
		owner.Show();
		await Frames(3);

		// ---- a form ----
		Dialog form = new() { Title = "Probe form" };
		DialogTextField name = new() { Id = "name", Label = "Name", Placeholder = "Something" };
		DialogDropdown kind = new() { Id = "kind", Label = "Kind", Options = ["One", "Two", "Three"] };
		DialogNumber count = new() { Id = "count", Label = "Count", Min = 1, Max = 10, Value = 3, Suffix = "clips" };
		DialogPathField where = new() { Id = "where", Label = "Folder", Value = "/Users/probe/Movies", Mode = DialogPathField.PathMode.OpenFolder };
		DialogCheckbox check = new() { Id = "check", Text = "Remember this" };
		DialogList list = new() { Id = "list", Label = "Files" };
		list.Rows.Add(new DialogListRow { Id = "a", Text = "a.mp4", Detail = "C:/missing/a.mp4", ButtonText = "Locate..." });
		list.Buttons.Add(new DialogButton { Id = "search", Text = "Search Folder..." });
		form.Elements.Add(new DialogText { Id = "message", Text = "A probe of every element.", Style = DialogText.TextStyle.Heading });
		form.Elements.Add(name);
		form.Elements.Add(kind);
		form.Elements.Add(count);
		form.Elements.Add(where);
		form.Elements.Add(check);
		form.Elements.Add(list);
		form.Buttons.Add(new DialogButton { Id = "ok", Text = "OK", Role = DialogButtonRole.Default });
		form.Buttons.Add(new DialogButton { Id = "cancel", Text = "Cancel", Role = DialogButtonRole.Cancel });
		form.Validate = d => d.Find<DialogTextField>("name").Value.Length == 0 ? "Type a name." : null;

		DialogListRow pressedRow = null;
		list.Pressed += (row, button) => pressedRow = row;

		Task<DialogResult> answer = Dialogs.Show(form, owner);
		await Showing("Probe form");

		Check(DialogButtonEnabled("Probe form", "OK") == false, "the Enter button waits for a name");
		Check(DialogTextShown("Probe form", "Type a name.") == true, "and the problem shows");

		// the user types; code changes things while it's up
		DialogType("Probe form", "Name", "Typed");
		kind.Selected = 2;
		check.Checked = true;
		list.Rows[0].Detail = "C:/found/a.mp4";
		list.Rows[0].ButtonText = "Found";
		list.Refresh();
		await Frames(5);
		Check(DialogButtonEnabled("Probe form", "OK") == true, "typing a name lets it through");
		DialogPress("Probe form", "Found");
		await Frames(2);
		Check(pressedRow?.Id == "a", "a row's button reaches the code, and the dialog stays up");
		await Frames(5);
		Check(DialogButtonEnabled("Probe form", "OK") == true, "and OK is still enabled when it's captured");
		await Settle();
		Shot("Probe form", "form");

		DialogPress("Probe form", "OK");
		DialogResult result = await answer;
		Check(result.Is("ok") && result.Text("name") == "Typed" && result.Selected("kind") == 2 && result.Checked("check") && result.Number("count") == 3,
			$"OK answers with every value: {result.Button} {result.Text("name")} {result.Selected("kind")} {result.Checked("check")} {result.Number("count")}");

		// ---- the new project form, on a preset and on Custom ----
		Dialog newProject = NewProjectDialog.Build();
		Task<DialogResult> making = Dialogs.Show(newProject, owner);
		await Showing("New Project");
		await Settle();
		Shot("New Project", "new-project");
		newProject.UserEdited(newProject.Find<DialogDropdown>("resolution"), 4);
		await Frames(5);
		Check(DialogTextShown("New Project", newProject.Find<DialogText>("summary").Text) == true, "Custom shows its rows and the summary follows");
		await Settle();
		Shot("New Project", "new-project-custom");
		DialogPress("New Project", "Cancel");
		Check((await making).Is("cancel"), "and Cancel closes it");

		// ---- keys: enter takes the default button, escape the cancel ----
		Dialog keys = Dialogs.Question("Keys", "Enter or escape?", null, ("yes", "Yes", DialogButtonRole.Default), ("no", "No", DialogButtonRole.Cancel));
		Task<DialogResult> keyed = Dialogs.Show(keys, owner);
		await Showing("Keys");
		DialogKey("Keys", Key.Enter);
		Check((await keyed).Is("yes"), "Enter answers with the default button");

		keyed = Dialogs.Show(Dialogs.Question("Keys", "Enter or escape?", null, ("yes", "Yes", DialogButtonRole.Default), ("no", "No", DialogButtonRole.Cancel)), owner);
		await Showing("Keys");
		DialogKey("Keys", Key.Escape);
		Check((await keyed).Is("no"), "Escape answers with the cancel button");

		// ---- a long list scrolls on its own ----
		Dialog longList = new() { Title = "Long list" };
		DialogList many = new() { Id = "many" };
		for (int i = 0; i < 12; i++) many.Rows.Add(new DialogListRow { Id = $"row{i}", Text = $"clip-{i:00}.mp4", Detail = $"C:/footage/day-{i % 3}/clip-{i:00}.mp4", ButtonText = i == 11 ? "Last..." : "Locate..." });
		longList.Elements.Add(new DialogText { Id = "message", Text = "12 media files are missing.", Style = DialogText.TextStyle.Heading });
		longList.Elements.Add(many);
		longList.Buttons.Add(new DialogButton { Id = "done", Text = "Done", Role = DialogButtonRole.Default });
		DialogListRow lastPressed = null;
		many.Pressed += (row, _) => lastPressed = row;

		Task<DialogResult> listed = Dialogs.Show(longList, owner);
		await Showing("Long list");
		DialogPress("Long list", "Last...");
		await Frames(3);
		Check(lastPressed?.Id == "row11", $"a row scrolled out of sight still answers: {lastPressed?.Id}");
		await Settle();
		Shot("Long list", "long-list");
		DialogPress("Long list", "Done");
		Check((await listed).Is("done"), "and the long list closes");

		// ---- the question a project window asks when it closes with changes ----
		string folder = Path.Combine(Path.GetTempPath(), $"editsharp-dialogs-{Guid.NewGuid():N}");
		RecentProjects.UseFile(Path.Combine(folder, "projects.json"));
		ProjectWindow project = ProjectManager.Singleton.CreateProject(folder, "Dialog Probe", new EditSharp.Rendering.RenderSettings());
		await Frames(5);
		using (EditSharp.History.Transaction.Scope change = project.Session.Project.History.Begin("edit"))
		{
			project.Session.Project.Timeline.VideoChannels[0].Name = "Changed";
			change.Commit();
		}

		Task<bool> closing = project.CloseAsync();
		await Showing("Unsaved changes");
		await Settle();
		Shot("Unsaved changes", "unsaved");
		DialogPress("Unsaved changes", "Cancel");
		Check(!await closing && ProjectManager.Singleton.OpenProjects.Contains(project), "Cancel keeps the project open");

		closing = project.CloseAsync();
		await Showing("Unsaved changes");
		DialogPress("Unsaved changes", "Don't Save");
		Check(await closing && !ProjectManager.Singleton.OpenProjects.Contains(project), "Don't Save closes it");

		try { Directory.Delete(folder, true); } catch (IOException) { }

		GD.Print(ok ? "DIALOGS OK" : "DIALOGS FAILED");
		GetTree().Quit();
	}

	// ---- driving a dialog: through the themed windows here; the native handlers answer the same calls ----

	// until the dialog is up, as a person would wait; a native one is built on another thread
	async Task Showing(string title)
	{
		double until = Godot.Time.GetTicksMsec() + 5000;
		while (!(DialogProbeDriver.Showing(title) ?? DialogWindow(title) is { Visible: true }) && Godot.Time.GetTicksMsec() < until) await Frames(1);
		await Frames(5);
	}

	// native controls fade between states; a capture waits them out
	async Task Settle() => await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);

	Window DialogWindow(string title) => Descendants(GetTree().Root).OfType<Window>().FirstOrDefault(w => w.Title == title && !w.IsQueuedForDeletion());

	void DialogPress(string title, string text)
	{
		if (DialogProbeDriver.Press(title, text)) return;
		Descendants(DialogWindow(title)).OfType<Button>().First(b => b.Text == text).EmitSignal(BaseButton.SignalName.Pressed);
	}

	bool? DialogButtonEnabled(string title, string text)
	{
		if (DialogProbeDriver.Enabled(title, text) is bool native) return native;
		return Descendants(DialogWindow(title)).OfType<Button>().FirstOrDefault(b => b.Text == text) is { } b ? !b.Disabled : null;
	}

	void DialogType(string title, string label, string text)
	{
		if (DialogProbeDriver.Type(title, label, text)) return;
		LineEdit edit = Descendants(DialogWindow(title)).OfType<LineEdit>().First();
		edit.Text = text;
		edit.EmitSignal(LineEdit.SignalName.TextChanged, text);
	}

	bool? DialogTextShown(string title, string text)
	{
		if (DialogProbeDriver.TextShown(title, text) is bool native) return native;
		return Descendants(DialogWindow(title)).OfType<Label>().Any(l => l.Text == text && l.IsVisibleInTree());
	}

	void DialogKey(string title, Key key)
	{
		if (DialogProbeDriver.Key(title, key)) return;
		Window window = DialogWindow(title);
		// a focused button takes enter itself, as ui_accept
		if (key == Key.Enter && window?.GuiGetFocusOwner() is Button focused) focused.EmitSignal(BaseButton.SignalName.Pressed);
		else window?.EmitSignal(Window.SignalName.WindowInput, new InputEventKey { Keycode = key, Pressed = true });
	}

	void Shot(string title, string name)
	{
		string file = ProjectSettings.GlobalizePath($"user://probe-dialog-{name}{shotSuffix}.png");
		if (DialogProbeDriver.Shot(title, file) || DialogWindow(title) is not { } window) { GD.Print($"PROBE shot {file}"); return; }
		window.GetTexture().GetImage().SavePng(file);
		GD.Print($"PROBE shot {file}");
	}

	static IEnumerable<Node> Descendants(Node node)
	{
		if (node is null) yield break;
		foreach (Node child in node.GetChildren())
		{
			yield return child;
			foreach (Node below in Descendants(child)) yield return below;
		}
	}

	async Task Frames(int count)
	{
		for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
}

// routes the probe's actions to the native driver; false or null for the themed dialogs
public static partial class DialogProbeDriver
{
	static bool Windows => Dialogs.Handler is EditSharpGUI.Scripts.UI.Dialogs.Platform.Windows.WindowsDialogHandler;
	static bool Mac => Dialogs.Handler is EditSharpGUI.Scripts.UI.Dialogs.Platform.MacOS.MacOSDialogHandler;

	public static bool Press(string title, string text) => Windows ? WindowsDialogDriver.Press(title, text) : Mac && MacOSDialogDriver.Press(title, text);
	public static bool? Enabled(string title, string text) => Windows ? WindowsDialogDriver.Enabled(title, text) : Mac ? MacOSDialogDriver.Enabled(title, text) : null;
	public static bool Type(string title, string label, string text) => Windows ? WindowsDialogDriver.Type(title, label, text) : Mac && MacOSDialogDriver.Type(title, label, text);
	public static bool Shot(string title, string file) => Windows ? WindowsDialogDriver.Shot(title, file) : Mac && MacOSDialogDriver.Shot(title, file);
	public static bool? Showing(string title) => Windows ? WindowsDialogDriver.Showing(title) : Mac ? MacOSDialogDriver.Showing(title) : null;
	public static bool? TextShown(string title, string text) => Windows ? WindowsDialogDriver.TextShown(title, text) : Mac ? MacOSDialogDriver.TextShown(title, text) : null;
	public static bool Key(string title, Key key) => Windows ? WindowsDialogDriver.Key(title, key) : Mac && MacOSDialogDriver.Key(title, key);
}
