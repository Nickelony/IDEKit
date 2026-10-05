using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.LanguageFeatures.Markup;

/// <summary>Rich text together with the markup kind that decides how a host renders it.</summary>
/// <remarks>
/// A completion item, a hover, a signature, and a parameter each carry their documentation as one <see cref="MarkupContent"/>, so the text
/// and its kind cannot disagree. Plain text is trimmed on construction; Markdown keeps its surrounding whitespace, because it carries indented code
/// blocks and hard line breaks. Two values are equal when their text and kind match.
/// </remarks>
public sealed record MarkupContent
{
	/// <summary>Initializes a new instance of the <see cref="MarkupContent"/> class.</summary>
	/// <param name="value">The text; plain text is trimmed.</param>
	/// <param name="kind">How a host interprets <paramref name="value"/>.</param>
	/// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined <see cref="MarkupKind"/> value.</exception>
	public MarkupContent(string value, MarkupKind kind = MarkupKind.PlainText)
	{
		ArgumentNullException.ThrowIfNull(value);

		if (!Enum.IsDefined(kind))
			throw new ArgumentOutOfRangeException(nameof(kind), kind, "The markup kind is not defined.");

		Value = kind == MarkupKind.Markdown ? value : value.Trim();
		Kind = kind;
	}

	/// <summary>Converts a string to plain-text content, which is the common documentation form.</summary>
	/// <param name="value">The text, or <see langword="null"/> for no content.</param>
	/// <returns>
	/// The plain-text content of <paramref name="value"/>, or <see langword="null"/> when <paramref name="value"/> is <see langword="null"/>.
	/// </returns>
	[return: NotNullIfNotNull(nameof(value))]
	public static implicit operator MarkupContent?(string? value) => value is null ? null : FromPlainText(value);

	/// <summary>Creates plain-text content.</summary>
	/// <param name="value">The text; it is trimmed.</param>
	/// <returns>The plain-text content of <paramref name="value"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
	public static MarkupContent FromPlainText(string value) => new(value);

	/// <summary>The text, trimmed when <see cref="Kind"/> is <see cref="MarkupKind.PlainText"/>.</summary>
	public string Value { get; }

	/// <summary>How a host interprets <see cref="Value"/>.</summary>
	public MarkupKind Kind { get; }

	/// <summary><see langword="true"/> when <see cref="Value"/> is empty or whitespace, which a consumer treats as no content.</summary>
	public bool IsBlank => string.IsNullOrWhiteSpace(Value);
}
