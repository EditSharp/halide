using EditSharp.Components;
using EditSharp.Components.Clips;
using EditSharp.Components.Media;
using EditSharp.Components.Nodes.Input;
using EditSharp.History;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// the project's media: every file the user has brought in, shared by
// whichever nodes read it. a minimal bin - a list with history - until
// there is a view for it. a video media's audio is part of it, not an
// entry of its own, but is offered wherever audio is asked for. Changed
// fires on every add and remove, undo and redo included
public sealed class MediaLibrary : IReadOnlyList<IMedia>
{
	readonly List<IMedia> items = [];

	public event Action Changed;

	public int Count => items.Count;

	public IMedia this[int index] => items[index];

	public IEnumerator<IMedia> GetEnumerator() => items.GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	// the entries and what they hold: each video media's audio after it
	IEnumerable<IMedia> All => items.SelectMany(m => m is VideoMedia { Audio: { } audio } ? new IMedia[] { m, audio } : [m]);

	// whether a media is here, as an entry or inside one
	public bool Holds(IMedia media) => All.Contains(media);

	// the media a property of this type can hold: VideoMedia for a video
	// node's Media, AudioMedia for an audio node's - a video's own audio
	// among them
	public IReadOnlyList<IMedia> For(Type type) => [.. All.Where(type.IsInstanceOfType)];

	public void Add(IMedia media)
	{
		if (media is null || Holds(media)) return;

		Transaction.Apply(
			() => { items.Add(media); Changed?.Invoke(); },
			() => { items.Remove(media); Changed?.Invoke(); },
			"add media");
	}

	public void Remove(IMedia media)
	{
		int index = items.IndexOf(media);
		if (index < 0) return;

		Transaction.Apply(
			() => { items.Remove(media); Changed?.Invoke(); },
			() => { items.Insert(Math.Min(index, items.Count), media); Changed?.Invoke(); },
			"remove media");
	}

	// everything the timeline's clips already read, for a project built
	// in code rather than from a file: the video media first, so their
	// audio is found inside them rather than added again. nothing is recorded
	public void AdoptFrom(Timeline timeline)
	{
		using IDisposable _ = Transaction.Suppress();

		List<IMedia> read = [];

		foreach (Clip clip in timeline.Channels.SelectMany(c => c.Clips))
		{
			foreach (EditSharp.Components.Nodes.Node node in clip.Graph.AllNodes)
			{
				IMedia media = node switch
				{
					VideoMediaNode video => video.Media,
					AudioMediaNode audio => audio.Media,
					_ => null
				};

				if (media is not null) read.Add(media);
			}
		}

		foreach (IMedia media in read.OrderBy(m => m is VideoMedia ? 0 : 1)) Add(media);
	}
}
