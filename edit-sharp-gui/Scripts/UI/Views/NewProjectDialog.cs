using EditSharp.Rendering;
using EditSharpGUI.Scripts.UI.Dialogs;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

// adding a project: a native dialog over Home. a name, where it goes, and its
// frame size and rate, with a way to open an existing project instead
public static class NewProjectDialog
{
	// the preset sizes, landscape; Vertical swaps width and height
	static readonly (string Label, int Width, int Height)[] Resolutions =
	[
		("3840 × 2160 (4K UHD)", 3840, 2160),
		("2560 × 1440 (QHD)", 2560, 1440),
		("1920 × 1080 (Full HD)", 1920, 1080),
		("1280 × 720 (HD)", 1280, 720),
	];

	static readonly (string Label, Rational Rate)[] Rates =
	[
		("23.976", new Rational(24000, 1001)),
		("24", 24),
		("25", 25),
		("29.97", new Rational(30000, 1001)),
		("30", 30),
		("50", 50),
		("59.94", new Rational(60000, 1001)),
		("60", 60),
	];

	// the form, filled in and checked as it's typed in
	public static Dialog Build()
	{
		string location = AppSettings.Current.ProjectsFolder;

		Dialog dialog = new() { Title = "New Project" };
		dialog.Elements.Add(new DialogText { Id = "heading", Text = "Create a new project", Style = DialogText.TextStyle.Heading });
		dialog.Elements.Add(new DialogTextField { Id = "name", Label = "Name", Value = FreeName(location), Placeholder = "Project name" });
		dialog.Elements.Add(new DialogPathField { Id = "location", Label = "Location", Value = location, Mode = DialogPathField.PathMode.OpenFolder });
		dialog.Elements.Add(new DialogDropdown { Id = "resolution", Label = "Resolution", Options = [.. Resolutions.Select(r => r.Label), "Custom"], Selected = 2 });
		dialog.Elements.Add(new DialogNumber { Id = "width", Label = "Width", Min = 16, Max = 16384, Step = 2, Value = 1920, Suffix = "px", Visible = false });
		dialog.Elements.Add(new DialogNumber { Id = "height", Label = "Height", Min = 16, Max = 16384, Step = 2, Value = 1080, Suffix = "px", Visible = false });
		dialog.Elements.Add(new DialogCheckbox { Id = "vertical", Text = "Vertical" });
		dialog.Elements.Add(new DialogDropdown { Id = "rate", Label = "Frame rate", Options = [.. Rates.Select(r => r.Label)], Selected = 4 });
		dialog.Elements.Add(new DialogNumber { Id = "customRate", Label = "Frame rate", Min = 1, Max = 240, Step = 1, Value = 30, Suffix = "fps", Visible = false });
		dialog.Elements.Add(new DialogText { Id = "summary" });

		dialog.Buttons.Add(new DialogButton { Id = "open", Text = "Open Existing…" });
		dialog.Buttons.Add(new DialogButton { Id = "create", Text = "Create", Role = DialogButtonRole.Default });
		dialog.Buttons.Add(new DialogButton { Id = "cancel", Text = "Cancel", Role = DialogButtonRole.Cancel });

		dialog.Validate = Problem;
		dialog.Edited += _ => Follow(dialog);
		Follow(dialog);
		return dialog;
	}

	// shows the form over `owner` and does what it was answered with
	public static async Task ShowAsync(Node owner)
	{
		Dialog dialog = Build();
		DialogResult answer = await Dialogs.Show(dialog, owner);

		if (answer.Is("open")) OpenExisting(owner);
		else if (answer.Is("create")) await CreateAsync(dialog, owner);
	}

	// "Untitled Project", or with a number when that folder is taken
	static string FreeName(string parent)
	{
		string name = "Untitled Project";
		for (int n = 2; Directory.Exists(Path.Combine(parent, name)); n++) name = $"Untitled Project {n}";
		return name;
	}

	static bool Custom(Dialog d) => d.Find<DialogDropdown>("resolution").Selected == Resolutions.Length;

	// a typed rate as an exact fraction: 29.97 is 30000/1001, as the NTSC rates are
	static Rational RateOf(double fps)
	{
		double ntsc = fps * 1.001;
		if (Math.Abs(ntsc - Math.Round(ntsc)) < 0.0005 && Math.Abs(fps - Math.Round(fps)) > 0.0005) return new Rational((long)Math.Round(ntsc) * 1000, 1001);
		return Rational.Approximate(fps, 1000);
	}

	// what the project will be made at
	static (int Width, int Height, Rational Rate) Format(Dialog d)
	{
		if (Custom(d)) return ((int)d.Find<DialogNumber>("width").Value, (int)d.Find<DialogNumber>("height").Value, RateOf(d.Find<DialogNumber>("customRate").Value));

		(string _, int width, int height) = Resolutions[Math.Clamp(d.Find<DialogDropdown>("resolution").Selected, 0, Resolutions.Length - 1)];
		Rational rate = Rates[Math.Clamp(d.Find<DialogDropdown>("rate").Selected, 0, Rates.Length - 1)].Rate;
		return d.Find<DialogCheckbox>("vertical").Checked ? (height, width, rate) : (width, height, rate);
	}

	// Custom trades the presets for typed values, and the summary says what will be made
	static void Follow(Dialog d)
	{
		bool custom = Custom(d);
		foreach ((string id, bool visible) in new[] { ("width", custom), ("height", custom), ("customRate", custom), ("vertical", !custom), ("rate", !custom) })
		{
			DialogElement element = d.Elements.First(e => e.Id == id);
			if (element.Visible != visible) element.Visible = visible;
		}

		(int width, int height, Rational rate) = Format(d);
		string text = $"{width} × {height}, {Rates.FirstOrDefault(r => r.Rate == rate).Label ?? rate.Value.ToString("0.###")} fps";
		DialogText summary = d.Find<DialogText>("summary");
		if (summary.Text != text) summary.Text = text;
	}

	// the reason the project can't be made as it stands, or null
	static string Problem(Dialog d)
	{
		string name = d.Find<DialogTextField>("name").Value.Trim();
		string location = d.Find<DialogPathField>("location").Value.Trim();

		if (name.Length == 0) return "Give the project a name.";
		if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "The name has characters a folder name can't.";
		if (location.Length == 0) return "Choose where the project goes.";
		if (Directory.Exists(Path.Combine(location, name))) return $"There's already a folder named '{name}' there.";
		if (Custom(d) && (d.Find<DialogNumber>("width").Value % 2 != 0 || d.Find<DialogNumber>("height").Value % 2 != 0)) return "The frame size has to be even.";
		if (Custom(d) && d.Find<DialogNumber>("customRate").Value <= 0) return "The frame rate has to be above zero.";

		return null;
	}

	static void OpenExisting(Node owner)
	{
		DisplayServer.FileDialogShow("Open a project", AppSettings.Current.ProjectsFolder, "", false, DisplayServer.FileDialogMode.OpenFile,
			[$"*{ProjectFile.Extension};EditSharp projects"],
			Callable.From((bool ok, string[] paths, long filter) =>
			{
				if (ok && paths.Length > 0) _ = ProjectManager.Singleton.OpenProjectAsync(paths[0], owner);
			}));
	}

	static async Task CreateAsync(Dialog d, Node owner)
	{
		(int width, int height, Rational rate) = Format(d);
		string location = d.Find<DialogPathField>("location").Value.Trim();
		string name = d.Find<DialogTextField>("name").Value.Trim();

		// the folder picked becomes the default for the next one
		AppSettings.Current.ProjectsFolder = location;
		AppSettings.Current.Save();

		try
		{
			ProjectManager.Singleton.CreateProject(location, name, new RenderSettings { Resolution = new(width, height), Framerate = rate });
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			await Dialogs.Show(Dialogs.Question("New Project", $"Couldn't create {name}.", e.Message, ("ok", "OK", DialogButtonRole.Default)), owner);
		}
	}
}
