using Godot;
using System;
using System.Linq;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// ---- something with no editor: shown, not edited (Label.tscn) ----

[Tool]
public partial class LabelEditor : ValueEditor
{
	[Export] Label label;

	protected override void Build() { }

	public override void Display(object value, bool mixed)
	{
		label.Text = mixed ? "—" : value switch
		{
			null => "(none)",
			EditSharp.Components.Timeline timeline => $"Timeline ({timeline.VideoChannels.Count} video, {timeline.AudioChannels.Count} audio)",
			_ => value.ToString()
		};
	}
}
