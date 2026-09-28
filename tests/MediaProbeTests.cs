using EditSharp.Components.Media;
using EditSharp.History;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EditSharp.Tests;

// background probes: a file that can't be probed reports once and isn't probed again until it changes
public class MediaProbeTests
{
	static async Task<int> ProbesAfterAsking(VideoMedia media, int asks)
	{
		int finished = 0;
		media.InfoAvailable += _ => Interlocked.Increment(ref finished);
		await Ask(media, () => finished >= 1);
		for (int i = 1; i < asks; i++) Assert.False(media.TryGetInfo(out _));
		await Task.Delay(500);
		return finished;
	}

	static VideoMedia Garbage(out string path)
	{
		path = Path.Combine(Path.GetTempPath(), $"editsharp-probe-{System.Guid.NewGuid():N}.mp4");
		File.WriteAllText(path, "not a video");
		using var quiet = Transaction.Suppress();
		return new VideoMedia { Path = path };
	}

	[Fact]
	public async Task AFailedProbeIsntRetriedForTheSameFile()
	{
		VideoMedia media = Garbage(out string path);
		try
		{
			Assert.Equal(1, await ProbesAfterAsking(media, 3));
		}
		finally { File.Delete(path); }
	}

	[Fact]
	public async Task AChangedFileIsProbedAgain()
	{
		VideoMedia media = Garbage(out string path);
		try
		{
			int finished = 0;
			media.InfoAvailable += _ => Interlocked.Increment(ref finished);
			await Ask(media, () => finished >= 1);
			File.AppendAllText(path, " still not a video");
			await Ask(media, () => finished >= 2);
			Assert.Equal(2, finished);
		}
		finally { File.Delete(path); }
	}

	static async Task Ask(VideoMedia media, System.Func<bool> done)
	{
		Assert.False(media.TryGetInfo(out _));
		for (int wait = 0; wait < 200 && !done(); wait++) await Task.Delay(50);
	}

	[Fact]
	public void AMissingFileIsNeverProbed()
	{
		using var quiet = Transaction.Suppress();
		VideoMedia media = new() { Path = Path.Combine(Path.GetTempPath(), "editsharp-missing.mp4") };
		int finished = 0;
		media.InfoAvailable += _ => finished++;
		Assert.False(media.TryGetInfo(out _));
		Assert.Equal(0, finished);
	}
}
