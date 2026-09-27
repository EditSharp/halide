using System;

namespace Halide.Api;

/// <summary>Something added to the app, removed again when disposed.</summary>
public sealed class Registration(Action remove) : IDisposable
{
	Action remove = remove;

	public void Dispose()
	{
		remove?.Invoke();
		remove = null;
	}
}
