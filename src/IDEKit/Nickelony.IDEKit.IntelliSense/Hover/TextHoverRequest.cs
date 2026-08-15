using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.IntelliSense.Hover;

/// <summary>
/// Describes a hover request against an immutable document snapshot.
/// </summary>
/// <remarks>
/// Hover requests use a zero-based document offset so they can be constructed directly from
/// editor positions without converting to line and character coordinates.
/// </remarks>
public sealed record TextHoverRequest
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextHoverRequest"/> record.
	/// </summary>
	/// <param name="documentText">The current document snapshot text.</param>
	/// <param name="hoveredOffset">The zero-based UTF-16 hovered offset within that snapshot.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="hoveredOffset"/> is negative or greater than the length of <paramref name="documentText"/>.
	/// </exception>
	public TextHoverRequest(string documentText, int hoveredOffset)
	{
		ArgumentNullException.ThrowIfNull(documentText);
		TextOffsetValidation.ValidateOffset(documentText, hoveredOffset, nameof(hoveredOffset), "hovered");

		DocumentText = documentText;
		HoveredOffset = hoveredOffset;
	}

	/// <summary>
	/// Gets the current document snapshot text.
	/// </summary>
	public string DocumentText { get; }

	/// <summary>
	/// Gets the zero-based UTF-16 hovered offset within <see cref="DocumentText"/>.
	/// </summary>
	public int HoveredOffset { get; }
}
