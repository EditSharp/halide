using EditSharpGUI.Scripts.UI.Dialogs;
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace EditSharpGUI.Api.Extensions;

/// <summary>The extensions in the extensions folder: finding them, asking before a new or changed one runs, and starting and stopping them.</summary>
/// <remarks>
/// Each extension is a folder holding extension.json and its DLLs. Whether each is enabled, and the fingerprint it was
/// trusted with, live in <see cref="TrustFile"/>. A failing extension is stopped and shows its error on the Extensions page.
/// </remarks>
public sealed class ExtensionManager
{
	public const string DefaultFolder = "user://extensions";
	public const string TrustFile = "user://extensions.json";

	readonly List<LoadedExtension> extensions = [];

	internal ExtensionManager() { }

	/// <summary>Where extensions are looked for; a test or <c>--extensions=DIR</c> points it elsewhere.</summary>
	public string Folder { get; set; } = DefaultFolder;

	/// <summary>Where trust decisions are kept; a test points it elsewhere.</summary>
	public string TrustPath { get; set; } = TrustFile;

	/// <summary>Enables new and changed extensions without asking, for scripts and tests (<c>--trust-extensions</c>).</summary>
	public bool TrustAll { get; set; }

	public IReadOnlyList<LoadedExtension> All => extensions;

	/// <summary>An extension started, stopped, failed or was found.</summary>
	public event Action Changed;

	public string FolderPath => ProjectSettings.GlobalizePath(Folder);

	/// <summary>Finds every extension and starts the enabled ones, asking about new or changed ones over <paramref name="owner"/>.</summary>
	public async Task LoadAllAsync(Node owner = null)
	{
		foreach (LoadedExtension extension in extensions.ToList()) Stop(extension);
		extensions.Clear();

		Directory.CreateDirectory(FolderPath);
		JsonObject trust = ReadTrust();

		foreach (string folder in Directory.GetDirectories(FolderPath).OrderBy(f => f))
		{
			LoadedExtension extension = Inspect(folder);
			extensions.Add(extension);
			if (extension.State is ExtensionState.Broken or ExtensionState.Incompatible) continue;

			JsonObject known = trust[extension.Id] as JsonObject;
			bool trusted = known is not null && (string)known["hash"] == extension.Hash;
			bool enabled = trusted && (bool?)known["enabled"] == true;

			if (!trusted)
			{
				enabled = TrustAll || await AskAsync(extension, owner);
				Remember(extension, enabled);
			}

			if (enabled) Start(extension);
			else extension.State = trusted ? ExtensionState.Disabled : ExtensionState.Untrusted;
		}

		Changed?.Invoke();
	}

	/// <summary>Turns an extension on or off, remembering the choice.</summary>
	public void SetEnabled(LoadedExtension extension, bool enabled)
	{
		if (extension.State is ExtensionState.Broken or ExtensionState.Incompatible) return;

		Remember(extension, enabled);
		if (enabled) Start(extension);
		else
		{
			Stop(extension);
			extension.State = ExtensionState.Disabled;
		}

		Changed?.Invoke();
	}

	/// <summary>Stops an extension, reads its folder again and starts it again if it was running, asking if it changed.</summary>
	public async Task ReloadAsync(LoadedExtension extension, Node owner = null)
	{
		bool wasOn = extension.State == ExtensionState.Enabled;
		Stop(extension);

		LoadedExtension fresh = Inspect(extension.Folder);
		extensions[extensions.IndexOf(extension)] = fresh;

		if (fresh.State is not (ExtensionState.Broken or ExtensionState.Incompatible))
		{
			bool changed = fresh.Hash != extension.Hash;
			bool on = changed ? TrustAll || await AskAsync(fresh, owner) : wasOn || extension.State == ExtensionState.Failed;
			Remember(fresh, on);
			if (on) Start(fresh);
			else fresh.State = ExtensionState.Disabled;
		}

		Changed?.Invoke();
	}

	/// <summary>Stops every running extension, as the app quits.</summary>
	public void StopAll()
	{
		foreach (LoadedExtension extension in extensions) Stop(extension);
	}

	// ---- one extension ----

	static LoadedExtension Inspect(string folder)
	{
		LoadedExtension extension = new(folder);

		try
		{
			extension.Manifest = ExtensionManifest.Load(folder);
			if (!File.Exists(Path.Combine(folder, extension.Manifest.Assembly))) throw new InvalidDataException($"'{extension.Manifest.Assembly}' isn't in the folder.");
			extension.Hash = Fingerprint(folder);

			if (!extension.Manifest.Compatible(out string reason))
			{
				extension.State = ExtensionState.Incompatible;
				extension.Error = reason;
			}
		}
		catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
		{
			extension.State = ExtensionState.Broken;
			extension.Error = e.Message;
		}

		return extension;
	}

	void Start(LoadedExtension extension)
	{
		if (extension.State == ExtensionState.Enabled) return;

		try
		{
			string assembly = Path.Combine(extension.Folder, extension.Manifest.Assembly);
			extension.LoadContext = new ExtensionLoadContext(assembly);
			Type entry = extension.LoadContext.LoadFromAssemblyPath(assembly).GetType(extension.Manifest.Entry, throwOnError: true);
			if (!typeof(IExtension).IsAssignableFrom(entry)) throw new InvalidDataException($"'{extension.Manifest.Entry}' doesn't implement IExtension.");

			extension.Instance = (IExtension)Activator.CreateInstance(entry);
			extension.Context = new ExtensionContext(extension.Manifest, extension.Folder, e => Fail(extension, e));
			extension.State = ExtensionState.Enabled;
			extension.Error = null;
			extension.Instance.Activate(extension.Context);
			GD.Print($"Extension '{extension.Id}' {extension.Manifest.Version} started");
		}
		catch (Exception e)
		{
			Fail(extension, e is System.Reflection.TargetInvocationException { InnerException: Exception inner } ? inner : e);
		}
	}

	void Stop(LoadedExtension extension)
	{
		if (extension.Instance is not null)
		{
			try { extension.Instance.Deactivate(); }
			catch (Exception e) { GD.PushWarning($"Extension '{extension.Id}' failed to stop cleanly: {e.Message}"); }
		}

		extension.Context?.RemoveAll();
		extension.Context = null;
		extension.Instance = null;
		extension.LoadContext?.Unload();
		extension.LoadContext = null;
	}

	// something the extension did threw: it stops, and says why
	void Fail(LoadedExtension extension, Exception e)
	{
		GD.PushError($"Extension '{extension.Id}' failed: {e}");
		if (extension.State == ExtensionState.Failed) return;

		Callable.From(() =>
		{
			Stop(extension);
			extension.State = ExtensionState.Failed;
			extension.Error = $"{e.GetType().Name}: {e.Message}";
			Changed?.Invoke();
		}).CallDeferred();
		extension.State = ExtensionState.Failed;
	}

	async Task<bool> AskAsync(LoadedExtension extension, Node owner)
	{
		Dialog dialog = Dialogs.Question("Enable Extension",
			$"Enable \"{extension.Name}\"{(extension.Manifest.Version is string v ? $" {v}" : "")}?",
			(extension.Manifest.Description is string d ? d + "\n\n" : "") + "Extensions can read and change your projects and files. Only enable extensions you trust.",
			("enable", "Enable", DialogButtonRole.Default), ("disable", "Keep Disabled", DialogButtonRole.Cancel));
		return (await Dialogs.Show(dialog, owner ?? ((SceneTree)Engine.GetMainLoop()).Root)).Is("enable");
	}

	// ---- trust ----

	JsonObject ReadTrust()
	{
		try
		{
			string file = ProjectSettings.GlobalizePath(TrustPath);
			return File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? [] : [];
		}
		catch (Exception e) when (e is IOException or System.Text.Json.JsonException)
		{
			return [];
		}
	}

	void Remember(LoadedExtension extension, bool enabled)
	{
		JsonObject trust = ReadTrust();
		trust[extension.Id] = new JsonObject { ["hash"] = extension.Hash, ["enabled"] = enabled };

		try
		{
			string file = ProjectSettings.GlobalizePath(TrustPath);
			Directory.CreateDirectory(Path.GetDirectoryName(file)!);
			File.WriteAllText(file, trust.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
		}
		catch (IOException e)
		{
			GD.PushWarning($"Could not save extension choices: {e.Message}");
		}
	}

	// the manifest and every assembly, so any change to what runs asks again
	static string Fingerprint(string folder)
	{
		using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
			.Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f) == ExtensionManifest.FileName)
			.OrderBy(f => f, StringComparer.Ordinal))
		{
			hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(folder, file)));
			hash.AppendData(File.ReadAllBytes(file));
		}
		return Convert.ToHexString(hash.GetHashAndReset());
	}
}
