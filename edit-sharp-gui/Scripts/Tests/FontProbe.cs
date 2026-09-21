using Godot;
using System.Collections.Generic;
using System.Linq;

// renders sample text with each font file under the app's import settings
// and saves the result, so overlap artefacts and weights can be seen. run
// with a window:  godot --path . res://Scenes/Tests/FontProbe.tscn
public partial class FontProbe : Control
{
	static readonly (string Label, string Path)[] Files =
	[
		("Flattened Regular", "res://Fonts/InterRegularNoOverlap.ttf"),
		("Variable, flags", "res://Fonts/InterVariable.ttf"),
		("Static Regular", "res://Fonts/Inter-Regular.ttf"),
		("Static Bold", "res://Fonts/Inter-Bold.ttf"),
		("Mono variable", "res://Fonts/JetBrainsMonoVariable.ttf"),
		("Mono static", "res://Fonts/JetBrainsMono-Regular.ttf"),
	];

	const string Sample = "Weight check AKMRWXeksy@&g 0123 Ω";

	public override void _Ready()
	{
		// drawn into a viewport of its own, so the picture is not bound to the window
		SubViewport view = new() { Size = new Vector2I(1500, 1500), RenderTargetUpdateMode = SubViewport.UpdateMode.Always, TransparentBg = false };
		AddChild(view);
		ColorRect back = new() { Color = new Color(0.16f, 0.16f, 0.16f), Size = new Vector2(1500f, 1500f) };
		view.AddChild(back);
		VBoxContainer box = new() { Position = new Vector2(8f, 8f) };
		view.AddChild(box);
		snapshotFrom = view;

		foreach ((string label, string path) in Files)
		{
			foreach (bool msdf in new[] { true, false })
			{
				FontFile font = new();
				font.LoadDynamicFont(path);
				font.MultichannelSignedDistanceField = msdf;
				font.MsdfPixelRange = 8;
				font.MsdfSize = 48;
				font.Antialiasing = TextServer.FontAntialiasing.Gray;
				font.Hinting = TextServer.Hinting.None;
				font.SubpixelPositioning = TextServer.SubpixelPositioning.OneQuarter;

				var axes = font.GetSupportedVariationList();
				string axesText = string.Join(",", axes.Keys.Select(k => TextServerManager.GetPrimaryInterface().TagToName(k.AsInt64())));

				foreach (int weight in new[] { 400, 700 })
				{
					FontVariation v = new() { BaseFont = font };
					using (Godot.Collections.Dictionary d = new() { [TextServerManager.GetPrimaryInterface().NameToTag("wght")] = weight }) v.VariationOpentype = d;

					foreach (int size in weight == 400 ? new[] { 14 } : new[] { 14, 30 })
					{
						Label line = new() { Text = $"{label} {(msdf ? "msdf" : "ft")} [{axesText}] w{weight} {size}px  {Sample}" };
						line.AddThemeColorOverride("font_color", new Color(0.92f, 0.92f, 0.92f));
						line.AddThemeFontOverride("font", v);
						line.AddThemeFontSizeOverride("font_size", size);
						box.AddChild(line);
					}
				}
			}
		}

		Callable.From(Snap).CallDeferred();
	}

	int frames;
	SubViewport snapshotFrom;

	public override void _Process(double delta)
	{
		if (++frames == 20) Snap();
	}

	void Snap()
	{
		if (frames < 20) return;
		Image image = snapshotFrom.GetTexture().GetImage();
		string file = ProjectSettings.GlobalizePath("user://font_probe.png");
		image.SavePng(file);
		GD.Print("saved " + file);
		GetTree().Quit();
	}
}
