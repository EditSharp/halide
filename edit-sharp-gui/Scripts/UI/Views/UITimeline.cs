using EditSharp.Components;
using EditSharp.Components.Channels;
using EditSharp.Components.Clips;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class UITimeline : Control
{
	[ExportGroup("Options")]

	[Export] Slider widthSlider;
	[Export] Slider heightSlider;

	[ExportGroup("Channels")]

	[Export] ScrollContainer editsContainer;
	[Export] VBoxContainer edits;
	[Export] Control editsScrollSpacer;
	[Export] ScrollContainer clipsViewContainer;
	[Export] UIClipsView clipsView;
	[Export] ScrollContainer rulerContainer;
	[Export] UIRuler ruler;

	[ExportGroup("Packed Scenes")]

	[Export] PackedScene channelEditScene;

	public Timeline Timeline;

	public double VerticalScale { 
		get; 
		set
		{
			if (value > 0d)
			{
				// update clips view
				clipsView.Refresh();

				field = value;
			}
			else throw new ArgumentOutOfRangeException(nameof(value), "Vertical scale must be greater than zero");
		}
	} = 90d;

	public double PixelsPerSecond { 
		get; 
		set
		{
			if (value > 0d)
			{
				// update clips view
				clipsView.Refresh();

				// update ruler
				ruler.Update(value, ProjectManager.Singleton.CurrentProject.RenderSettings.Framerate);

				field = value;
			}
			else throw new ArgumentOutOfRangeException(nameof(value), "Pixels per second must be greater than zero");
		}
	} = 100d;

	public double TimeSpanToPixels(TimeSpan t) => t.TotalSeconds * PixelsPerSecond;
	public TimeSpan PixelsToTimeSpan(double p) => TimeSpan.FromSeconds(p / PixelsPerSecond);


	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// TEMPORARILY GET TIMELINE AUTOMATICALLY INSTEAD OF MANUAL ASSIGNMENT
		Timeline = ProjectManager.Singleton.CurrentProject.Timeline;

		// add event listeners
		heightSlider.ValueChanged += h => VerticalScale = h;
		widthSlider.ValueChanged += w => PixelsPerSecond = w;

		clipsView.UITimeline = this;
		clipsView.Refresh();
	}

    public override void _Process(double delta)
    {
		// match scrolls to timeline
        editsContainer.ScrollVertical = clipsViewContainer.ScrollVertical;
		rulerContainer.ScrollHorizontal = clipsViewContainer.ScrollHorizontal;

		//stretch ruler to length of channels
		ruler.CustomMinimumSize = new(
			clipsView.Size.X + clipsViewContainer.GetVScrollBar().Size.X,
			ruler.CustomMinimumSize.Y
		);

		// show scrollbar spacer if timeline is scrollable
		editsScrollSpacer.Visible = clipsViewContainer.GetHScrollBar().Visible;
    }
}
