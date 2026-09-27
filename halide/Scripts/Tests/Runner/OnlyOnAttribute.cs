using System;

namespace Halide.Tests;

/// <summary>A test that only means something on one OS ("Windows", "macOS", "Linux"); skipped elsewhere.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class OnlyOnAttribute(string os) : Attribute
{
	public string OS { get; } = os;
}
