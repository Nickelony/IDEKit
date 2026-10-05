using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.Hover;

/// <summary>Describes a hover request against an immutable document snapshot.</summary>
/// <remarks>
/// Hover requests use a zero-based document offset so they can be constructed directly from editor positions without converting to line
/// and character coordinates.
/// </remarks>
public sealed record HoverRequest
{
	/// <param name="documentId">
	/// The identifier of the document the text belongs to. A language-server provider maps it to the document's file URI, and a local provider
	/// may ignore it.
	/// </param>
	/// <param name="documentText">The current document snapshot text.</param>
	/// <param name="hoveredOffset">The zero-based UTF-16 hovered offset within that snapshot.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentId"/> or <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="hoveredOffset"/> is negative or greater than the length of <paramref name="documentText"/>.
	/// </exception>
	public HoverRequest(string documentId, string documentText, int hoveredOffset)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
		ArgumentNullException.ThrowIfNull(documentText);
		TextOffsetValidation.ValidateOffset(documentText, offset: hoveredOffset, parameterName: nameof(hoveredOffset), offsetDescription: "hovered");

		DocumentId = documentId;
		DocumentText = documentText;
		HoveredOffset = hoveredOffset;
	}

	/// <summary>The identifier of the document the text belongs to.</summary>
	public string DocumentId { get; }

	/// <summary>The current document snapshot text.</summary>
	public string DocumentText { get; }

	/// <summary>The zero-based UTF-16 hovered offset within <see cref="DocumentText"/>.</summary>
	public int HoveredOffset { get; }
}
