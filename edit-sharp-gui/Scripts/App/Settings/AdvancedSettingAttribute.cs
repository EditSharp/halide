using System;

// a setting hidden until App Settings' Advanced toggle is on
[AttributeUsage(AttributeTargets.Property)]
public sealed class AdvancedSettingAttribute : Attribute;
