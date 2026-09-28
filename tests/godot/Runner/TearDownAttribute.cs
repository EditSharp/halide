using System;

namespace Halide.Tests;

/// <summary>Runs after each test of its fixture, even a failed one.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TearDownAttribute : Attribute;
