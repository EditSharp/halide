using EditSharp.Components;
using EditSharp.Components.Clips;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class UIChannelTimeline : PanelContainer
{
	[ExportGroup("Controls")]

	[Export] Control clipsContainer;

	[ExportGroup("Packed Scenes")]

	[Export] PackedScene clipScene;

	// reference to parent timeline
	public UITimeline Timeline;
	// reference to actual channel data under the hood
	public Channel Channel;

	public List<UIClip> UIClips = [];

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// check for existing clips in the provided channel and create the gui for them
		foreach (var clip in Channel.Clips)
		{
			AddClip(clip);
		}
	}

	public void SetWidth(double pixelsPerSecond)
	{
		foreach (var clip in UIClips)
		{

			float offset = (float)(clip.Clip.Start.TotalSeconds * pixelsPerSecond);
			float length = (float)(clip.Clip.Duration.TotalSeconds * pixelsPerSecond);

			clip.Position = new(
				offset,
				clip.Position.Y
			);

			clip.SetLength(length);
		}
	}

	public void AddClip(Clip c)
	{
		UIClip ui = clipScene.Instantiate() as UIClip;

		ui.Clip = c;
		ui.Channel = this;

		clipsContainer.AddChild(ui);

		UIClips.Add(ui);
	}

	public void EditClip()
	{
		
	}
}
