using EditSharp;
using EditSharp.Playback;
using System;

namespace EditSharpGUI.Api;

/// <summary>A project's program viewer: playing, pausing, shuttling and stepping through the timeline.</summary>
public sealed class PlaybackService
{
	readonly ProjectHandle project;

	internal PlaybackService(ProjectHandle project) => this.project = project;

	UIPlayback View => project.Editor.ProgramView;

	public PlaybackState State => View.State;

	public bool Playing => State == PlaybackState.Playing;

	/// <summary>Where playback is; the playhead follows it.</summary>
	public Time Position => project.Timeline.Playhead;

	/// <summary>Plays forward at normal speed, from where it is.</summary>
	public void Play()
	{
		if (!Playing) View.TogglePlayback();
	}

	public void Pause()
	{
		if (Playing) View.TogglePlayback();
	}

	public void Toggle() => View.TogglePlayback();

	/// <summary>Moves playback and the playhead to <paramref name="time"/>.</summary>
	public void Seek(Time time) => project.Timeline.Playhead = time;

	/// <summary>Plays faster each call, forward or backward, as J and L do.</summary>
	public void Shuttle(bool forward) => View.Shuttle(forward ? 1 : -1);

	/// <summary>Steps whole frames forward or back while paused.</summary>
	public void Step(int frames)
	{
		for (int i = 0; i < Math.Abs(frames); i++) View.StepFrame(Math.Sign(frames));
	}

	/// <summary>Whether playback goes round at the ends instead of stopping.</summary>
	public bool Loop
	{
		get => View.Loop;
		set => View.Loop = value;
	}

	/// <summary>Playback moved, while playing or scrubbing.</summary>
	public event Action<Time> PositionChanged
	{
		add => Subscribe(value, true);
		remove => Subscribe(value, false);
	}

	readonly System.Collections.Generic.Dictionary<Action<Time>, EventHandler<Time>> wrapped = [];

	void Subscribe(Action<Time> handler, bool add)
	{
		if (add)
		{
			EventHandler<Time> inner = (_, t) => handler(t);
			wrapped[handler] = inner;
			View.PositionChanged += inner;
		}
		else if (wrapped.Remove(handler, out EventHandler<Time> inner)) View.PositionChanged -= inner;
	}
}
