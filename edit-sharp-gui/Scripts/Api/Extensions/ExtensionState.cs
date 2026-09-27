namespace EditSharpGUI.Api.Extensions;

/// <summary>Where an extension stands.</summary>
public enum ExtensionState
{
	/// <summary>Turned off, by the user or never turned on.</summary>
	Disabled,

	/// <summary>Running.</summary>
	Enabled,

	/// <summary>New or changed since it was trusted; it waits for the user to enable it.</summary>
	Untrusted,

	/// <summary>Built for another API version.</summary>
	Incompatible,

	/// <summary>It threw while starting or running, and was stopped.</summary>
	Failed,

	/// <summary>Its manifest or assembly couldn't be read.</summary>
	Broken,
}
