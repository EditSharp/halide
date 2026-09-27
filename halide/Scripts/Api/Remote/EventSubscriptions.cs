using EditSharp.History;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Halide.Api.Remote;

/// <summary>The events one remote connection asked for, forwarded to it as notifications while it's connected.</summary>
/// <remarks>
/// project.opened, project.closed, history.changed, layout.changed, media.changed and playback.position
/// (at most ten a second per project). Projects opened later are followed too.
/// </remarks>
public sealed class EventSubscriptions : IDisposable
{
	public static readonly string[] Names = ["project.opened", "project.closed", "history.changed", "layout.changed", "media.changed", "playback.position"];

	readonly IRpcPeer peer;
	readonly HashSet<string> wanted = [];
	readonly List<Action> detach = [];
	readonly HashSet<ProjectHandle> followed = [];
	bool listening;

	public EventSubscriptions(IRpcPeer peer) => this.peer = peer;

	public IReadOnlyCollection<string> Wanted => wanted;

	/// <summary>Adds events by name; returns the ones that exist.</summary>
	public IReadOnlyList<string> Subscribe(IEnumerable<string> names)
	{
		List<string> known = [.. names.Where(n => Names.Contains(n))];
		foreach (string n in known) wanted.Add(n);
		Listen();
		return known;
	}

	public void Unsubscribe(IEnumerable<string> names)
	{
		foreach (string n in names) wanted.Remove(n);
	}

	void Listen()
	{
		if (listening) return;
		listening = true;

		ProjectsService projects = EditSharpApp.Instance.Projects;
		Action<ProjectHandle> opened = p =>
		{
			Send("project.opened", new JsonObject { ["project"] = p.Name });
			Follow(p);
		};
		Action<ProjectHandle> closed = p => Send("project.closed", new JsonObject { ["project"] = p.Name });
		projects.Opened += opened;
		projects.Closed += closed;
		detach.Add(() => { projects.Opened -= opened; projects.Closed -= closed; });

		foreach (ProjectHandle p in projects.All) Follow(p);
	}

	void Follow(ProjectHandle p)
	{
		if (!followed.Add(p)) return;

		EventHandler<HistoryEventArgs> history = (_, e) => Send("history.changed", new JsonObject
		{
			["project"] = p.Name,
			["action"] = e.Action.ToString(),
			["undo"] = p.History.UndoDescription,
		});
		Action layout = () => Send("layout.changed", new JsonObject { ["project"] = p.Name, ["active"] = p.Layout.Active });
		Action media = () => Send("media.changed", new JsonObject { ["project"] = p.Name, ["count"] = p.Media.All.Count });

		ulong last = 0;
		Action<EditSharp.Time> position = t =>
		{
			ulong now = Godot.Time.GetTicksMsec();
			if (now - last < 100) return;
			last = now;
			Send("playback.position", new JsonObject { ["project"] = p.Name, ["seconds"] = t.Seconds });
		};

		p.History.Changed += history;
		p.Layout.Changed += layout;
		p.Media.Changed += media;
		p.Playback.PositionChanged += position;

		detach.Add(() =>
		{
			if (!p.IsOpen) return;
			p.History.Changed -= history;
			p.Layout.Changed -= layout;
			p.Media.Changed -= media;
			p.Playback.PositionChanged -= position;
		});
	}

	void Send(string name, JsonObject parameters)
	{
		if (wanted.Contains(name)) peer.Notify(name, parameters);
	}

	public void Dispose()
	{
		foreach (Action a in detach) a();
		detach.Clear();
		followed.Clear();
		listening = false;
	}
}
