using System;

namespace EditSharpGUI.Tests;

/// <summary>A class of tests the runner finds and runs; each fixture gets a fresh user-data folder.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class TestFixtureAttribute : Attribute;
