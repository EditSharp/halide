using System.Reflection;
using System.Runtime.Loader;

namespace EditSharpGUI.Api.Extensions;

/// <summary>An extension's own assembly context, unloadable, reading its dependencies from its folder and sharing the app's own assemblies.</summary>
sealed class ExtensionLoadContext(string mainAssembly) : AssemblyLoadContext(isCollectible: true)
{
	readonly AssemblyDependencyResolver resolver = new(mainAssembly);

	protected override Assembly Load(AssemblyName name)
	{
		// the app, EditSharp and Godot are the app's own, wherever godot loaded them: the extension talks to the same types
		foreach (Assembly loaded in System.AppDomain.CurrentDomain.GetAssemblies())
			if (GetLoadContext(loaded) is { IsCollectible: false } && AssemblyName.ReferenceMatchesDefinition(loaded.GetName(), name)) return loaded;

		return resolver.ResolveAssemblyToPath(name) is string path ? LoadFromAssemblyPath(path) : null;
	}
}
