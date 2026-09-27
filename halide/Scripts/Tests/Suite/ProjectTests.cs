using Halide.Api;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Halide.Tests;

// opening, creating, saving and closing projects, and what the app remembers of them
[TestFixture]
public sealed class ProjectTests
{
	[Test]
	public async Task CreatingAProjectOpensItClean()
	{
		ProjectHandle project = await TestApp.NewProjectAsync("Fresh");
		Assert.Equal("Fresh", project.Name);
		Assert.True(File.Exists(project.FilePath), "its file is written");
		Assert.False(project.Dirty, "nothing unsaved yet");
		Assert.Contains(project, TestApp.App.Projects.All);
		Assert.Equal(project, TestApp.App.Projects.Find("Fresh"));
		Assert.Equal(project, TestApp.App.Projects.Find(project.FilePath));
	}

	[Test]
	public async Task AnEditMakesItDirtyAndSavingCleansIt()
	{
		ProjectHandle project = await TestApp.NewProjectAsync();
		project.Timeline.AddChannel(video: false);
		Assert.True(project.Dirty);
		Assert.True(project.Save());
		Assert.False(project.Dirty);
	}

	[Test]
	public async Task SaveAsMovesTheProjectToItsNewFile()
	{
		ProjectHandle project = await TestApp.NewProjectAsync("Before");
		string target = Path.Combine(TestApp.Sandbox, "Moved", "After");
		Directory.CreateDirectory(Path.GetDirectoryName(target)!);
		Assert.True(project.SaveAs(target));
		Assert.Equal(target + ProjectFile.Extension, project.FilePath, "the extension is added");
		Assert.Equal("After", project.Name);
		Assert.True(File.Exists(project.FilePath));
	}

	[Test]
	public async Task OpeningAnOpenProjectFocusesTheSameWindow()
	{
		ProjectHandle project = await TestApp.NewProjectAsync();
		ProjectHandle again = await TestApp.App.Projects.OpenAsync(project.FilePath);
		Assert.Equal(project, again);
		Assert.Count(1, TestApp.App.Projects.All);
	}

	[Test]
	public async Task ClosingDropsTheHandle()
	{
		ProjectHandle project = await TestApp.NewProjectAsync();
		bool closedEvent = false;
		void OnClosed(ProjectHandle p) => closedEvent |= p == project;
		TestApp.App.Projects.Closed += OnClosed;
		try
		{
			Assert.True(await project.CloseAsync());
			await TestApp.Frames(2);
		}
		finally { TestApp.App.Projects.Closed -= OnClosed; }

		Assert.False(project.IsOpen);
		Assert.True(closedEvent, "Closed fired");
		Assert.Throws<System.InvalidOperationException>(() => _ = project.Dirty, "a closed handle refuses to work");
	}

	[Test]
	public async Task ClosingWithUnsavedChangesCanBeKeptOpen()
	{
		ProjectHandle project = await TestApp.NewProjectAsync();
		project.Timeline.AddChannel(video: true);
		Scripts.UI.Dialogs.Dialogs.Answering = d => d.Cancel?.Id;
		Assert.False(await project.CloseAsync(), "cancelling keeps it open");
		Assert.True(project.IsOpen);
	}

	[Test]
	public async Task OpeningARecentProjectPutsItFirst()
	{
		ProjectHandle first = await TestApp.NewProjectAsync("First");
		ProjectHandle second = await TestApp.NewProjectAsync("Second");
		Assert.Equal(Path.GetFullPath(second.FilePath), RecentProjects.All.First().Path);
		await first.CloseAsync();
		await TestApp.App.Projects.OpenAsync(first.FilePath);
		Assert.Equal(Path.GetFullPath(first.FilePath), RecentProjects.All.First().Path);
	}

	[Test]
	public async Task OpenedFiresForEveryNewProject()
	{
		int opened = 0;
		void Count(ProjectHandle _) => opened++;
		_ = TestApp.App.Projects.All;
		TestApp.App.Projects.Opened += Count;
		try
		{
			await TestApp.NewProjectAsync();
			await TestApp.NewProjectAsync();
		}
		finally { TestApp.App.Projects.Opened -= Count; }
		Assert.Equal(2, opened);
	}
}
