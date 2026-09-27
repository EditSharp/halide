using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EditSharpGUI.Api.Extensions;

/// <summary>An extension's extension.json: who it is and where its entry point lives.</summary>
/// <param name="Id">Unique, like <c>com.example.scopes</c>.</param>
/// <param name="Name">What the Extensions page calls it.</param>
/// <param name="Version">The extension's own version.</param>
/// <param name="Assembly">The DLL holding the entry point, beside the manifest.</param>
/// <param name="Entry">The full name of the type implementing <see cref="IExtension"/>.</param>
/// <param name="Api">The API version it was built against, like "1.0".</param>
/// <param name="Description">A line about what it does.</param>
public sealed record ExtensionManifest(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("version")] string Version,
	[property: JsonPropertyName("assembly")] string Assembly,
	[property: JsonPropertyName("entry")] string Entry,
	[property: JsonPropertyName("api")] string Api,
	[property: JsonPropertyName("description")] string Description = null)
{
	public const string FileName = "extension.json";

	/// <summary>The manifest in a folder.</summary>
	/// <exception cref="InvalidDataException">It's missing a required field or isn't valid JSON.</exception>
	public static ExtensionManifest Load(string folder)
	{
		ExtensionManifest manifest;
		try
		{
			manifest = JsonSerializer.Deserialize<ExtensionManifest>(File.ReadAllText(Path.Combine(folder, FileName)));
		}
		catch (JsonException e)
		{
			throw new InvalidDataException($"{FileName} isn't valid: {e.Message}");
		}

		if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Assembly) || string.IsNullOrWhiteSpace(manifest.Entry) || string.IsNullOrWhiteSpace(manifest.Api))
			throw new InvalidDataException($"{FileName} needs id, assembly, entry and api.");

		return manifest with { Name = string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Id : manifest.Name };
	}

	/// <summary>Whether the app's API can run it: the same major version, and a minor no newer than the app's.</summary>
	public bool Compatible(out string reason)
	{
		reason = null;
		if (!System.Version.TryParse(Api.Contains('.') ? Api : Api + ".0", out System.Version wanted)) reason = $"Its API version '{Api}' isn't a version.";
		else
		{
			System.Version have = System.Version.Parse(EditSharpApp.Version);
			if (wanted.Major != have.Major) reason = $"It was built for API {Api}; this app has {EditSharpApp.Version}.";
			else if (wanted.Minor > have.Minor) reason = $"It needs API {Api}; this app has {EditSharpApp.Version}. Update EditSharp.";
		}
		return reason is null;
	}
}
