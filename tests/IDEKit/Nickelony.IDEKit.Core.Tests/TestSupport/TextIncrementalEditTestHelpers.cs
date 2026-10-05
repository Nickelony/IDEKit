namespace Nickelony.IDEKit.Core.Tests;

/// <summary>Builds <see cref="TextIncrementalEdit"/> values for the editing tests.</summary>
internal static class TextIncrementalEditTestHelpers
{
	/// <summary>Creates an incremental edit for the given zero-based range and replacement text.</summary>
	internal static TextIncrementalEdit CreateEdit(int offset, int length, string newText) => new(new TextRange(offset, length), newText);
}
