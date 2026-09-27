using System;

// a path property whose Browse button picks a folder
[AttributeUsage(AttributeTargets.Property)]
public sealed class FolderPathAttribute : Attribute;
