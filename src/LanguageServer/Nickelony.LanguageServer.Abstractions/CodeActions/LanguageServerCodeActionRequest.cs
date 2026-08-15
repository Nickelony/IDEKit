using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Describes a request for the code actions available for a document range.
/// </summary>
/// <remarks>
/// Negative line and character values are changed to zero; otherwise, the range is stored as supplied and is not
/// required to be ordered.
/// </remarks>
public sealed record LanguageServerCodeActionRequest
{
	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerCodeActionRequest"/> record.
	/// </summary>
	/// <param name="filePath">The current document file path.</param>
	/// <param name="documentText">The current document content.</param>
	/// <param name="range">The zero-based range to get code actions for. Negative line and character values are changed to zero.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/> or <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	public LanguageServerCodeActionRequest(string filePath, string documentText, TextPositionRange range)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(documentText);

		FilePath = filePath;
		DocumentText = documentText;
		Range = new TextPositionRange(
			range.Start.ClampNegativeToZero(), range.End.ClampNegativeToZero());
	}

	/// <summary>
	/// Gets the current document file path.
	/// </summary>
	public string FilePath { get; }

	/// <summary>
	/// Gets the current document content.
	/// </summary>
	public string DocumentText { get; }

	/// <summary>
	/// Gets the zero-based range to get code actions for.
	/// </summary>
	public TextPositionRange Range { get; }
}
