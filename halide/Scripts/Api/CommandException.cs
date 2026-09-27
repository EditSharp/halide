using System;

namespace Halide.Api;

/// <summary>A command couldn't be run: it doesn't exist, doesn't apply right now, or failed.</summary>
public sealed class CommandException(string message, Exception inner = null) : Exception(message, inner);
