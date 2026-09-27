using EditSharp.Components;
using EditSharp.Components.Media;
using Halide.Scripts.Input;
using Halide.Scripts.UI;
using Halide.Scripts.UI.ContextMenu;
using Halide.Scripts.UI.Theming;
using Halide.Scripts.UI.Thumbnails;
using Godot;
using System;

// one tile in the media viewer: a media or a timeline. the picture area
// keeps the project's aspect, the name sits under it, an options button
// hangs in the top right corner and a kind icon in the bottom left. laid
// out in MediaItem.tscn; the viewer sizes it and answers its gestures
public partial class UIMediaItem : VBoxContainer, IDragCancellable
{
	[ExportGroup("Controls")]

	[Export] Control content;
	[Export] TextureRect picture;
	[Export] WaveformView waveform;
	[Export] TextureRect kindIcon;
	[Export] Button options;
	[Export] Panel outline;
	[Export] ProgressBar proxy;
	[Export] Label nameLabel;
	[Export] LineEdit nameEdit;

	// what the tile stands for: an IMedia or a Timeline
	public object Subject { get; private set; }
	public MediaViewer Viewer { get; private set; }

	public IMedia Media => Subject as IMedia;
	public Timeline Timeline => Subject as Timeline;

	public bool Selected
	{
		get => outline?.Visible ?? false;
		set { if (outline is not null) outline.Visible = value; }
	}


	public void Setup(MediaViewer viewer, object subject)
	{
		Viewer = viewer;
		Subject = subject;
		if (IsNodeReady()) Refresh();
	}

	public override void _Ready()
	{
		if (options is not null) options.Pressed += () => Viewer?.ShowTileMenu(this, options);

		if (nameEdit is not null)
		{
			nameEdit.Visible = false;
			nameEdit.TextSubmitted += _ => EndRename(commit: true);
			nameEdit.FocusExited += () => EndRename(commit: true);
		}

		if (proxy is not null) proxy.Visible = false;

		Refresh();
	}

	// the picture area's size, from the viewer: the project aspect scaled
	// to the tile's long side
	public void SetPictureSize(Vector2 size)
	{
		if (content is null) return;
		content.CustomMinimumSize = size;
		CustomMinimumSize = new(size.X, 0f);
	}

	// everything shown, from the subject as it is now
	public void Refresh()
	{
		if (Subject is null || !IsNodeReady()) return;

		string name = Viewer?.NameOf(Subject) ?? Subject.ToString();
		if (nameLabel is not null && nameLabel.Text != name) nameLabel.Text = name;

		if (kindIcon is not null)
		{
			kindIcon.Texture = IconRaster.Get(Viewer?.KindOf(Subject) switch
			{
				MediaViewer.Kind.Video => IconRaster.Shape.Film,
				MediaViewer.Kind.Image => IconRaster.Shape.Picture,
				MediaViewer.Kind.Audio => IconRaster.Shape.Speaker,
				MediaViewer.Kind.Timeline => IconRaster.Shape.Timeline,
				_ => IconRaster.Shape.Dot
			}, 18);

			kindIcon.Modulate = GetThemeColor("font_color", "Label");
		}

		bool offline = Media is IMedia m && !string.IsNullOrEmpty(m.Path) && !System.IO.File.Exists(m.Path);
		Modulate = offline ? new Color(1f, 1f, 1f, 0.5f) : Colors.White;
		if (nameLabel is not null) nameLabel.TooltipText = offline ? $"Offline: {Media.Path}" : Media?.Path ?? "";

		RefreshPicture();
	}

	// asks the caches for the picture or the peaks; they call back when ready
	public void RefreshPicture()
	{
		if (Viewer?.Thumbnails is not MediaThumbnails thumbnails || content is null) return;

		Vector2 size = content.CustomMinimumSize;
		int width = Mathf.Max(8, Mathf.RoundToInt(size.X * 2f));
		int height = Mathf.Max(8, Mathf.RoundToInt(size.Y * 2f));

		if (Subject is AudioMedia audio)
		{
			if (picture is not null) picture.Visible = false;
			if (waveform is null) return;

			EnvelopeTexture envelope = thumbnails.GetEnvelope(audio);
			waveform.SetEnvelope(envelope);

			if (envelope is not null)
			{
				Color color = ClipColors.Waveform(this, GetThemeColor("audio", "Clip"));
				waveform.SetColors(color, color.Lightened(0.35f));
				waveform.SetWindow(0d, envelope.FrameCount * envelope.FrameSeconds / Mathf.Max(1f, size.X - 6f));
			}

			return;
		}

		if (waveform is not null) waveform.Visible = false;
		if (picture is null) return;

		Texture2D frame = thumbnails.GetFrame(Subject, width, height);
		picture.Texture = frame;
		picture.Visible = frame is not null;
	}

	// the proxy build's progress, 0 to 1; null hides the bar
	public void SetProxyProgress(double? progress)
	{
		if (proxy is null) return;

		proxy.Visible = progress is not null;
		if (progress is double p) proxy.Value = p * proxy.MaxValue;
	}

	// ---- renaming ----

	bool renaming;

	public void BeginRename()
	{
		if (nameEdit is null || renaming || Media is null) return;

		renaming = true;
		nameEdit.Text = Media.Name;
		nameEdit.Visible = true;
		nameLabel.Visible = false;
		nameEdit.GrabFocus();
		nameEdit.SelectAll();
	}

	void EndRename(bool commit)
	{
		if (!renaming) return;
		renaming = false;

		nameEdit.Visible = false;
		nameLabel.Visible = true;

		string name = nameEdit.Text.Trim();
		if (commit && Media is not null && name != Media.Name) Viewer?.Rename(Media, name);
	}

	// ---- gestures ----

	public void CancelDrag(MouseButtonState button) => Viewer?.CancelTileDrag(this);

	public override void _GuiInput(InputEvent _)
	{
		MouseButtonState left = InputManager.Singleton.Mouse.LeftButton;

		switch (left.Action)
		{
			case MouseAction.Press:
				if (!left.Capture(this)) break;
				Viewer?.PressTile(this, left.PressModifiers);
				break;

			case MouseAction.DoubleClick:
				if (!left.Capture(this)) break;
				Viewer?.OpenTile(this);
				break;

			case MouseAction.DragStart:
				if (!left.HasCapture(this)) break;
				Viewer?.BeginTileDrag(this);
				goto case MouseAction.DragMove;

			case MouseAction.DragMove:
				if (!left.HasCapture(this)) break;
				Viewer?.UpdateTileDrag(this);
				break;

			case MouseAction.DragEnd:
				if (!left.HasCapture(this)) break;
				Viewer?.FinishTileDrag(this);
				break;

			case MouseAction.Click:
				if (!left.HasCapture(this)) break;
				if (left.PressModifiers.Shift || left.PressModifiers.Control) break;
				Viewer?.ClickTile(this);
				break;
		}

		ContextTrigger.Handle(this, at => Viewer?.ShowTileMenu(this, at));
	}

	public override void _Notification(int what)
	{
		if (what == NotificationThemeChanged && IsNodeReady()) Refresh();
	}
}
