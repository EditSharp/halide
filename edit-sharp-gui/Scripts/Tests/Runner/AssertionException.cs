using System;

namespace EditSharpGUI.Tests;

/// <summary>A check that didn't hold.</summary>
public sealed class AssertionException(string message) : Exception(message);
