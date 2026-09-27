using EditSharp.Components.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Halide.Api;

/// <summary>Media importers extensions add: for files of certain extensions, what media to make of them.</summary>
public sealed class ImporterRegistry
{
	sealed record Importer(HashSet<string> Extensions, Func<string, IMedia> Create);

	readonly List<Importer> importers = [];

	/// <summary>Makes media from files ending in <paramref name="extensions"/> (like ".cube") until disposed; the latest added wins.</summary>
	/// <param name="create">Makes the media for a path; returning null leaves the file to the built-in importer.</param>
	public Registration Register(IEnumerable<string> extensions, Func<string, IMedia> create)
	{
		Importer importer = new([.. extensions.Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())], create);
		importers.Add(importer);
		return new Registration(() => importers.Remove(importer));
	}

	/// <summary>The media an extension makes of a file, or null when none takes it.</summary>
	public IMedia Create(string path)
	{
		string extension = Path.GetExtension(path).ToLowerInvariant();

		for (int i = importers.Count - 1; i >= 0; i--)
		{
			if (!importers[i].Extensions.Contains(extension)) continue;
			try
			{
				if (importers[i].Create(path) is IMedia media) return media;
			}
			catch (Exception e)
			{
				Godot.GD.PushWarning($"An importer failed on '{path}': {e.Message}");
			}
		}

		return null;
	}
}
