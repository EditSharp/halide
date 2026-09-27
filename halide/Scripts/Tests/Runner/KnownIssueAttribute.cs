using System;

namespace Halide.Tests;

/// <summary>A test that reproduces a bug not fixed yet: it runs and is reported, but its failure (or a crash in its fixture) doesn't fail the run.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class KnownIssueAttribute(string issue) : Attribute
{
	public string Issue { get; } = issue;
}
