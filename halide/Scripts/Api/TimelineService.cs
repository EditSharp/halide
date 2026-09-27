using EditSharp;
using EditSharp.Components;
using EditSharp.Components.Channels;
using EditSharp.Components.Clips;
using EditSharp.Components.Media;
using EditSharp.History;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halide.Api;

/// <summary>A project's timeline as the editor shows it: its clips, selection, playhead and zoom, and edits to them.</summary>
public sealed class TimelineService
{
	readonly ProjectHandle project;

	internal TimelineService(ProjectHandle project) => this.project = project;

	UITimeline View => project.Editor.TimelineView;

	/// <summary>The timeline being edited.</summary>
	public Timeline Timeline => View.Timeline;

	/// <summary>Every clip, channel by channel.</summary>
	public IReadOnlyList<Clip> Clips => [.. Timeline.Channels.SelectMany(c => c.Clips)];

	/// <summary>The clips covering a moment.</summary>
	public IReadOnlyList<Clip> ClipsAt(Time time) => [.. Clips.Where(c => c.Start <= time && time < c.End)];

	public Clip Find(Guid id) => Clips.FirstOrDefault(c => c.Id == id);

	/// <summary>Where the playhead is; setting it moves the picture and the inspector with it.</summary>
	public Time Playhead
	{
		get => View.PlayheadTime;
		set => project.Editor.SeekTo(value);
	}

	/// <summary>The selected clips.</summary>
	public IReadOnlyList<Clip> Selected => View.SelectedClips;

	public void Select(IEnumerable<Clip> clips) => View.SelectClips(clips);

	public void SelectAll() => Select(Clips);

	public void Deselect() => Select([]);

	/// <summary>How many pixels a second of the timeline spans.</summary>
	public double PixelsPerSecond
	{
		get => View.PixelsPerSecond;
		set => View.PixelsPerSecond = value;
	}

	/// <summary>The whole timeline across the view.</summary>
	public void ZoomToFit() => View.ZoomToFit();

	/// <summary>The view reads the model again, after edits made to it directly or through its history.</summary>
	public void Reconcile() => View.Reconcile();

	// ---- edits: each one undo entry ----

	/// <summary>Places media on the timeline at <paramref name="at"/>, on a video or audio channel counted from the first; returns the new clips.</summary>
	/// <remarks>A video with sound gets a linked audio clip on the matching audio channel.</remarks>
	public IReadOnlyList<Clip> Place(IMedia media, Time at, int channel = 0, bool video = true) => Place([media], at, channel, video);

	public IReadOnlyList<Clip> Place(IReadOnlyList<IMedia> media, Time at, int channel = 0, bool video = true)
	{
		HashSet<Clip> before = [.. Clips];
		View.PlaceMedia(media, at, video, channel);
		return [.. Clips.Where(c => !before.Contains(c))];
	}

	/// <summary>Moves a clip to start at <paramref name="start"/>, onto another channel of its kind when given.</summary>
	public void Move(Clip clip, Time start, Channel channel = null) => Edit($"Move {clip.Name}", () => clip.Move(start, channel));

	/// <summary>Moves a clip's start by <paramref name="amount"/>: positive shortens it, negative extends it.</summary>
	public void TrimStart(Clip clip, Time amount) => Edit($"Trim {clip.Name}", () => clip.TrimStart(amount));

	/// <summary>Moves a clip's end in by <paramref name="amount"/>: positive shortens it, negative extends it.</summary>
	public void TrimEnd(Clip clip, Time amount) => Edit($"Trim {clip.Name}", () => clip.TrimEnd(amount));

	/// <summary>Cuts clips in two at <paramref name="at"/>: the given ones, or every clip covering it.</summary>
	public void Split(Time at, IEnumerable<Clip> clips = null)
	{
		List<Clip> cut = [.. (clips ?? ClipsAt(at)).Where(c => c.Start < at && at < c.End)];
		if (cut.Count > 0) Edit(cut.Count == 1 ? $"Split {cut[0].Name}" : $"Split {cut.Count} clips", () => { foreach (Clip c in cut) c.Split(at); });
	}

	/// <summary>Removes clips; with <paramref name="ripple"/>, what follows on their channels closes the gap.</summary>
	public void Delete(IEnumerable<Clip> clips, bool ripple = false)
	{
		List<Clip> gone = [.. clips];
		if (gone.Count == 0) return;

		Edit(gone.Count == 1 ? $"Delete {gone[0].Name}" : $"Delete {gone.Count} clips", () =>
		{
			foreach (Clip c in gone.OrderByDescending(c => c.Start))
			{
				if (ripple) c.RippleDelete();
				else c.Delete();
			}
		});
	}

	/// <summary>Copies of clips straight after them, on the same channels; returns the copies.</summary>
	public IReadOnlyList<Clip> Duplicate(IEnumerable<Clip> clips)
	{
		HashSet<Clip> before = [.. Clips];
		Select(clips);
		View.DuplicateSelection();
		return [.. Clips.Where(c => !before.Contains(c))];
	}

	/// <summary>Links clips so they move and trim together.</summary>
	public void Link(IEnumerable<Clip> clips) => Edit("Link clips", () => Timeline.Link(clips));

	/// <summary>A new video or audio channel after the others of its kind.</summary>
	public Channel AddChannel(bool video)
	{
		Channel added = null;
		Edit(video ? "Add video channel" : "Add audio channel", () => added = Timeline.AddChannel(video ? new VideoChannel() : new AudioChannel()));
		return added;
	}

	/// <summary>A change was undone, redone or committed.</summary>
	public event Action Changed
	{
		add => Handlers.Add(value);
		remove => Handlers.Remove(value);
	}

	List<Action> Handlers
	{
		get
		{
			if (handlers is null)
			{
				handlers = [];
				project.History.Changed += (_, _) => { foreach (Action a in handlers.ToList()) a(); };
			}
			return handlers;
		}
	}

	List<Action> handlers;

	void Edit(string name, Action change)
	{
		using (Transaction.Scope scope = project.History.Begin(name))
		{
			change();
			scope.Commit();
		}

		View.Reconcile();
	}
}
