namespace EditSharpGUI.Tests;

/// <summary>How one test went.</summary>
public sealed record TestResult(string Fixture, string Test, TestOutcome Outcome, double Seconds, string Message = null);
