using EditSharpGUI.Api;
using EditSharpGUI.Scripts.App.Layouts;
using EditSharpGUI.Scripts.Input;
using EditSharpGUI.Scripts.UI.Theming;
using Godot;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace EditSharpGUI.Tests;

// app settings, the user-data redirect, and the App Settings window with pages extensions add
[TestFixture]
public sealed class SettingsTests
{
	[Test]
	public void SettingsSaveAndLoad()
	{
		AppSettings.Current.AutosaveSeconds = 77;
		AppSettings.Current.InterfaceScale = InterfaceScale.Percent125;
		AppSettings.Current.Save();
		AppSettings loaded = AppSettings.Load();
		Assert.Equal(77, loaded.AutosaveSeconds);
		Assert.Equal(InterfaceScale.Percent125, loaded.InterfaceScale);
	}

	[Test]
	public void UserDataKeepsEverythingInItsFolder()
	{
		string folder = UserData.Folder;
		Assert.NotNull(folder, "the runner redirected user data");
		Assert.True(Path.GetFullPath(ProjectSettings.GlobalizePath(LayoutStore.FilePath)).StartsWith(folder));
		Assert.True(Path.GetFullPath(ShortcutMap.FilePath).StartsWith(folder));
		Assert.True(Path.GetFullPath(EditSharpTheme.SettingsFile).StartsWith(folder));
		Assert.True(Path.GetFullPath(TestApp.App.Extensions.TrustPath).StartsWith(folder));
		Assert.True(Path.GetFullPath(Api.Remote.RpcServer.AddressFile).StartsWith(folder));
	}

	[Test]
	public void TheInterfaceScaleIsHonoured()
	{
		AppSettings.Current.InterfaceScale = InterfaceScale.Percent150;
		Assert.Near(1.5, AppSettings.Current.ScaleFor(null), 1e-6);
		AppSettings.Current.InterfaceScale = InterfaceScale.System;
		Assert.True(AppSettings.Current.ScaleFor(null) > 0);
	}

	[Test]
	public async Task TheSettingsWindowListsEveryPageAndExtensionPages()
	{
		TestPage page = new();
		using Registration added = TestApp.App.SettingsPages.Register("Test Page", page);
		ProjectManager.Singleton.ShowSettings();
		await TestApp.Frames(3);

		Window window = TestApp.Tree.Root.GetChildren().OfType<SettingsWindow>().Single();
		string[] sections = [.. window.FindChildren("*", "Button", true, false).OfType<Button>().Where(b => b.ThemeTypeVariation == "SettingsSection").Select(b => b.Text)];
		Assert.Sequence(["Appearance", "Keyboard Shortcuts", "Media & Cache", "Projects & Autosave", "Test Page", "Extensions"], sections);
	}

	[Test]
	public async Task OpeningSettingsTwiceBringsTheSameWindow()
	{
		ProjectManager.Singleton.ShowSettings();
		await TestApp.Frames(2);
		ProjectManager.Singleton.ShowSettings();
		await TestApp.Frames(2);
		Assert.Count(1, TestApp.Tree.Root.GetChildren().OfType<SettingsWindow>().Where(w => !w.IsQueuedForDeletion()));
	}

	public sealed class TestPage
	{
		[EditSharp.Editing.Editable]
		public string Name { get; set; } = "x";
	}
}
