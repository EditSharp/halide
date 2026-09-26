using EditSharp.Editing;
using System;
using System.Globalization;

namespace EditSharpGUI.Scripts.UI.Inspecting;

// what an editor needs to know about the value it edits, apart from the
// value: which editor, the type to hand back, and the attribute's limits
public sealed record EditorSpec(
	PropertyEditor Editor,
	Type ValueType,
	double? Min,
	double? Max,
	double? Step,
	string Unit,
	bool ReadOnly,
	bool Nullable,
	string Tooltip,
	Choice[] Choices = null,
	FrameMeasure Frame = FrameMeasure.None)
{
	public static EditorSpec Of(PropertyDescriptor d)
		=> new(d.Editor, d.ValueType, d.Min, d.Max, d.Step, d.Unit, d.IsReadOnly, d.IsNullable, d.Tooltip, Frame: d.Frame);

	public static EditorSpec OfItem(PropertyDescriptor d)
		=> new(d.ItemEditor, d.ItemValueType, d.Min, d.Max, d.Step, d.Unit, false, false, d.Tooltip, Frame: d.Frame);

	public bool HasRange => Min.HasValue && Max.HasValue;

	// how many decimals a number with this step is shown to
	public int Decimals => Step is double step and > 0d ? Math.Clamp((int)Math.Ceiling(-Math.Log10(step)), 0, 6) : 3;

	// the value as the property's own type - editors work in doubles and
	// strings, properties are floats, ints, enums
	public object Coerce(object value)
	{
		Type type = ValueType;

		if (value is null || type.IsInstanceOfType(value)) return value;

		if (type.IsEnum) return value is string name ? Enum.Parse(type, name, ignoreCase: true) : Enum.ToObject(type, value);

		// a typed number becomes the exact decimal it reads as: 1.5025 is 601/400
		if (type == typeof(Rational))
		{
			return value switch
			{
				string text => Rational.Parse(text),
				double or float => Rational.FromDecimal((decimal)Convert.ToDouble(value, CultureInfo.InvariantCulture)),
				IConvertible => Rational.FromDecimal(Convert.ToDecimal(value, CultureInfo.InvariantCulture)),
				_ => value,
			};
		}

		if (value is IConvertible) return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);

		return value;
	}
}
