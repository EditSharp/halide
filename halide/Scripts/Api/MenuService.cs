using System;
using System.Collections.Generic;
using System.Linq;

namespace Halide.Api;

/// <summary>Items extensions add to the bar menus (File, Edit, View, Playback), each running a command.</summary>
public sealed class MenuService
{
	/// <summary>An item: the menu it's in, the command it runs, and the item it follows (null for the end).</summary>
	public sealed record Item(string Menu, string Command, string After);

	readonly List<Item> items = [];

	public IReadOnlyList<Item> Items => items;

	/// <summary>The items added to one menu.</summary>
	public IEnumerable<Item> In(string menu) => items.Where(i => string.Equals(i.Menu, menu, StringComparison.OrdinalIgnoreCase));

	/// <summary>Puts a command in a bar menu until the returned registration is disposed.</summary>
	/// <param name="menu">File, Edit, View or Playback.</param>
	/// <param name="command">The command's id; its title, shortcut and state show as the menu opens.</param>
	/// <param name="after">The id of the item it follows; null puts it at the end.</param>
	public Registration Add(string menu, string command, string after = null)
	{
		Item item = new(menu, command, after);
		items.Add(item);
		return new Registration(() => items.Remove(item));
	}
}
