using Godot;
using System;
using Vector2 = System.Numerics.Vector2;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// ---- a time, as frames or as a clock, whichever the inspector is showing (Time.tscn) ----

public partial class TimeEditor : ValueEditor
{
	[Export] SpinSlider frames;
	[Export] LineEdit clock;
	[Export] Button toggle;

	string shownClock = "";

	public override bool IsEditing => clock.HasFocus() || frames.IsEditing;

	int Framerate => Math.Max(1, Inspector?.Framerate ?? 30);

	protected override void Build()
	{
		frames.Step = 1d;
		frames.Decimals = 0;
		frames.Unit = "f";
		frames.ReadOnly = ReadOnly;
		frames.TooltipText = Spec.Tooltip ?? "";
		frames.DragBegan += RaiseBegan;
		frames.Changed += f => RaiseChanged(FromFrames(f));
		frames.Committed += f => RaiseCommitted(FromFrames(f));
		frames.DragEnded += RaiseEnded;

		clock.Editable = !ReadOnly;
		clock.TooltipText = Spec.Tooltip ?? "";
		clock.TextSubmitted += _ => { CommitClock(); clock.ReleaseFocus(); };
		clock.FocusExited += CommitClock;

		toggle.Pressed += () => { if (Inspector is not null) Inspector.ShowFrames = !Inspector.ShowFrames; };
	}

	TimeSpan FromFrames(double f) => TimeSpan.FromTicks((long)Math.Round(f / Framerate * TimeSpan.TicksPerSecond));

	void CommitClock()
	{
		if (ReadOnly || clock.Text == shownClock) return;

		if (string.IsNullOrWhiteSpace(clock.Text))
		{
			if (Spec.Nullable) { shownClock = ""; RaiseCommitted(null); }
			else clock.Text = shownClock;
			return;
		}

		if (!TryParseTime(clock.Text, out TimeSpan t))
		{
			clock.Text = shownClock;
			return;
		}

		shownClock = FormatTime(t);
		RaiseCommitted(t);
	}

	public override void Display(object value, bool mixed)
	{
		bool showFrames = Inspector?.ShowFrames ?? false;

		frames.Visible = showFrames;
		clock.Visible = !showFrames;
		toggle.Text = showFrames ? "f" : "t";

		if (mixed || value is not TimeSpan t)
		{
			frames.Display(null);
			shownClock = "";
			clock.PlaceholderText = mixed ? "—" : "(none)";
			if (!clock.HasFocus()) clock.Text = "";
			return;
		}

		frames.Display(Math.Round(t.TotalSeconds * Framerate));
		shownClock = FormatTime(t);
		clock.PlaceholderText = "";
		if (!clock.HasFocus()) clock.Text = shownClock;
	}
}
