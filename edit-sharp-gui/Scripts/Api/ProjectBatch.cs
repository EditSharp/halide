using EditSharp.History;
using System;

namespace EditSharpGUI.Api;

/// <summary>Edits grouped into one undo entry; see <see cref="ProjectHandle.Batch(string)"/>.</summary>
public sealed class ProjectBatch : IDisposable
{
	readonly Transaction.Scope scope;
	bool cancelled, done;

	internal ProjectBatch(Transaction.Scope scope) => this.scope = scope;

	/// <summary>Rolls everything in the batch back when it's disposed.</summary>
	public void Cancel() => cancelled = true;

	public void Dispose()
	{
		if (done) return;
		done = true;

		if (!cancelled) scope.Commit();
		scope.Dispose();
	}
}
