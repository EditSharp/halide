using Godot;
using System;
using EditSharp;
using EditSharp.Components.Clips;
using EditSharpGUI.Scripts.Input;
using static UIClipsView;

public partial class UIClip : PanelContainer
{
	[ExportGroup("Controls")]

	[Export] Control content;
	[Export] Label clipName;
	[Export] Panel thumbnail;
	[Export] Panel outline;

	[ExportGroup("Styles")]

	[Export] StyleBoxFlat contentStyleBox;
	[Export] Color videoColor;
	[Export] Color audioColor;

	public Clip Clip;
	public UIClipsView ClipsView;

	public Color Color
	{
        get;
        set
		{
			StyleBoxFlat style = contentStyleBox.DuplicateDeep() as StyleBoxFlat;
			style.BgColor = value;
			content.AddThemeStyleboxOverride("panel", style);
			field = value;
		}
	}
	public bool Selected 
	{
		get
		{
			return outline.Visible;
		}
		set
		{
			outline.Visible = value;
		}
	}

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		// update gui based on provided clip
		clipName.Text = Clip.Name;

		if (Clip is VideoClip) Color = videoColor;
		else Color = audioColor;

		Refresh();
		UpdateThumbnail();
	}

    public override void _GuiInput(InputEvent _)
	{
		MouseButtonState left = InputManager.Singleton.Mouse.LeftButton;

        switch (left.Action)
        {
            case MouseAction.Press:
            case MouseAction.DoubleClick:
                // claim the press, so whatever drag follows is unambiguously ours
                if (!left.Capture(this)) break;
                Press(left);
                break;

            case MouseAction.DragStart:
                if (!left.HasCapture(this)) break;
                ClipsView.BeginDrag();
                // fall through so the frame that started the drag also moves the
                // clips - otherwise the selection visibly jumps the threshold
                goto case MouseAction.DragMove;

            case MouseAction.DragMove:
                if (!left.HasCapture(this)) break;
                ClipsView.DragSelection(this, Drag(left));
                break;

            case MouseAction.DragEnd:
                if (!left.HasCapture(this)) break;
                ClipsView.FinishDrag(this, Drag(left));
                break;

            case MouseAction.Click:
                if (!left.HasCapture(this)) break;
                // a modified click already did its work on press; collapsing the
                // selection here would immediately undo it
                if (left.PressModifiers.Shift || left.PressModifiers.Control) break;
                ClipsView.SelectClip(this, SelectionMode.Exclusive);
                break;
        }

		void Press(MouseButtonState left)
		{
			if (left.PressModifiers.Shift) ClipsView.SelectClip(this, SelectionMode.Inclusive);
			else if (left.PressModifiers.Control) ClipsView.DeselectClip(this);
			// ExclusiveIfUnselected rather than Exclusive: dragging a clip that is
			// already part of a multi-selection has to take the rest of the selection
			// with it. if this turns out to be a click and not a drag, the Click case
			// collapses it afterwards
			else ClipsView.SelectClip(this, SelectionMode.ExclusiveIfUnselected);
		}

		// where the drag began, and how far it has travelled since
		(Vector2 start, Vector2 delta) Drag(MouseButtonState left)
			=> (left.ClickStartPosition, InputManager.Singleton.Mouse.GetDragDelta(left));
	}

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
