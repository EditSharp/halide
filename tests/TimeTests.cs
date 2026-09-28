using Xunit;

namespace EditSharp.Tests;

// exact time: seconds, frames at awkward rates, samples and scaling all land on whole ticks
public class TimeTests
{
	static readonly Rational Ntsc = new(30000, 1001);

	[Theory]
	[InlineData(0.0)]
	[InlineData(1.0)]
	[InlineData(2.5)]
	[InlineData(-3.25)]
	[InlineData(3600.0)]
	public void SecondsRoundTrip(double seconds) => Assert.Equal(seconds, Time.FromSeconds(seconds).Seconds, 9);

	[Fact]
	public void WholeSecondsAreExact() => Assert.Equal(Time.TicksPerSecond * 7, Time.FromSeconds(7L).Ticks);

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(29)]
	[InlineData(30)]
	[InlineData(1799)]
	[InlineData(107892)]
	public void NtscFramesRoundTripExactly(long frame) => Assert.Equal(frame, Time.FromFrame(frame, Ntsc).ToFrame(Ntsc));

	[Fact]
	public void TheTickDividesCommonRatesExactly()
	{
		foreach (long fps in new long[] { 24, 25, 30, 48, 50, 60, 120 })
			Assert.Equal(0, Time.TicksPerSecond % fps);
		foreach (long rate in new long[] { 44100, 48000, 96000 })
			Assert.Equal(0, Time.TicksPerSecond % rate);
	}

	[Fact]
	public void SamplesRoundTrip()
	{
		Time t = Time.FromSamples(48000 * 3 + 17, 48000);
		Assert.Equal(48000 * 3 + 17, t.ToSamples(48000));
	}

	[Fact]
	public void ScalingByARational()
	{
		Time two = Time.FromSeconds(2L);
		Assert.Equal(Time.FromSeconds(1L), two.Scale(new Rational(1, 2)));
		Assert.Equal(Time.FromSeconds(6L), two.Scale(new Rational(3, 1)));
	}

	[Fact]
	public void ArithmeticAndComparison()
	{
		Time a = Time.FromSeconds(1.5), b = Time.FromSeconds(0.5);
		Assert.Equal(Time.FromSeconds(2L), a + b);
		Assert.Equal(Time.FromSeconds(1L), a - b);
		Assert.True(a > b);
		Assert.True(b < a);
		Assert.Equal(Time.Zero, a - a);
	}

	[Fact]
	public void TimeSpansConvert()
	{
		System.TimeSpan span = System.TimeSpan.FromMilliseconds(1234);
		Assert.Equal(span, Time.FromTimeSpan(span).ToTimeSpan());
	}

	[Fact]
	public void FrameLengthIsOneFrame() => Assert.Equal(Time.FromFrame(1, Ntsc), Time.FrameLength(Ntsc));
}
