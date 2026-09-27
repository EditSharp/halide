using Godot;
using SkiaSharp;
using System;

namespace Halide.Scripts.UI.Inspecting;

// a swatch that opens a picker, with the hex beside it (Color.tscn). the
// picker's changes are live - the value moves while the user drags around
// the wheel - and settle when the popup closes
[Tool]
public partial class ColorEditor : ValueEditor
{
	[Export] Button swatch;
	[Export] ColorRect fill;
	[Export] LineEdit hex;
	[Export] PopupPanel popup;
	[Export] ColorPicker picker;

	Color current = Colors.White;
	string shownHex = "";

	public override bool IsEditing => hex.HasFocus();

	protected override void Build()
	{
		swatch.Disabled = ReadOnly;
		swatch.TooltipText = Spec.Tooltip ?? "";
		swatch.Pressed += Open;

		hex.Editable = !ReadOnly;
		hex.TextSubmitted += _ => { CommitHex(); hex.ReleaseFocus(); };
		hex.FocusExited += CommitHex;

		picker.ColorChanged += c => { Show(c); RaiseChanged(ToSk(c)); };
		popup.PopupHide += () => { RaiseCommitted(ToSk(current)); RaiseEnded(); };
	}

	void Open()
	{
		picker.Color = current;

		RaiseBegan();

		Vector2 below = swatch.GlobalPosition + new Vector2(0f, swatch.Size.Y + 2f);
		popup.Popup(new Rect2I((Vector2I)below, Vector2I.Zero));
	}

	void CommitHex()
	{
		if (ReadOnly || hex.Text == shownHex) return;

		string text = hex.Text.Trim();
		if (!text.StartsWith('#')) text = "#" + text;

		if (!Color.HtmlIsValid(text))
		{
			hex.Text = shownHex;
			return;
		}

		Color c = Color.FromHtml(text);
		Show(c);
		RaiseCommitted(ToSk(c));
	}

	void Show(Color c)
	{
		current = c;
		fill.Color = c;
		shownHex = "#" + c.ToHtml(c.A < 1f);
		if (!IsEditing) hex.Text = shownHex;
	}

	public override void Display(object value, bool mixed)
	{
		if (mixed || value is not SKColor sk)
		{
			fill.Color = Colors.Transparent;
			shownHex = "";
			hex.PlaceholderText = mixed ? "—" : "";
			if (!IsEditing) hex.Text = "";
			return;
		}

		hex.PlaceholderText = "";
		Show(ToGodot(sk));
	}

	public static Color ToGodot(SKColor c) => new(c.Red / 255f, c.Green / 255f, c.Blue / 255f, c.Alpha / 255f);

	public static SKColor ToSk(Color c) => new(
		(byte)Math.Round(Math.Clamp(c.R, 0f, 1f) * 255f),
		(byte)Math.Round(Math.Clamp(c.G, 0f, 1f) * 255f),
		(byte)Math.Round(Math.Clamp(c.B, 0f, 1f) * 255f),
		(byte)Math.Round(Math.Clamp(c.A, 0f, 1f) * 255f));
}
