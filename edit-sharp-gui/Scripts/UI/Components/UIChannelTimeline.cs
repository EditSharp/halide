using EditSharp.Components;
using EditSharp.Components.Clips;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

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

	List<UIClip> clips = [];

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// check for existing clips in the provided channel and create the gui for them
		foreach (var clip in channel.Clips)
		{
			AddClip(clip);
		}
	}

	public void SetWidth(double pixelsPerSecond)
	{
		foreach (var clip in clips)
		{

			float length = (float)(clip.clip.Duration.TotalSeconds * pixelsPerSecond);

			float gap = 0f;

			int clipIndex = clips.IndexOf(clip);
			if (clipIndex != clips.Count - 1)
			{
				gap = (float)(clip.clip.DistanceFrom(clips[clipIndex + 1].clip).TotalSeconds * pixelsPerSecond);
			}

			clip.SetLength(length, gap);
		}
	}

	public void AddClip(Clip c)
	{
		UIClip ui = clipScene.Instantiate() as UIClip;

		ui.clip = c;

		clipsContainer.AddChild(ui);

		clips.Add(ui);
	}

	public void EditClip()
	{
		
	}
}
