namespace Nickelony.IDEKit.Core.Formatting;

/// <summary>
/// Formats the content of a text document.
/// </summary>
public interface ITextDocumentFormatter
{
	/// <summary>
	/// Formats the supplied document content, or declines the document.
	/// </summary>
	/// <remarks>
	/// Returning <see langword="null"/> means the formatter declines the document (for example when
	/// the host cannot format it), which callers treat as "no changes". A formatter that ran and
	/// found nothing to change reports that by returning the supplied content unchanged (the same
	/// instance), so callers can detect a no-op by reference and no result needs to be invented for
	/// either case.
	/// </remarks>
	/// <param name="content">The document content to format.</param>
	/// <returns>The formatted document content, or <see langword="null"/> when the formatter declines.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
	string? FormatDocument(string content);
}
