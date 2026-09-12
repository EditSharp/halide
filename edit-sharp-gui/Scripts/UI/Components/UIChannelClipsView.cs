using EditSharp.Components;
using EditSharp.Components.Channels;
using EditSharp.Components.Clips;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class UIChannelClipsView : PanelContainer
{
	[ExportGroup("Controls")]

	[Export] Control clipsContainer;

	[ExportGroup("Packed Scenes")]

	[Export] PackedScene clipScene;

	// reference to actual channel data under the hood
	public Channel Channel;

	public UITimeline UITimeline;
	public List<UIClip> UIClips = [];
	

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// check for existing clips in the provided channel and create the gui for them
		Refresh();
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

	public UIClip CreateUIClip(Clip c)
	{
		UIClip ui = clipScene.Instantiate() as UIClip;

		ui.Clip = c;
		ui.ClipsView = this;

		clipsContainer.AddChild(ui);

		UIClips.Add(ui);

		return ui;
	}

	public void RemoveUIClip(UIClip c)
	{
		// return if clip is not a part of this channel
		UIClips.Remove(c);
		c.QueueFree();
	}

	// refresh clip guis
	// only refreshes missing guis by default
	// returns number of clips refreshed
	public int Refresh(bool all = false)
	{
		if (all)
		{
			//delete all clips
			foreach (UIClip c in UIClips) c.QueueFree();
			UIClips.Clear();
		}
		
		// find all missing clips and generate their guis
		int clips = 0;
		foreach (Clip c in Channel.Clips)
		{
			// skip existing clips
			if (UIClips.Any(u => ReferenceEquals(u.Clip, c))) continue;

			CreateUIClip(c);
			clips++;
		}

		SetWidth(UITimeline.PixelsPerSecond);
		return clips;
	}
}
