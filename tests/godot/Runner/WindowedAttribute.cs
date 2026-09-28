using System;

namespace Halide.Tests;

/// <summary>A test needing real rendering or OS windows: skipped by headless runs, run by windowed ones.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class WindowedAttribute : Attribute;
