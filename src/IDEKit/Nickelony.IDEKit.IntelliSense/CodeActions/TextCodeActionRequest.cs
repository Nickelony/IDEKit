using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.IntelliSense.CodeActions;

/// <summary>
/// Describes a code-action request against an immutable document snapshot.
/// </summary>
/// <remarks>
/// The request addresses the document range the provider is asked about with zero-based UTF-16
/// offsets; a provider that needs additional contextual state resolves it from
/// <see cref="DocumentText"/> and the range itself. A host that derives the range from editor state
/// (for example the caret's diagnostic span) owns that mapping before constructing the request.
/// </remarks>
public sealed record TextCodeActionRequest
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextCodeActionRequest"/> record.
	/// </summary>
	/// <param name="documentText">The document text snapshot the offsets refer to.</param>
	/// <param name="startOffset">The zero-based start offset of the requested range.</param>
	/// <param name="endOffset">The zero-based end offset of the requested range.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// An offset is negative or greater than the document text length, or
	/// <paramref name="startOffset"/> is greater than <paramref name="endOffset"/>.
	/// </exception>
	public TextCodeActionRequest(string documentText, int startOffset, int endOffset)
	{
		ArgumentNullException.ThrowIfNull(documentText);
		TextOffsetValidation.ValidateOffset(documentText, startOffset, nameof(startOffset), "start");
		TextOffsetValidation.ValidateOffset(documentText, endOffset, nameof(endOffset), "end");
		TextOffsetValidation.ValidateOrderedRange(startOffset, endOffset, nameof(startOffset), "range");

		DocumentText = documentText;
		StartOffset = startOffset;
		EndOffset = endOffset;
	}

	/// <summary>Gets the document text snapshot the offsets refer to.</summary>
	public string DocumentText { get; }

	/// <summary>Gets the zero-based start offset of the requested range.</summary>
	public int StartOffset { get; }

	/// <summary>Gets the zero-based end offset of the requested range.</summary>
	public int EndOffset { get; }
}
