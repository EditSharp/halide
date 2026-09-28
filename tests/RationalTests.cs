using Xunit;

namespace EditSharp.Tests;

// rationals reduce, compare by value, and parse the ways speeds and rates are written
public class RationalTests
{
	[Fact]
	public void MultiplyingAndDividingReduce()
	{
		Rational half = new(1, 2), third = new(1, 3);
		Assert.Equal(new Rational(1, 6), half * third);
		Assert.Equal(new Rational(3, 2), half / third);
	}

	[Fact]
	public void ValueIsTheQuotient() => Assert.Equal(29.97002997, new Rational(30000, 1001).Value, 6);

	[Fact]
	public void NegativesAndReciprocals()
	{
		Rational r = new(-3, 4);
		Assert.Equal(new Rational(3, 4), r.Abs());
		Assert.Equal(new Rational(3, 4), -r);
		Assert.Equal(new Rational(-4, 3), r.Reciprocal());
	}

	[Fact]
	public void WholeNumbersConvert()
	{
		Rational five = 5;
		Assert.Equal(new Rational(5, 1), five);
		Assert.Equal(1, Rational.One.Value);
		Assert.Equal(0, Rational.Zero.Value);
	}

	[Theory]
	[InlineData("30000/1001", 30000, 1001)]
	[InlineData("2", 2, 1)]
	[InlineData("0.5", 1, 2)]
	public void Parsing(string text, long num, long den)
	{
		Assert.True(Rational.TryParse(text, out Rational r));
		Assert.Equal(new Rational(num, den).Value, r.Value, 9);
	}

	[Theory]
	[InlineData("")]
	[InlineData("abc")]
	[InlineData("1/0")]
	public void BadTextDoesntParse(string text) => Assert.False(Rational.TryParse(text, out _));

	[Fact]
	public void DecimalsAreExact() => Assert.Equal(new Rational(1, 4).Value, Rational.FromDecimal(0.25m).Value);

	[Fact]
	public void ApproximationsAreClose() => Assert.Equal(System.Math.PI, Rational.Approximate(System.Math.PI).Value, 6);
}
