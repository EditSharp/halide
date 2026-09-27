using System;
using System.Collections.Generic;
using System.Linq;

namespace EditSharpGUI.Tests;

/// <summary>The checks tests make; each throws <see cref="AssertionException"/> saying what was expected.</summary>
public static class Assert
{
	public static void True(bool condition, string what = "expected true")
	{
		if (!condition) throw new AssertionException(what);
	}

	public static void False(bool condition, string what = "expected false") => True(!condition, what);

	public static void Equal<T>(T expected, T actual, string what = null)
	{
		if (!EqualityComparer<T>.Default.Equals(expected, actual))
			throw new AssertionException($"{what ?? "values differ"}: expected <{expected}>, got <{actual}>");
	}

	public static void NotEqual<T>(T unexpected, T actual, string what = null)
	{
		if (EqualityComparer<T>.Default.Equals(unexpected, actual))
			throw new AssertionException($"{what ?? "values match"}: didn't expect <{actual}>");
	}

	public static void Near(double expected, double actual, double tolerance = 1e-6, string what = null)
	{
		if (Math.Abs(expected - actual) > tolerance)
			throw new AssertionException($"{what ?? "numbers differ"}: expected {expected} ± {tolerance}, got {actual}");
	}

	public static T NotNull<T>(T value, string what = "expected a value") where T : class
	{
		if (value is null) throw new AssertionException(what);
		return value;
	}

	public static void Null(object value, string what = "expected nothing")
	{
		if (value is not null) throw new AssertionException($"{what}: got <{value}>");
	}

	public static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string what = null)
	{
		List<T> e = [.. expected], a = [.. actual];
		if (!e.SequenceEqual(a))
			throw new AssertionException($"{what ?? "sequences differ"}: expected [{string.Join(", ", e)}], got [{string.Join(", ", a)}]");
	}

	public static void Contains<T>(T item, IEnumerable<T> collection, string what = null)
	{
		if (!collection.Contains(item)) throw new AssertionException($"{what ?? "missing item"}: <{item}> isn't in [{string.Join(", ", collection)}]");
	}

	public static void DoesNotContain<T>(T item, IEnumerable<T> collection, string what = null)
	{
		if (collection.Contains(item)) throw new AssertionException($"{what ?? "unexpected item"}: <{item}> is in the collection");
	}

	public static void Count<T>(int expected, IEnumerable<T> collection, string what = null) => Equal(expected, collection.Count(), what ?? "count");

	public static TException Throws<TException>(Action action, string what = null) where TException : Exception
	{
		try { action(); }
		catch (TException e) { return e; }
		catch (Exception e) { throw new AssertionException($"{what ?? "wrong exception"}: expected {typeof(TException).Name}, got {e.GetType().Name}: {e.Message}"); }
		throw new AssertionException($"{what ?? "no exception"}: expected {typeof(TException).Name}");
	}

	public static void Fail(string what) => throw new AssertionException(what);

	public static void Skip(string reason) => throw new SkipException(reason);
}
