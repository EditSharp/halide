using EditSharp.Components.Channels;
using EditSharp.Components.Clips;
using EditSharp.History;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EditSharpGUI.Scripts;

// something on the clipboard. whatever was copied is held as a private copy
// taken at copy time, so editing the original afterwards does not change
// what a paste produces, and a paste hands out fresh copies so pasting twice
// never yields the same objects. Text is the plain-text face of the item,
// if it has one - that is what reaches the os clipboard
public abstract class ClipboardItem
{
	public abstract string Kind { get; }

	public virtual string Text => null;
}

public sealed class TextItem(string text) : ClipboardItem
{
	public override string Kind => "text";
	public override string Text { get; } = text;
}

// a single value of any type - a number out of an inspector field, say
public sealed class ValueItem<T>(T value) : ClipboardItem
{
	public T Value { get; } = value;
	public override string Kind => typeof(T).Name;
	public override string Text => Value?.ToString();
}

// a set of clips and how they were laid out relative to each other: each one
// is stored with its offset from the earliest start and the index of its
// channel among channels of its own kind, so a paste can put the whole set
// down somewhere else with the same shape
public sealed class ClipsItem : ClipboardItem
{
	// LinkGroup is the group the original belonged to, so clips linked when
	// copied can be linked again when pasted
	public readonly record struct Entry(Clip Clip, Time Offset, bool Video, int ChannelIndex, Guid? LinkGroup);

	readonly List<Entry> entries;

	public IReadOnlyList<Entry> Entries => entries;
	public override string Kind => "clips";
	public override string Text => string.Join(", ", entries.Select(e => e.Clip.Name));

	// which entry the user had in hand when copying - the clip they last
	// clicked, if it was among them. a paste lines that one up with the
	// channel it is aimed at and the rest keep their places around it. -1
	// when none was
	public int AnchorIndex { get; }

	ClipsItem(List<Entry> entries, int anchorIndex)
	{
		this.entries = entries;
		AnchorIndex = anchorIndex;
	}

	// copies the clips as they are right now
	public static ClipsItem From(IEnumerable<Clip> clips, Clip anchor = null)
	{
		List<Clip> list = [.. clips.Where(c => c.Channel is not null)];
		Time origin = list.Count == 0 ? Time.Zero : list.Min(c => c.Start);

		// copies are construction, not edits - see Transaction's remarks
		using IDisposable _ = Transaction.Suppress();

		return new(
			[.. list.Select(c => new Entry(c.Duplicate(), c.Start - origin, c is VideoClip, c.Channel.Index, c.LinkGroupId))],
			anchor is null ? -1 : list.IndexOf(anchor));
	}

	// fresh copies for one paste, in the same layout
	public List<Entry> Materialize()
	{
		using IDisposable _ = Transaction.Suppress();

		return [.. entries.Select(e => e with { Clip = e.Clip.Duplicate() })];
	}
}

// the app's clipboard. one item at a time, of any kind, with its text form
// mirrored to the os clipboard through DisplayServer so plain text copied
// here can be pasted anywhere else. the other direction is honoured too:
// text copied outside the app since we last wrote wins over what we hold
public sealed class Clipboard
{
	public static Clipboard Shared { get; } = new();

	ClipboardItem item;

	// what we last handed the os, to tell our own text apart from someone else's
	string lastOsText;

	public event EventHandler Changed;

	public bool IsEmpty => item is null && string.IsNullOrEmpty(ExternalText);

	public void Copy(ClipboardItem item)
	{
		this.item = item;

		if (item.Text is string text)
		{
			DisplayServer.ClipboardSet(text);
			lastOsText = text;
		}

		Changed?.Invoke(this, EventArgs.Empty);
	}

	public void CopyText(string text) => Copy(new TextItem(text));

	// the item if it is of the kind asked for. a text request also honours
	// text copied elsewhere since we last wrote to the os clipboard
	public bool TryGet<T>(out T result) where T : ClipboardItem
	{
		if (typeof(T) == typeof(TextItem) && ExternalText is string external)
		{
			result = (T)(ClipboardItem)new TextItem(external);
			return true;
		}

		result = item as T;
		return result is not null;
	}

	// whatever text is current: the outside world's if it has changed, else ours
	public string Text => ExternalText ?? item?.Text;

	// text on the os clipboard that we did not put there
	string ExternalText
	{
		get
		{
			string os = DisplayServer.ClipboardGet();

			return !string.IsNullOrEmpty(os) && os != lastOsText ? os : null;
		}
	}
}
