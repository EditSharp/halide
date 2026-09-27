namespace EditSharpGUI.Api.Extensions;

/// <summary>One extension the app found: its folder, manifest, state, and what went wrong if anything did.</summary>
public sealed class LoadedExtension
{
	internal LoadedExtension(string folder) => Folder = folder;

	public string Folder { get; }

	/// <summary>Its manifest; null when it couldn't be read.</summary>
	public ExtensionManifest Manifest { get; internal set; }

	public string Id => Manifest?.Id ?? System.IO.Path.GetFileName(Folder);

	public string Name => Manifest?.Name ?? Id;

	public ExtensionState State { get; internal set; } = ExtensionState.Disabled;

	/// <summary>Why it's failed, broken or incompatible.</summary>
	public string Error { get; internal set; }

	/// <summary>A fingerprint of its manifest and assemblies; a change asks for trust again.</summary>
	internal string Hash { get; set; }

	internal ExtensionLoadContext LoadContext { get; set; }
	internal IExtension Instance { get; set; }
	internal ExtensionContext Context { get; set; }
}
