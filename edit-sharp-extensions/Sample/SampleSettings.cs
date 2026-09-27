using EditSharp.Editing;

namespace SampleExtension;

/// <summary>The sample's settings page.</summary>
public sealed class SampleSettings
{
	[Editable(Group = "Greeting")]
	public string Greeting { get; set; } = "Hello";
}
