using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Represents a single text replacement inside a document.
/// </summary>
/// <remarks>
/// The range uses the line/character unit model documented on <see cref="TextPosition"/>: zero-based
/// positions whose line counts line breaks and whose character counts UTF-16 code units within the line.
/// </remarks>
public sealed record TextEdit
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextEdit"/> record.
	/// </summary>
	/// <param name="range">The range to replace.</param>
	/// <param name="newText">The replacement text.</param>
	/// <exception cref="ArgumentNullException"><paramref name="newText"/> is <see langword="null"/>.</exception>
	public TextEdit(TextPositionRange range, string newText)
	{
		ArgumentNullException.ThrowIfNull(newText);

		Range = range;
		NewText = newText;
	}

	/// <summary>
	/// Gets the range to replace.
	/// </summary>
	public TextPositionRange Range { get; }

	/// <summary>
	/// Gets the replacement text.
	/// </summary>
	public string NewText { get; }
}
