using EditSharp.Components;
using EditSharp.Components.Channels;
using EditSharp.History;
using Xunit;

namespace EditSharp.Tests;

using History = EditSharp.History.History;

// transactions: commit, roll back, nest, suppress; undo and redo walk the entries
public class HistoryTests
{
	static (History History, Timeline Timeline) Fresh()
	{
		History history = new();
		Timeline timeline;
		using (Transaction.Suppress()) timeline = new Timeline();
		return (history, timeline);
	}

	[Fact]
	public void ACommittedScopeIsOneUndoableEntry()
	{
		(History history, Timeline timeline) = Fresh();
		using (Transaction.Scope scope = history.Begin("Add channels"))
		{
			timeline.AddChannel(new VideoChannel());
			timeline.AddChannel(new AudioChannel());
			scope.Commit();
		}

		Assert.Equal(1, history.Position);
		Assert.Equal("Add channels", history.UndoDescription);
		Assert.Equal(2, timeline.Channels.Count);

		Assert.True(history.Undo());
		Assert.Empty(timeline.Channels);
		Assert.Equal("Add channels", history.RedoDescription);

		Assert.True(history.Redo());
		Assert.Equal(2, timeline.Channels.Count);
	}

	[Fact]
	public void AScopeLeftUncommittedRollsBack()
	{
		(History history, Timeline timeline) = Fresh();
		using (history.Begin("Never mind")) timeline.AddChannel(new VideoChannel());
		Assert.Empty(timeline.Channels);
		Assert.Equal(0, history.Position);
	}

	[Fact]
	public void NestedScopesShareTheOuterEntry()
	{
		(History history, Timeline timeline) = Fresh();
		using (Transaction.Scope outer = history.Begin("Outer"))
		{
			using (Transaction.Scope inner = history.Begin("Inner"))
			{
				timeline.AddChannel(new VideoChannel());
				inner.Commit();
			}
			timeline.AddChannel(new VideoChannel());
			outer.Commit();
		}
		Assert.Equal(1, history.Position);
		Assert.Equal("Outer", history.UndoDescription);
	}

	[Fact]
	public void AnUncommittedInnerScopeRollsBackEverything()
	{
		(History history, Timeline timeline) = Fresh();
		using (Transaction.Scope outer = history.Begin("Outer"))
		{
			timeline.AddChannel(new VideoChannel());
			using (history.Begin("Inner")) timeline.AddChannel(new VideoChannel());
			outer.Commit();
		}
		Assert.Empty(timeline.Channels);
	}

	[Fact]
	public void SuppressedChangesAreNotRecorded()
	{
		(History history, Timeline timeline) = Fresh();
		using (Transaction.Suppress()) timeline.AddChannel(new VideoChannel());
		Assert.Equal(0, history.Position);
		Assert.Single(timeline.Channels);
	}

	[Fact]
	public void UndoAndRedoAtTheEndsDoNothing()
	{
		(History history, _) = Fresh();
		Assert.False(history.CanUndo);
		Assert.False(history.CanRedo);
		Assert.False(history.Undo());
		Assert.False(history.Redo());
	}

	[Fact]
	public void ANewEntryDropsTheRedos()
	{
		(History history, Timeline timeline) = Fresh();
		for (int i = 0; i < 2; i++)
		{
			using Transaction.Scope scope = history.Begin($"Step {i}");
			timeline.AddChannel(new VideoChannel());
			scope.Commit();
		}
		history.Undo();
		Assert.True(history.CanRedo);
		using (Transaction.Scope scope = history.Begin("Branch"))
		{
			timeline.AddChannel(new AudioChannel());
			scope.Commit();
		}
		Assert.False(history.CanRedo);
	}

	[Fact]
	public void ChangedReportsEachAction()
	{
		(History history, Timeline timeline) = Fresh();
		System.Collections.Generic.List<HistoryAction> seen = [];
		history.Changed += (_, e) => seen.Add(e.Action);
		using (Transaction.Scope scope = history.Begin("Add"))
		{
			timeline.AddChannel(new VideoChannel());
			scope.Commit();
		}
		history.Undo();
		history.Redo();
		Assert.Equal([HistoryAction.Commit, HistoryAction.Undo, HistoryAction.Redo], seen);
	}
}
