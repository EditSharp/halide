using System;

namespace Halide.Tests;

/// <summary>A test: a method taking nothing and returning void or Task.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute
{
	/// <summary>Seconds before it's failed as hung.</summary>
	public double Timeout { get; set; } = 30;
}
