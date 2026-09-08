using EditSharp.Components;
using EditSharp.Components.Clips;
using Godot;
using System;
using System.Collections.Generic;

public partial class UIChannelTimeline : PanelContainer
{
	[ExportGroup("Controls")]

	[Export] BoxContainer clipsContainer;

	[ExportGroup("Packed Scenes")]

	[Export] PackedScene clipScene;

	// reference to parent timeline
	public UITimeline timeline;
	// reference to actual channel data under the hood
	public Channel channel;

	List<UIClip> UIClips = [];

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// check for existing clips in the provided channel and create the gui for them
		foreach (var clip in channel.Clips)
		{
			AddClip(clip);
		}
	}

	public void AddClip(Clip c)
	{
		UIClip ui = clipScene.Instantiate() as UIClip;

		ui.clip = c;

		clipsContainer.AddChild(ui);
	}

	public void EditClip()
	{
		
	}
}
