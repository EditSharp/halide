using EditSharp.Editing;
using System.Collections.Generic;

// App Settings' Projects & Autosave page, saved as it changes
public sealed class ProjectsPage : IChoiceProvider
{
	static AppSettings Settings => AppSettings.Current;

	[Editable("Default folder", Group = "Projects", Order = 0, Editor = PropertyEditor.Path, Tooltip = "Where new projects are created")]
	[FolderPath]
	public string ProjectsFolder { get => Settings.ProjectsFolder; set { Settings.ProjectsFolder = value; Settings.Save(); } }

	[Editable("On startup", Group = "Projects", Order = 1, Default = StartupAction.Home)]
	public StartupAction OnStartup { get => Settings.OnStartup; set { Settings.OnStartup = value; Settings.Save(); } }

	[Editable("When the last window closes", Group = "Projects", Order = 2, Default = LastWindowAction.Quit)]
	public LastWindowAction OnLastWindowClosed { get => Settings.OnLastWindowClosed; set { Settings.OnLastWindowClosed = value; Settings.Save(); } }

	[Editable("Autosave every", Group = "Autosave", Order = 3, Min = 15, Max = 3600, Step = 15, Unit = "s", Default = 120)]
	public int AutosaveSeconds { get => Settings.AutosaveSeconds; set { Settings.AutosaveSeconds = value; Settings.Save(); } }

	[Editable("Backups kept", Group = "Autosave", Order = 4, Min = 1, Max = 100, Step = 1, Default = 10)]
	public int AutosaveBackups { get => Settings.AutosaveBackups; set { Settings.AutosaveBackups = value; Settings.Save(); } }

	public IReadOnlyList<Choice> ChoicesFor(string property) => property switch
	{
		nameof(OnStartup) => [new(StartupAction.Home, "Show Home"), new(StartupAction.ReopenLastSession, "Reopen last session"), new(StartupAction.HomeAndLastProject, "Home and the last project")],
		nameof(OnLastWindowClosed) => [new(LastWindowAction.Quit, "Quit"), new(LastWindowAction.StayInTray, "Stay in the tray")],
		_ => null,
	};
}
