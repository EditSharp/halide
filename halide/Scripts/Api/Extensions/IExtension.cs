namespace Halide.Api.Extensions;

/// <summary>An extension's entry point: the type its manifest names, made once each time the extension is enabled.</summary>
/// <remarks>
/// Everything added through the context is removed again when the extension is disabled or reloaded, so
/// <see cref="Deactivate"/> only needs to undo what the extension did on its own.
/// </remarks>
public interface IExtension
{
	/// <summary>The extension is starting: add its commands, views, menu items, settings pages and importers here.</summary>
	void Activate(ExtensionContext context);

	/// <summary>The extension is stopping, before what it added through the context is removed.</summary>
	void Deactivate();
}
