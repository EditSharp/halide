using EditSharp.Components;
using EditSharp.Components.Channels;
using EditSharp.History;
using EditSharpGUI.Scripts.UI.ContextMenu;
using Godot;
using System;

public partial class UIChannelEdit : PanelContainer
{
	[ExportGroup("Controls")]

	[Export] LineEdit channelName;

	// the toggles along the bottom: mute (audio) or hide (video), and solo.
	// they flip their look only, until the model carries the state
	[Export] BaseButton muteOrHide;
	[Export] BaseButton solo;

	[Export] ContextMenu menu;

	// reference to actual channel data under the hood
	public Channel Channel;
	public UITimeline UITimeline;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		channelName.Text = Channel.Name;

		// the name is written back when typing ends, either way it ends
		channelName.TextSubmitted += _ => CommitName();
		channelName.FocusExited += CommitName;

		if (muteOrHide is Button toggle) toggle.Text = Channel is VideoChannel ? "Hide" : "Mute";

		Refresh();
	}

	public override void _GuiInput(InputEvent _)
	{
		ContextTrigger.Handle(this, ShowMenu);
	}

	void CommitName()
	{
		string name = channelName.Text;
		if (name == Channel.Name) return;

		using Transaction.Scope change = UITimeline.History.Begin("Rename channel");
		Channel.Name = name;
		change.Commit();
	}

	// reset chanel edit ui to what is actually stored in data
	public void Refresh()
	{
		CustomMinimumSize = new(
			CustomMinimumSize.X,
			(float)UITimeline.VerticalScale
		);

		// an undo can change the name under the field - but not while it is
		// being typed in
		if (!channelName.HasFocus()) channelName.Text = Channel.Name;
	}

	public void BeginRename()
	{
		channelName.GrabFocus();
		channelName.SelectAll();
	}

	// ---- the menu ----

	void ShowMenu(Vector2 at)
	{
		// a placeholder row stands for a channel that is not on the timeline yet
		if (menu is null || Channel.Timeline is not Timeline timeline) return;

		ContextMenu shown = menu.Clone();

		bool video = Channel is VideoChannel;
		int count = video ? timeline.VideoChannels.Count : timeline.AudioChannels.Count;
		int index = Channel.Index;

		// video channels stack upwards on screen, so "up" is a higher index
		// for video and a lower one for audio
		bool canUp = video ? index < count - 1 : index > 0;
		bool canDown = video ? index > 0 : index < count - 1;

		Wire(shown, "channel.rename", true, BeginRename);
		Wire(shown, "channel.moveUp", canUp, () => Edit("Move channel up", () => { if (video) Channel.MoveUp(); else Channel.MoveDown(); }));
		Wire(shown, "channel.moveDown", canDown, () => Edit("Move channel down", () => { if (video) Channel.MoveDown(); else Channel.MoveUp(); }));
		Wire(shown, "channel.insertAbove", true, () => Insert(above: true));
		Wire(shown, "channel.insertBelow", true, () => Insert(above: false));
		Wire(shown, "channel.remove", count > 1, () => Edit("Remove channel", () => timeline.RemoveChannel(Channel)));

		ContextMenus.ShowContextMenu(shown, at);
	}

	// a new channel of this kind next to this one. a channel is added at the
	// end of its kind, then walked to its place
	void Insert(bool above)
	{
		Timeline timeline = Channel.Timeline;
		bool video = Channel is VideoChannel;

		Edit(above ? "Insert channel above" : "Insert channel below", () =>
		{
			Channel added = video ? timeline.AddChannel(new VideoChannel()) : timeline.AddChannel(new AudioChannel());

			// the new index it should end up at, in its kind's list
			int target = video ? (above ? Channel.Index + 1 : Channel.Index) : (above ? Channel.Index : Channel.Index + 1);

			while (added.Index > target) added.MoveDown();
		});
	}

	void Edit(string description, Action action)
	{
		using (Transaction.Scope change = UITimeline.History.Begin(description))
		{
			action();
			change.Commit();
		}

		UITimeline.Reconcile();
	}

	static void Wire(ContextMenu menu, string id, bool enabled, Action action)
	{
		if (menu.Find<ContextButton>(id) is not ContextButton button) return;
		button.Enabled = enabled;
		button.Pressed += () => action();
	}
}
