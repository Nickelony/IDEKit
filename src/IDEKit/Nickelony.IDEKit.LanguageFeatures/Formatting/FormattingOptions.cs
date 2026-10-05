namespace Nickelony.IDEKit.LanguageFeatures.Formatting;

/// <summary>Specifies indentation preferences for document formatting.</summary>
/// <remarks>
/// The record carries the indentation options the language-server client sends. Additional options (for example trim-trailing-whitespace
/// or final-newline behavior) cannot be expressed through this contract.
/// </remarks>
public sealed record FormattingOptions
{
	/// <summary>The default indentation width, used when a supplied width is less than one.</summary>
	public const int DefaultTabSize = 4;

	/// <param name="tabSize">The width of a tab in spaces. A value less than one uses <see cref="DefaultTabSize"/>.</param>
	/// <param name="insertSpaces"><see langword="true"/> to indent with spaces; otherwise, tabs.</param>
	public FormattingOptions(int tabSize, bool insertSpaces)
	{
		TabSize = tabSize > 0 ? tabSize : DefaultTabSize;
		InsertSpaces = insertSpaces;
	}

	/// <summary>The width of a tab in spaces.</summary>
	public int TabSize { get; }

	public bool InsertSpaces { get; }
}
