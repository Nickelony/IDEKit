namespace Nickelony.IDEKit.Core.Formatting;

/// <summary>Formats the text of a whole document without access to an editor.</summary>
public interface IDocumentFormatter
{
	/// <summary>Formats the supplied document content, or declines the document.</summary>
	/// <returns>The formatted document content, or <see langword="null"/> when the formatter declines.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// Returning <see langword="null"/> means the formatter declines the document, which callers treat as no changes. A formatter that ran
	/// and changed nothing returns the supplied instance unchanged, so callers can detect a no-op by reference.
	/// </remarks>
	string? FormatDocument(string content);
}
