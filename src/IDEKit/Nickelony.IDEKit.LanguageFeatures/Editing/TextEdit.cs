using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.Editing;

/// <summary>Replaces one range of a document with new text.</summary>
/// <remarks>
/// This is the language-server edit shape: a line and character range with its replacement text. The range uses the line/character unit
/// model documented on <see cref="TextPosition"/>: zero-based positions whose line counts line breaks and whose character counts UTF-16 code units
/// within the line.
/// </remarks>
public sealed record TextEdit
{
	/// <param name="range">The range to replace.</param>
	/// <param name="newText">The replacement text.</param>
	/// <exception cref="ArgumentNullException"><paramref name="newText"/> is <see langword="null"/>.</exception>
	public TextEdit(TextPositionRange range, string newText)
	{
		ArgumentNullException.ThrowIfNull(newText);

		Range = range;
		NewText = newText;
	}

	public TextPositionRange Range { get; }

	public string NewText { get; }
}
