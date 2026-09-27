#if TOOLS
using Halide.Scripts.UI.Theming;
using Godot;
using Godot.Collections;
using System;
using System.Linq;

// one theme type's bare items as properties godot's inspector can edit: a
// definition, shade and alpha for each colour item, the font family, weight
// and size role, and each constant. reads and writes go straight to the
// theme's bindings and items, so the inspector's own undo covers them
[Tool]
public partial class TypeThemeProxy : GodotObject
{
	readonly EditSharpTheme theme;
	readonly string type;

	public TypeThemeProxy() { }

	public TypeThemeProxy(EditSharpTheme theme, string type)
	{
		this.theme = theme;
		this.type = type;
	}

	public bool HasAnything => theme is not null;

	static string Names<T>() where T : struct, Enum => string.Join(",", Enum.GetNames<T>());

	public override Array<Dictionary> _GetPropertyList()
	{
		Array<Dictionary> list = [];
		if (theme is null) return list;

		foreach (string item in theme.GetColorList(type).OrderBy(i => i))
		{
			list.Add(Property($"colors/{item}/source", Variant.Type.Int, PropertyHint.Enum, Names<ColorSource>()));
			list.Add(Property($"colors/{item}/definition", Variant.Type.Int, PropertyHint.Enum, Names<ThemeDefinition>()));
			list.Add(Property($"colors/{item}/clip_swatch", Variant.Type.Int, PropertyHint.Enum, Names<ClipSwatch>()));
			list.Add(Property($"colors/{item}/node_swatch", Variant.Type.Int, PropertyHint.Enum, Names<NodeSwatch>()));
			list.Add(Property($"colors/{item}/shade", Variant.Type.Int, PropertyHint.Enum, Names<ThemeShade>()));
			list.Add(Property($"colors/{item}/alpha", Variant.Type.Float, PropertyHint.Range, "0,1,0.01"));
		}

		list.Add(Property("font/family", Variant.Type.Int, PropertyHint.Enum, Names<ThemeFontFamily>()));
		list.Add(Property("font/weight", Variant.Type.Int, PropertyHint.Enum, Names<ThemeFontWeight>()));
		list.Add(Property("font/size", Variant.Type.Int, PropertyHint.Enum, Names<ThemeFontSizeRole>()));

		foreach (string item in theme.GetConstantList(type).OrderBy(i => i))
			list.Add(Property($"constants/{item}", Variant.Type.Int, PropertyHint.Range, "-64,256,1"));

		return list;
	}

	static Dictionary Property(string name, Variant.Type type, PropertyHint hint, string hintString) => new()
	{
		["name"] = name,
		["type"] = (int)type,
		["hint"] = (int)hint,
		["hint_string"] = hintString,
		["usage"] = (int)PropertyUsageFlags.Default
	};

	public override Variant _Get(StringName property)
	{
		if (theme is null) return default;
		string name = property;

		if (name.StartsWith("colors/", StringComparison.Ordinal))
		{
			(string item, string field) = Split(name[7..]);
			ColorBinding binding = theme.FindColorBinding(type, item);

			return field switch
			{
				"source" => (int)(binding?.Source ?? ColorSource.Definition),
				"definition" => (int)(binding?.Definition ?? ThemeDefinition.None),
				"clip_swatch" => (int)(binding?.ClipSwatch ?? ClipSwatch.Poppy),
				"node_swatch" => (int)(binding?.NodeSwatch ?? NodeSwatch.Poppy),
				"shade" => (int)(binding?.Shade ?? ThemeShade.None),
				"alpha" => binding?.Alpha ?? 1f,
				_ => default
			};
		}

		if (name.StartsWith("font/", StringComparison.Ordinal))
		{
			FontBinding binding = theme.FindFontBinding(type);

			return name[5..] switch
			{
				"family" => (int)(binding?.Family ?? ThemeFontFamily.None),
				"weight" => (int)(binding?.Weight ?? ThemeFontWeight.Regular),
				"size" => (int)(binding?.Size ?? ThemeFontSizeRole.None),
				_ => default
			};
		}

		if (name.StartsWith("constants/", StringComparison.Ordinal))
		{
			string item = name[10..];
			return theme.HasConstant(item, type) ? theme.GetConstant(item, type) : 0;
		}

		return default;
	}

	public override bool _Set(StringName property, Variant value)
	{
		if (theme is null) return false;
		string name = property;

		if (name.StartsWith("colors/", StringComparison.Ordinal))
		{
			(string item, string field) = Split(name[7..]);
			ColorBinding binding = theme.FindColorBinding(type, item);

			if (binding is null)
			{
				binding = new ColorBinding { ThemeType = type, Item = item, Definition = ThemeDefinition.None };
				theme.ColorBindings.Add(binding);
			}

			switch (field)
			{
				case "source": binding.Source = (ColorSource)value.AsInt32(); break;
				case "definition": binding.Definition = (ThemeDefinition)value.AsInt32(); break;
				case "clip_swatch": binding.ClipSwatch = (ClipSwatch)value.AsInt32(); break;
				case "node_swatch": binding.NodeSwatch = (NodeSwatch)value.AsInt32(); break;
				case "shade": binding.Shade = (ThemeShade)value.AsInt32(); break;
				case "alpha": binding.Alpha = (float)value.AsDouble(); break;
				default: return false;
			}

			theme.ApplyBindings();
			return true;
		}

		if (name.StartsWith("font/", StringComparison.Ordinal))
		{
			FontBinding binding = theme.FindFontBinding(type);

			if (binding is null)
			{
				binding = new FontBinding { ThemeType = type, Family = ThemeFontFamily.UI };
				theme.FontBindings.Add(binding);
			}

			switch (name[5..])
			{
				case "family": binding.Family = (ThemeFontFamily)value.AsInt32(); break;
				// a weight is a weight of the UI font: picking one without a family means that
				case "weight": binding.Weight = (ThemeFontWeight)value.AsInt32(); if (binding.Family == ThemeFontFamily.None) binding.Family = ThemeFontFamily.UI; break;
				case "size": binding.Size = (ThemeFontSizeRole)value.AsInt32(); break;
				default: return false;
			}

			theme.ApplyBindings();
			return true;
		}

		if (name.StartsWith("constants/", StringComparison.Ordinal))
		{
			theme.SetConstant(name[10..], type, value.AsInt32());
			return true;
		}

		return false;
	}

	static (string Item, string Field) Split(string rest)
	{
		int slash = rest.LastIndexOf('/');
		return slash < 0 ? (rest, "") : (rest[..slash], rest[(slash + 1)..]);
	}
}
#endif
