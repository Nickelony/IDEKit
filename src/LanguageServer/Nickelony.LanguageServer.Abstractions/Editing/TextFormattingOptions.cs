namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Specifies indentation preferences for document formatting.
/// </summary>
/// <remarks>
/// The record carries the indentation options the language-server transport supports. Additional
/// options (for example trim-trailing-whitespace or final-newline behavior) cannot be expressed through this
/// contract.
/// </remarks>
public sealed record TextFormattingOptions
{
	/// <summary>
	/// The default indentation width, used when a supplied width is less than one.
	/// </summary>
	public const int DefaultTabSize = 4;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextFormattingOptions"/> record.
	/// </summary>
	/// <param name="tabSize">The width of a tab in spaces. A value less than one uses <see cref="DefaultTabSize"/>.</param>
	/// <param name="insertSpaces"><see langword="true"/> to indent with spaces; otherwise, tabs.</param>
	public TextFormattingOptions(int tabSize, bool insertSpaces)
	{
		TabSize = tabSize > 0 ? tabSize : DefaultTabSize;
		InsertSpaces = insertSpaces;
	}

	/// <summary>
	/// Gets the width of a tab in spaces.
	/// </summary>
	public int TabSize { get; }

	/// <summary>
	/// Gets a value indicating whether indentation uses spaces instead of tabs.
	/// </summary>
	public bool InsertSpaces { get; }
}
