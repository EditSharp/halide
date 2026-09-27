using System;

namespace Halide.Tests;

/// <summary>A test that can't mean anything here, and is skipped rather than failed.</summary>
public sealed class SkipException(string reason) : Exception(reason);
