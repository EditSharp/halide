using Halide.Scripts.App.Commands;

namespace Halide.Api;

/// <summary>The app, for extensions, scripts and tests: everything the GUI can do, as services.</summary>
/// <remarks>Every member must be used from the main thread; remote callers are marshalled there by the RPC server.</remarks>
public sealed class EditSharpApp
{
	/// <summary>The API's version. The major number changes when something existing changes; the minor when something is added.</summary>
	public const string Version = "1.0";

	/// <summary>The running app.</summary>
	public static EditSharpApp Instance { get; } = new();

	EditSharpApp() { }

	/// <summary>Open projects, opening and creating them.</summary>
	public ProjectsService Projects { get; } = new();

	/// <summary>The named commands menus, shortcuts and remote callers run.</summary>
	public CommandsService Commands { get; } = new();

	/// <summary>The dockable views, built-in and added by extensions.</summary>
	public ViewRegistry Views { get; } = new();

	/// <summary>Extra items extensions put in the bar menus.</summary>
	public MenuService Menus { get; } = new();

	/// <summary>Media importers extensions add.</summary>
	public ImporterRegistry Importers { get; } = new();

	/// <summary>Pages extensions add to App Settings.</summary>
	public SettingsPageRegistry SettingsPages { get; } = new();

	/// <summary>The extensions found, and turning them on and off.</summary>
	public Extensions.ExtensionManager Extensions { get; } = new();

	/// <summary>The app settings, as the App Settings window shows them.</summary>
	public AppSettings Settings => AppSettings.Current;
}
