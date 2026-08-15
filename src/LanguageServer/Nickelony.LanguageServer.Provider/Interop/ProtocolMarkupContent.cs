using Nickelony.IDEKit.IntelliSense;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Represents extracted markup content together with the shared markup kind that describes how its
/// text is interpreted.
/// </summary>
/// <remarks>
/// The kind is the IntelliSense <see cref="TextMarkupKind"/> vocabulary, so a consumer forwards the
/// value to a payload member such as <c>TextHoverInfo.ContentKind</c> or
/// <c>TextCompletionItem.DocumentationKind</c> instead of folding a protocol boolean into it.
/// </remarks>
public readonly record struct ProtocolMarkupContent
{
	private readonly string? _text;

	/// <summary>
	/// Initializes a new instance of the <see cref="ProtocolMarkupContent"/> struct.
	/// </summary>
	/// <param name="text">The extracted text value.</param>
	/// <param name="kind">The markup kind of the extracted text.</param>
	public ProtocolMarkupContent(string? text, TextMarkupKind kind)
	{
		_text = text ?? string.Empty;
		Kind = kind;
	}

	/// <summary>
	/// Gets the extracted text.
	/// The <see langword="default"/> value carries no text and yields an empty string.
	/// </summary>
	public string Text => _text ?? string.Empty;

	/// <summary>
	/// Gets the markup kind of the content. The <see langword="default"/> value is
	/// <see cref="TextMarkupKind.PlainText"/>, which matches the protocol's absent-kind default.
	/// </summary>
	public TextMarkupKind Kind { get; }

	/// <summary>
	/// Determines whether the supplied value has the same text and markup kind.
	/// </summary>
	/// <param name="other">The value to compare with.</param>
	/// <returns><see langword="true"/> when the effective text and kind are equal; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// Equality compares the effective <see cref="Text"/> and <see cref="Kind"/> values, so a
	/// <see langword="default"/> instance and an instance constructed with empty text are equal.
	/// </remarks>
	public bool Equals(ProtocolMarkupContent other)
		=> string.Equals(Text, other.Text, StringComparison.Ordinal) && Kind == other.Kind;

	/// <summary>
	/// Serves as the hash function for <see cref="ProtocolMarkupContent"/>.
	/// </summary>
	/// <returns>A hash code over the effective text and kind.</returns>
	public override int GetHashCode() => HashCode.Combine(Text, Kind);
}
