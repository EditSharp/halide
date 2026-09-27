using System;
using System.Collections.Generic;

namespace EditSharpGUI.Api;

/// <summary>Pages extensions add to App Settings: an object whose [Editable] properties the page shows, like the built-in pages.</summary>
public sealed class SettingsPageRegistry
{
	/// <summary>A page: its sidebar title, and the object it edits.</summary>
	public sealed record Page(string Title, object Target);

	readonly List<Page> pages = [];

	public IReadOnlyList<Page> Pages => pages;

	/// <summary>A page was added or removed.</summary>
	public event Action Changed;

	public Registration Register(string title, object target)
	{
		Page page = new(title, target);
		pages.Add(page);
		Changed?.Invoke();
		return new Registration(() =>
		{
			if (pages.Remove(page)) Changed?.Invoke();
		});
	}
}
