using Halide.Api;
using Halide.Api.Extensions;
using Halide.Scripts.App.Commands;
using Halide.Scripts.Input;
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Halide.Tests;

// extensions: manifests, compatibility, trust, what a context adds and takes back, and the sample loaded for real
[TestFixture]
public sealed class ExtensionTests
{
	// the sample extension's build output, when it's been built (CI builds it first)
	static string SampleBuild => Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "extensions", "Sample", "bin", "Debug", "net10.0"));

	string folder;
	ExtensionManager Manager => TestApp.App.Extensions;

	[SetUp]
	public void Fresh()
	{
		folder = Path.Combine(TestApp.Sandbox, $"extensions-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		Manager.Folder = folder;
		Manager.TrustPath = Path.Combine(folder, "trust.json");
		Manager.TrustAll = false;
	}

	[TearDown]
	public void StopEverything()
	{
		Manager.StopAll();
		Manager.TrustAll = false;
	}

	string Write(string id, string manifest)
	{
		string dir = Path.Combine(folder, id);
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, ExtensionManifest.FileName), manifest);
		return dir;
	}

	static string Manifest(string id, string api = "1.0", string assembly = "Missing.dll") =>
		$"{{\"id\":\"{id}\",\"name\":\"{id}\",\"version\":\"1.0\",\"assembly\":\"{assembly}\",\"entry\":\"X.Y\",\"api\":\"{api}\"}}";

	string InstallSample()
	{
		if (!File.Exists(Path.Combine(SampleBuild, "SampleExtension.dll"))) Assert.Skip("the sample extension isn't built (dotnet build extensions/Sample)");
		string dir = Path.Combine(folder, "editsharp.sample");
		Directory.CreateDirectory(dir);
		foreach (string file in new[] { "SampleExtension.dll", "extension.json" }) File.Copy(Path.Combine(SampleBuild, file), Path.Combine(dir, file));
		return dir;
	}

	[Test]
	public void ManifestsNeedTheirFields()
	{
		string dir = Write("bad", "{\"id\":\"bad\"}");
		Assert.Throws<InvalidDataException>(() => ExtensionManifest.Load(dir));
		Assert.Throws<InvalidDataException>(() => ExtensionManifest.Load(Write("junk", "{ nope")));
		ExtensionManifest ok = ExtensionManifest.Load(Write("ok", "{\"id\":\"ok\",\"assembly\":\"a.dll\",\"entry\":\"A\",\"api\":\"1.0\"}"));
		Assert.Equal("ok", ok.Name, "the id stands in for a missing name");
	}

	[Test]
	public void CompatibilityFollowsTheApiVersion()
	{
		ExtensionManifest m(string api) => new("x", "x", "1", "x.dll", "X", api);
		Assert.True(m("1.0").Compatible(out _));
		Assert.True(m("1").Compatible(out _), "a bare major");
		Assert.False(m("2.0").Compatible(out _), "another major");
		Assert.False(m("1.9").Compatible(out string newer), "a newer minor");
		Assert.True(newer.Contains("Update"), "says what to do");
		Assert.False(m("banana").Compatible(out _));
	}

	[Test]
	public async Task BrokenAndIncompatibleExtensionsAreReportedNotRun()
	{
		Write("broken", "{ nope");
		Write("future", Manifest("future", api: "2.0", assembly: "extension.json"));
		Write("missing", Manifest("missing"));
		await Manager.LoadAllAsync();

		Assert.Equal(ExtensionState.Broken, Manager.All.Single(e => e.Folder.EndsWith("broken")).State);
		Assert.Equal(ExtensionState.Incompatible, Manager.All.Single(e => e.Id == "future").State);
		LoadedExtension missing = Manager.All.Single(e => e.Id == "missing");
		Assert.Equal(ExtensionState.Broken, missing.State);
		Assert.True(missing.Error.Contains("Missing.dll"), "the error names the file");
	}

	[Test]
	public async Task NewExtensionsWaitForTrust()
	{
		InstallSample();
		Scripts.UI.Dialogs.Dialogs.Answering = d => d.Cancel?.Id;
		await Manager.LoadAllAsync();
		LoadedExtension sample = Manager.All.Single();
		Assert.Equal(ExtensionState.Untrusted, sample.State);
		Assert.Null(Commands.Get("editsharp.sample.hello"), "it didn't run");
	}

	[Test]
	public async Task EnablingRunsItAndDisablingTakesEverythingBack()
	{
		InstallSample();
		Manager.TrustAll = true;
		await Manager.LoadAllAsync();
		LoadedExtension sample = Manager.All.Single();
		Assert.Equal(ExtensionState.Enabled, sample.State, sample.Error);

		Assert.NotNull(Commands.Get("editsharp.sample.hello"));
		Assert.Contains("editsharp.sample.notes", TestApp.App.Views.All.Select(v => v.Id));
		Assert.Contains("Sample", TestApp.App.SettingsPages.Pages.Select(p => p.Title));
		Assert.True(TestApp.App.Menus.In("Edit").Any(i => i.Command == "editsharp.sample.hello"));
		Assert.True(Shortcuts.Extra.Any(e => e.Action == "editsharp.sample.hello"));
		Assert.NotNull(TestApp.App.Importers.Create("x.samplewav"));

		Manager.SetEnabled(sample, false);
		Assert.Equal(ExtensionState.Disabled, sample.State);
		Assert.Null(Commands.Get("editsharp.sample.hello"));
		Assert.DoesNotContain("editsharp.sample.notes", TestApp.App.Views.All.Select(v => v.Id));
		Assert.DoesNotContain("Sample", TestApp.App.SettingsPages.Pages.Select(p => p.Title));
		Assert.False(TestApp.App.Menus.In("Edit").Any(i => i.Command == "editsharp.sample.hello"));
		Assert.False(Shortcuts.Extra.Any(e => e.Action == "editsharp.sample.hello"));
		Assert.Null(TestApp.App.Importers.Create("x.samplewav"));
	}

	[Test]
	public async Task TheChoiceIsRememberedUntilTheExtensionChanges()
	{
		string dir = InstallSample();
		Manager.TrustAll = true;
		await Manager.LoadAllAsync();
		Manager.SetEnabled(Manager.All.Single(), false);

		Manager.TrustAll = false;
		await Manager.LoadAllAsync();
		Assert.Equal(ExtensionState.Disabled, Manager.All.Single().State, "stays off without asking");

		Manager.SetEnabled(Manager.All.Single(), true);
		await Manager.LoadAllAsync();
		Assert.Equal(ExtensionState.Enabled, Manager.All.Single().State, "stays on");

		File.AppendAllText(Path.Combine(dir, "extension.json"), " ");
		Scripts.UI.Dialogs.Dialogs.Answering = d => d.Cancel?.Id;
		await Manager.LoadAllAsync();
		Assert.Equal(ExtensionState.Untrusted, Manager.All.Single().State, "a changed extension asks again");
	}

	[Test]
	public async Task ItsCommandRunsAndItsViewOpens()
	{
		InstallSample();
		Manager.TrustAll = true;
		await Manager.LoadAllAsync();
		ProjectHandle project = await TestApp.NewProjectAsync();

		string greeting = (string)TestApp.App.Commands.Run("editsharp.sample.hello", project);
		Assert.True(greeting.StartsWith("Hello"), greeting);

		project.Layout.Open("editsharp.sample.notes");
		Assert.True(project.Layout.IsOpen("editsharp.sample.notes"));
	}

	[Test]
	public async Task ReloadingRestartsIt()
	{
		InstallSample();
		Manager.TrustAll = true;
		await Manager.LoadAllAsync();
		await Manager.ReloadAsync(Manager.All.Single());
		Assert.Equal(ExtensionState.Enabled, Manager.All.Single().State);
		Assert.NotNull(Commands.Get("editsharp.sample.hello"));
	}

	[Test]
	public void AContextTakesBackEverythingItAdded()
	{
		ExtensionContext context = new(new ExtensionManifest("test.ctx", "Test", "1", "x.dll", "X", "1.0"), folder, _ => { });
		context.AddCommand(new Command { Id = "test.ctx.cmd", Title = "Ctx", Run = (_, _) => null });
		context.AddMenuItem("View", "test.ctx.cmd");
		context.AddView(new ViewDefinition("test.ctx.view", "Ctx View", _ => new Label()));
		context.AddSettingsPage("Ctx", new object());
		context.AddShortcut("test.ctx.cmd", new KeyCombo(Key.F9, Control: true, Shift: true, Alt: true));
		bool tracked = false;
		context.Track(new Registration(() => tracked = true));

		context.RemoveAll();
		Assert.Null(Commands.Get("test.ctx.cmd"));
		Assert.Count(0, TestApp.App.Menus.In("View").Where(i => i.Command == "test.ctx.cmd"));
		Assert.DoesNotContain("test.ctx.view", TestApp.App.Views.All.Select(v => v.Id));
		Assert.DoesNotContain("Ctx", TestApp.App.SettingsPages.Pages.Select(p => p.Title));
		Assert.Count(0, InputManager.Singleton.Keyboard.Shortcuts.Get("test.ctx.cmd"));
		Assert.True(tracked);
	}

	[Test]
	public void AFailingCommandReportsTheExtensionAndDoesntCrash()
	{
		Exception reported = null;
		ExtensionContext context = new(new ExtensionManifest("test.fail", "Fail", "1", "x.dll", "X", "1.0"), folder, e => reported = e);
		context.AddCommand(new Command { Id = "test.fail.cmd", Title = "Fail", CanRun = _ => throw new InvalidOperationException("can't tell"), Run = (_, _) => throw new InvalidOperationException("broke") });

		Assert.False(Commands.Get("test.fail.cmd").EnabledIn(new CommandContext()), "a throwing check counts as disabled");
		Assert.NotNull(reported);
		reported = null;
		Assert.False(Commands.Run("test.fail.cmd", new CommandContext()));
		context.RemoveAll();
	}

	[Test]
	public void AFailingViewOrImporterIsCaught()
	{
		int failures = 0;
		ExtensionContext context = new(new ExtensionManifest("test.guard", "Guard", "1", "x.dll", "X", "1.0"), folder, _ => failures++);
		context.AddImporter([".guard"], _ => throw new InvalidOperationException("no"));
		Assert.Null(TestApp.App.Importers.Create("a.guard"));
		Assert.Equal(1, failures);
		context.RemoveAll();
	}
}
