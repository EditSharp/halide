using EditSharp.Components.Media;
using Halide.Api;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Halide.Tests;

// the media library through the service, and importers extensions add
[TestFixture]
public sealed class MediaServiceTests
{
	ProjectHandle project;

	[SetUp]
	public async Task Open() => project = await TestApp.NewProjectAsync();

	string File(string name)
	{
		string path = Path.Combine(TestApp.Sandbox, name);
		System.IO.File.WriteAllText(path, "x");
		return path;
	}

	[Test]
	public void ImportingAddsOnceAsOneEntry()
	{
		string wav = File("a.wav"), mp4 = File("b.mp4");
		int position = project.History.Position;
		Assert.Count(2, project.Media.Import([wav, mp4]));
		Assert.Equal(position + 1, project.History.Position);
		Assert.Count(0, project.Media.Import([wav]), "already in the library");
		Assert.Count(2, project.Media.All);
	}

	[Test]
	public void AudioFilesBecomeAudioMedia()
	{
		IMedia media = project.Media.Import([File("sound.wav")]).Single();
		Assert.True(media is AudioMedia);
	}

	[Test]
	public void FindingIgnoresCase()
	{
		string path = File("Clip.mp4");
		project.Media.Import([path]);
		Assert.NotNull(project.Media.Find(path.ToUpperInvariant()));
		Assert.Null(project.Media.Find(Path.Combine(TestApp.Sandbox, "missing.mp4")));
	}

	[Test]
	public void RemovingIsUndoable()
	{
		IMedia media = project.Media.Import([File("gone.mp4")]).Single();
		project.Media.Remove([media]);
		Assert.Count(0, project.Media.All);
		project.History.Undo();
		Assert.Count(1, project.Media.All);
	}

	[Test]
	public void RemovingWhatIsntThereDoesNothing()
	{
		VideoMedia stranger;
		using (EditSharp.History.Transaction.Suppress()) stranger = new VideoMedia { Path = "nowhere.mp4" };
		int position = project.History.Position;
		project.Media.Remove([stranger]);
		Assert.Equal(position, project.History.Position);
	}

	[Test]
	public void ChangedFires()
	{
		int changes = 0;
		void Count() => changes++;
		project.Media.Changed += Count;
		try { project.Media.Import([File("c.mp4")]); }
		finally { project.Media.Changed -= Count; }
		Assert.True(changes > 0);
	}

	[Test]
	public void ImportersTakeTheirExtensionsLatestFirst()
	{
		using Registration first = TestApp.App.Importers.Register([".thing"], path => new AudioMedia { Path = path });
		using Registration second = TestApp.App.Importers.Register(["thing"], path => new VideoMedia { Path = path });
		Assert.True(project.Media.Import([File("x.thing")]).Single() is VideoMedia, "the latest registered wins");
	}

	[Test]
	public void AnImporterThatDeclinesOrThrowsFallsBack()
	{
		using Registration declines = TestApp.App.Importers.Register([".wav"], _ => null);
		using Registration throws = TestApp.App.Importers.Register([".wav"], _ => throw new System.InvalidOperationException());
		Assert.True(project.Media.Import([File("y.wav")]).Single() is AudioMedia, "the built-in importer took it");
	}

	[Test]
	public void AnUnregisteredImporterStops()
	{
		Registration custom = TestApp.App.Importers.Register([".wav"], path => new VideoMedia { Path = path });
		custom.Dispose();
		Assert.True(project.Media.Import([File("z.wav")]).Single() is AudioMedia);
	}
}
