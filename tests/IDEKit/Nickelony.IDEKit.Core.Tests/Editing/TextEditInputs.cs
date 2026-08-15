namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Builds <see cref="TextEditInput"/> values for the editing tests.
/// </summary>
internal static class TextEditInputs
{
	/// <summary>Creates a text edit input for the given zero-based range and replacement text.</summary>
	internal static TextEditInput CreateEdit(int offset, int length, string newText)
		=> new(new TextRange(offset, length), newText);
}
