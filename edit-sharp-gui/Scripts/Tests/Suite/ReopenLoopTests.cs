using EditSharpGUI.Api;
using System.Threading.Tasks;

namespace EditSharpGUI.Tests;

// an empty project closed and opened again, over and over
[TestFixture]
public sealed class ReopenLoopTests
{
	[Test(Timeout = 120)]
	public async Task AnEmptyProjectReopensTwentyTimes()
	{
		ProjectHandle project = await TestApp.NewProjectAsync();
		string path = project.FilePath;
		project.Save();
		for (int i = 0; i < 20; i++)
		{
			await project.CloseAsync();
			project = await TestApp.App.Projects.OpenAsync(path);
			await TestApp.Frames(3);
		}
	}
}
