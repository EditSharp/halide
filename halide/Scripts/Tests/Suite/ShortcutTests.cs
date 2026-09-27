using Halide.Scripts.Input;
using Godot;
using System.IO;
using System.Linq;

namespace Halide.Tests;

// key combos and the shortcut map
[TestFixture]
public sealed class ShortcutTests
{
	[Test]
	public void CombosReadAndWriteTheSameWay()
	{
		foreach (KeyCombo combo in new KeyCombo[] { new(Key.A), new(Key.S, Control: true), new(Key.Z, Control: true, Shift: true), new(Key.Key1, Shift: true, Alt: true), new(Key.F11) })
		{
			Assert.True(KeyCombo.TryParse(combo.ToString(), out KeyCombo back), combo.ToString());
			Assert.Equal(combo, back);
		}
	}

	[Test]
	public void CombosParseLeniently()
	{
		Assert.True(KeyCombo.TryParse(" ctrl + shift + a ", out KeyCombo combo));
		Assert.Equal(new KeyCombo(Key.A, Control: true, Shift: true), combo);
		Assert.True(KeyCombo.TryParse("Cmd+Comma", out combo), "cmd is ctrl");
		Assert.True(combo.Control);
	}

	[Test]
	public void BadCombosDontParse()
	{
		foreach (string text in new[] { "", "  ", "Ctrl", "Ctrl+Shift", "Ctrl+NotAKey", "+" })
			Assert.False(KeyCombo.TryParse(text, out _), $"'{text}'");
	}

	[Test]
	public void TheMapStartsWithTheDefaults()
	{
		ShortcutMap map = new();
		foreach ((string action, _, _) in Shortcuts.Catalog)
			Assert.Sequence(ShortcutMap.DefaultsFor(action), map.Get(action), action);
	}

	[Test]
	public void BindingUnbindingAndLookingUp()
	{
		ShortcutMap map = new();
		KeyCombo combo = new(Key.J, Control: true, Alt: true);
		map.Bind("test.action", combo);
		Assert.Contains("test.action", map.ActionsFor(combo));
		map.Unbind("test.action");
		Assert.Count(0, map.Get("test.action"));
		Assert.DoesNotContain("test.action", map.ActionsFor(combo));
	}

	[Test]
	public void TheMapSavesAndLoads()
	{
		string file = Path.Combine(TestApp.Sandbox, "shortcuts-test.json");
		ShortcutMap map = new();
		map.Bind(Shortcuts.Save, new KeyCombo(Key.F2, Control: true));
		map.Bind(Shortcuts.Redo, new KeyCombo(Key.Y, Control: true), new KeyCombo(Key.R, Control: true));
		map.Save(file);

		ShortcutMap loaded = new();
		loaded.Load(file);
		Assert.Sequence([new KeyCombo(Key.F2, Control: true)], loaded.Get(Shortcuts.Save));
		Assert.Count(2, loaded.Get(Shortcuts.Redo));
		Assert.Sequence(ShortcutMap.DefaultsFor(Shortcuts.Copy), loaded.Get(Shortcuts.Copy), "unmentioned actions keep their defaults");
	}

	[Test]
	public void ExtraShortcutsAreListedAfterTheCatalogue()
	{
		Shortcuts.Extra.Add(("test.extra", "Test", "Extra"));
		try
		{
			Assert.Equal("test.extra", Shortcuts.All.Last().Action);
			Assert.Equal(Shortcuts.Catalog.Length + 1, Shortcuts.All.Count());
		}
		finally { Shortcuts.Extra.Clear(); }
	}
}
