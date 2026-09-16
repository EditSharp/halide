using Godot;
using System;
using EditSharp;
using EditSharp.Components.Clips;
using System.Net.Sockets;
using EditSharpGUI.Scripts.UI.Components;

public partial class UIClip : PanelContainer
{
	[ExportGroup("Controls")]

	[Export] Control content;
	[Export] Label clipName;
	[Export] Panel thumbnail;
	[Export] Panel outline;

	[ExportGroup("Styles")]

	[Export] StyleBox outlinedStyleBox;
	[Export] StyleBox videoStyleBox;
	[Export] StyleBox audioStyleBox;

	public Clip Clip;
	public UIClipsView ClipsView;

	// whether this clip is selected
	// updated by timeline
	public bool Selected = false;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		input = new()
		{
			ClipsView = ClipsView,
			UIClip = this,
			Clip = Clip,
		};

		// update gui based on provided clip
		clipName.Text = Clip.Name;

		if (Clip is VideoClip) content.AddThemeStyleboxOverride("panel", videoStyleBox);
		else content.AddThemeStyleboxOverride("panel", audioStyleBox);

		Refresh();
		UpdateThumbnail();
	}

	UIClipInputHandler input;
    public override void _GuiInput(InputEvent @event) { input.Input(@event); }

	// reset clip ui to what is actually stored in data
	public void Refresh()
	{
		Position = new(
			(float)ClipsView.UITimeline.TimeSpanToPixels(Clip.Start),
			(float)ClipsView.UITimeline.VerticalScale * GetChannelsDown()
		);

		Size = new(
			(float)ClipsView.UITimeline.TimeSpanToPixels(Clip.Duration),
			(float)ClipsView.UITimeline.VerticalScale
		);
	}

	// move clip ui relative to what is actually stored in data
	public void MoveGUI(TimeSpan timeDelta, int channelDelta)
	{
		Position = new(
			(float)ClipsView.UITimeline.TimeSpanToPixels(Clip.Start + timeDelta),
			(float)(ClipsView.UITimeline.VerticalScale * (double)(GetChannelsDown() + ((Clip is VideoClip) ? -channelDelta : channelDelta)))
		);

		//GD.Print($"moved clip {Clip.Name} to {new Vector2((float)ClipsView.UITimeline.TimeSpanToPixels(Clip.Start + timeDelta), (float)(ClipsView.UITimeline.VerticalScale * (double)(GetChannelsDown() + ((Clip is VideoClip) ? -channelDelta : channelDelta))))}");
	}

	int GetChannelsDown()
	{
		// video clip
		if (Clip is VideoClip)
		{
			return Clip.Channel.Timeline.VideoChannels.Count - 1 - Clip.Channel.Index;
		}
		// audio clip
		else
		{
			return Clip.Channel.Timeline.VideoChannels.Count + Clip.Channel.Index;
		}
	}

	public void SetOutlined(bool outlined)
	{
		if (outlined) outline.AddThemeStyleboxOverride("panel", outlinedStyleBox);
		else outline.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
	}

	public void SetTransparency(float alpha)
	{
		Color transparency = Modulate;
		transparency.A = alpha;
		Modulate = transparency;
	}

	public void SetLength(float length)
	{
		content.CustomMinimumSize = new(length, content.CustomMinimumSize.Y);

		CustomMinimumSize = new(
			content.Size.X,
			CustomMinimumSize.Y
		);

		UpdateThumbnail();
	}

	void UpdateThumbnail()
	{
		
	}
}
