using EditSharpGUI.Scripts.UI;
using Godot;
using System;
using System.IO;

// a Home tile: poster, hover scrub over ten frames, name and date; laid out in ProjectItem.tscn
public partial class UIProjectItem : VBoxContainer
{
	[ExportGroup("Controls")]

	[Export] Control content;
	[Export] TextureRect picture;
	[Export] Control scrubTick;
	[Export] TextureRect missingIcon;
	[Export] Button options;
	[Export] Panel outline;
	[Export] Label nameLabel;
	[Export] Label dateLabel;
	[Export] LineEdit nameEdit;

	public RecentProject Project { get; private set; }
	public Home Home { get; private set; }

	// the frames skimmed across the tile
	public const int FrameCount = 10;

	Texture2D poster;
	Texture2D[] frames = [];

	public bool Selected
	{
		get => outline?.Visible ?? false;
		set { if (outline is not null) outline.Visible = value; }
	}

	public void Setup(Home home, RecentProject project)
	{
		Home = home;
		Project = project;
		if (IsNodeReady()) Refresh();
	}

	public override void _Ready()
	{
		if (options is not null) options.Pressed += () => Home?.ShowTileMenu(this, options);

		if (nameEdit is not null)
		{
			nameEdit.Visible = false;
			nameEdit.TextSubmitted += _ => EndRename(commit: true);
			nameEdit.FocusExited += () => EndRename(commit: true);
		}

		MouseExited += ShowPoster;

		if (scrubTick is not null) scrubTick.Visible = false;

		Refresh();
	}

	public void Refresh()
	{
		if (Project is null || !IsNodeReady()) return;

		bool missing = !Project.Exists;

		if (nameLabel is not null) { nameLabel.Text = Project.Name; nameLabel.TooltipText = Project.Path; }
		if (dateLabel is not null) dateLabel.Text = missing ? "Missing" : $"Edited {Ago(File.GetLastWriteTimeUtc(Project.Path))}";
		if (missingIcon is not null) missingIcon.Visible = missing;
		Modulate = missing ? new Color(1f, 1f, 1f, 0.5f) : Colors.White;

		(poster, frames) = missing ? (null, []) : ProjectThumbnails.Load(Project.Folder);
		ShowPoster();
	}

	// "just now", "5 minutes ago", "2 days ago", then the date
	static string Ago(DateTime utc)
	{
		TimeSpan span = DateTime.UtcNow - utc;

		if (span.TotalMinutes < 1) return "just now";
		if (span.TotalHours < 1) return Plural((int)span.TotalMinutes, "minute");
		if (span.TotalDays < 1) return Plural((int)span.TotalHours, "hour");
		if (span.TotalDays < 30) return Plural((int)span.TotalDays, "day");
		return utc.ToLocalTime().ToString("MMM d, yyyy");

		static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")} ago";
	}

	// ---- skimming ----

	void ShowPoster()
	{
		if (picture is not null) picture.Texture = poster;
		if (scrubTick is not null) scrubTick.Visible = false;
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseMotion motion && content is not null && frames.Length > 0 && content.GetRect().HasPoint(motion.Position))
		{
			float fraction = Mathf.Clamp((motion.Position.X - content.Position.X) / Mathf.Max(1f, content.Size.X), 0f, 0.9999f);
			int index = (int)(fraction * frames.Length);

			if (picture is not null) picture.Texture = frames[index];

			if (scrubTick is not null)
			{
				scrubTick.Visible = true;
				float width = content.Size.X / frames.Length;
				scrubTick.Position = new(index * width, scrubTick.Position.Y);
				scrubTick.Size = new(width, scrubTick.Size.Y);
			}
		}

		Home?.TileInput(this, @event);
	}

	// ---- renaming ----

	bool renaming;

	public void BeginRename()
	{
		if (nameEdit is null || nameLabel is null || renaming) return;

		renaming = true;
		nameEdit.Text = Project.Name;
		nameLabel.Visible = false;
		nameEdit.Visible = true;
		nameEdit.GrabFocus();
		nameEdit.SelectAll();
	}

	void EndRename(bool commit)
	{
		if (!renaming) return;
		renaming = false;

		nameEdit.Visible = false;
		nameLabel.Visible = true;

		string name = nameEdit.Text.Trim();
		if (commit && name.Length > 0 && name != Project.Name) Home?.Rename(this, name);
	}
}
