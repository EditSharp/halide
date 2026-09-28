using EditSharp.Components;
using EditSharp.Components.Channels;
using EditSharp.Components.Clips;
using EditSharp.History;
using Halide.Scripts;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

internal sealed class ClipSelectionCommands(UIClipsView view)
{
	(bool video, int index)? pasteTarget;
	UIClip lastClicked;

	public void MarkTarget(UIClip clip)
	{
		lastClicked = clip;

		if (clip.Clip.Channel is Channel channel) pasteTarget = (channel is VideoChannel, channel.Index);
	}

	public void MarkTarget((UIClipsView.ChannelType type, int index, bool exists) at)
	{
		lastClicked = null;

		if (at.exists) pasteTarget = (at.type == UIClipsView.ChannelType.Video, at.index);
	}

	public void CopySelection()
	{
		if (view.Selection.Count == 0) return;

		Clip anchor = lastClicked is not null && view.Selection.Contains(lastClicked) ? lastClicked.Clip : null;
		Clipboard.Shared.Copy(ClipsItem.From(view.Selection.Select(s => s.Clip), anchor));
	}

	public void DuplicateSelection()
	{
		if (view.Selection.Count == 0) return;

		List<ClipsItem.Entry> entries = ClipsItem.From(view.Selection.Select(s => s.Clip)).Materialize();
		Time at = view.Selection.Max(s => s.Clip.End);
		List<Clip> made = [];

		using (Transaction.Scope change = view.UITimeline.History.Begin(entries.Count == 1 ? "Duplicate clip" : $"Duplicate {entries.Count} clips"))
		{
			foreach (ClipsItem.Entry entry in entries)
			{
				entry.Clip.Start = at + entry.Offset;
				view.EnsureChannel(entry.Video, entry.ChannelIndex).AddClip(entry.Clip);
				made.Add(entry.Clip);
			}

			LinkCopiedGroups(entries);
			change.Commit();
		}

		view.Reconcile();
		view.SelectClips(made);
	}

	public void CutSelection()
	{
		if (view.Selection.Count == 0) return;

		CopySelection();
		DeleteSelection(UIClipsView.RippleScope.None, "Cut");
	}

	public void Paste()
	{
		if (!Clipboard.Shared.TryGet(out ClipsItem item) || item.Entries.Count == 0) return;

		List<ClipsItem.Entry> entries = item.Materialize();
		Time at = view.UITimeline.PlayheadTime;
		RebaseChannels(entries, item);
		List<Clip> pasted = [];

		using (Transaction.Scope change = view.UITimeline.History.Begin(entries.Count == 1 ? "Paste clip" : $"Paste {entries.Count} clips"))
		{
			foreach (ClipsItem.Entry entry in entries)
			{
				entry.Clip.Start = at + entry.Offset;
				view.EnsureChannel(entry.Video, entry.ChannelIndex).AddClip(entry.Clip);
				pasted.Add(entry.Clip);
			}

			LinkCopiedGroups(entries);
			change.Commit();
		}

		view.Reconcile();
		view.SelectClips(pasted);
	}

	void RebaseChannels(List<ClipsItem.Entry> entries, ClipsItem item)
	{
		if (pasteTarget is not (bool video, int index)) return;

		int videoCount = view.UITimeline.Timeline.VideoChannels.Count;
		int Row(bool isVideo, int channelIndex) => isVideo ? videoCount - 1 - channelIndex : videoCount + channelIndex;

		ClipsItem.Entry? anchor = item.AnchorIndex >= 0 ? entries[item.AnchorIndex] : null;
		List<ClipsItem.Entry> ofKind = [.. entries.Where(e => e.Video == video)];
		int? fromRow = anchor is ClipsItem.Entry held ? Row(held.Video, held.ChannelIndex)
			: ofKind.Count > 0 ? Row(video, ofKind.Min(e => e.ChannelIndex))
			: null;
		if (fromRow is not int from) return;

		int rows = Row(video, index) - from;
		int videoShift = -rows;
		int audioShift = rows;
		List<ClipsItem.Entry> videos = [.. entries.Where(e => e.Video)];
		List<ClipsItem.Entry> audios = [.. entries.Where(e => !e.Video)];

		if (videos.Count > 0) videoShift = Mathf.Max(videoShift, -videos.Min(e => e.ChannelIndex));
		if (audios.Count > 0) audioShift = Mathf.Max(audioShift, -audios.Min(e => e.ChannelIndex));

		for (int i = 0; i < entries.Count; i++)
		{
			ClipsItem.Entry entry = entries[i];
			entries[i] = entry with { ChannelIndex = entry.ChannelIndex + (entry.Video ? videoShift : audioShift) };
		}
	}

	void LinkCopiedGroups(IEnumerable<ClipsItem.Entry> entries)
	{
		foreach (IGrouping<Guid?, ClipsItem.Entry> group in entries.Where(e => e.LinkGroup is not null).GroupBy(e => e.LinkGroup))
			if (group.Count() > 1) view.UITimeline.Timeline.Link(group.Select(e => e.Clip));
	}

	public void SplitAtPlayhead(bool everything)
	{
		Time at = view.UITimeline.PlayheadTime;
		Timeline timeline = view.UITimeline.Timeline;
		IEnumerable<Clip> candidates = everything || view.Selection.Count == 0
			? timeline.Channels.SelectMany(c => c.Clips)
			: view.Selection.Select(s => s.Clip);

		List<Clip> spanning = [.. candidates.Where(c => c.Start < at && c.End > at).Distinct()];
		if (spanning.Count == 0) return;

		HashSet<Clip> before = [.. timeline.Channels.SelectMany(c => c.Clips)];
		List<Clip> selected = [.. view.Selection.Select(s => s.Clip)];
		List<(Channel channel, Time start, Time end)> selectedSpans = [.. selected.Select(c => (c.Channel, c.Start, c.End))];

		using (Transaction.Scope change = view.UITimeline.History.Begin(spanning.Count == 1 ? "Split clip" : $"Split {spanning.Count} clips"))
		{
			HashSet<Guid> groups = [];
			foreach (Clip clip in spanning)
			{
				if (clip.LinkGroupId is Guid id)
				{
					if (groups.Add(id)) timeline.GetLinkGroup(id)?.Split(at);
				}
				else clip.Split(at);
			}

			change.Commit();
		}

		view.Reconcile();
		IEnumerable<Clip> pieces = timeline.Channels.SelectMany(c => c.Clips)
			.Where(c => !before.Contains(c))
			.Where(c => selectedSpans.Any(s => ReferenceEquals(s.channel, c.Channel) && c.Start >= s.start && c.End <= s.end));
		view.SelectClips(selected.Where(c => c.Channel is not null).Concat(pieces));
	}

	public void DeleteSelection(UIClipsView.RippleScope ripple, string description = null)
	{
		if (view.Selection.Count == 0) return;

		List<Clip> clips = [.. view.Selection.Select(s => s.Clip)];
		Timeline timeline = view.UITimeline.Timeline;
		description ??= ripple == UIClipsView.RippleScope.None ? "Delete" : "Ripple delete";

		using (Transaction.Scope change = view.UITimeline.History.Begin(clips.Count == 1 ? $"{description} clip" : $"{description} {clips.Count} clips"))
		{
			switch (ripple)
			{
				case UIClipsView.RippleScope.None:
					foreach (Clip clip in clips) clip.Delete();
					break;
				case UIClipsView.RippleScope.OwnChannels:
					foreach (Clip clip in clips.OrderByDescending(c => c.Start)) clip.RippleDelete();
					break;
				case UIClipsView.RippleScope.AllChannels:
					foreach ((Time start, Time end) in MergeRanges(clips).OrderByDescending(r => r.start))
						timeline.RippleRemoveRange(start, end);
					break;
			}

			change.Commit();
		}

		view.DeselectAll();
		view.Reconcile();
	}

	static List<(Time start, Time end)> MergeRanges(IEnumerable<Clip> clips)
	{
		List<(Time start, Time end)> merged = [];
		foreach (Clip clip in clips.OrderBy(c => c.Start))
		{
			if (merged.Count > 0 && clip.Start <= merged[^1].end)
				merged[^1] = (merged[^1].start, clip.End > merged[^1].end ? clip.End : merged[^1].end);
			else
				merged.Add((clip.Start, clip.End));
		}

		return merged;
	}
}
