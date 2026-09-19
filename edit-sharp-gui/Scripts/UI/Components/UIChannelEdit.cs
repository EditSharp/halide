using EditSharp.Components;
using EditSharp.Components.Channels;
using EditSharp.History;
using Godot;
using System;

public partial class UIChannelEdit : PanelContainer
{
	[ExportGroup("Controls")]

	[Export] LineEdit channelName;

	// reference to actual channel data under the hood
	public Channel Channel;
	public UITimeline UITimeline;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		channelName.Text = Channel.Name;

		// the name is written back when typing ends, either way it ends
		channelName.TextSubmitted += _ => CommitName();
		channelName.FocusExited += CommitName;

		Refresh();
	}

	void CommitName()
	{
		string name = channelName.Text;
		if (name == Channel.Name) return;

		using Transaction.Scope change = UITimeline.History.Begin("Rename channel");
		Channel.Name = name;
		change.Commit();
	}

	// reset chanel edit ui to what is actually stored in data
	public void Refresh()
	{
		CustomMinimumSize = new(
			CustomMinimumSize.X,
			(float)UITimeline.VerticalScale
		);

		// an undo can change the name under the field - but not while it is
		// being typed in
		if (!channelName.HasFocus()) channelName.Text = Channel.Name;
	}
}
