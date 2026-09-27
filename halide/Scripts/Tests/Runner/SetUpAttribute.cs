using System;

namespace Halide.Tests;

/// <summary>Runs before each test of its fixture.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SetUpAttribute : Attribute;
