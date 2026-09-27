using Godot;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace EditSharpGUI.Tests;

// every scene loads and builds, and the self-contained ones come up in a window and go away cleanly
[TestFixture]
public sealed class SceneTests
{
	static string[] Scenes() =>
	[
		.. Directory.GetFiles(ProjectSettings.GlobalizePath("res://Scenes"), "*.tscn", SearchOption.AllDirectories)
			.Select(f => ProjectSettings.LocalizePath(f.Replace('\\', '/')))
			.OrderBy(f => f),
	];

	[Test]
	public void EverySceneLoads()
	{
		string[] scenes = Scenes();
		Assert.True(scenes.Length > 40, "the scenes were found");
		foreach (string scene in scenes)
		{
			PackedScene packed = GD.Load<PackedScene>(scene);
			Assert.NotNull(packed, $"{scene} loads");
			Assert.True(packed.CanInstantiate(), $"{scene} can be built");
		}
	}

	[Test]
	public void EverySceneInstantiates()
	{
		foreach (string scene in Scenes())
		{
			Node node = GD.Load<PackedScene>(scene).Instantiate();
			Assert.NotNull(node, scene);
			node.Free();
		}
	}

	[Test]
	public async Task StandaloneScenesEnterTheTree()
	{
		// the scenes that need nothing from a project or window around them
		string[] standalone =
		[
			"res://Scenes/Docking/DockCompass.tscn",
			"res://Scenes/Docking/DockPane.tscn",
			"res://Scenes/Docking/DockArea.tscn",
			"res://Scenes/Docking/DockTabGhost.tscn",
			"res://Scenes/Components/TopBar.tscn",
			"res://Scenes/App/WindowFrame.tscn",
			"res://Scenes/Settings/ExtensionsPage.tscn",
			"res://Scenes/Settings/AppSettings.tscn",
		];

		Window host = new() { Size = new Vector2I(800, 600), Visible = false };
		TestApp.Tree.Root.AddChild(host);
		try
		{
			foreach (string scene in standalone)
			{
				Node node = GD.Load<PackedScene>(scene).Instantiate();
				host.AddChild(node);
				await TestApp.Frames(2);
				Assert.True(node.IsInsideTree(), scene);
				node.QueueFree();
				await TestApp.Frames(1);
			}
		}
		finally { host.QueueFree(); }
	}
}
