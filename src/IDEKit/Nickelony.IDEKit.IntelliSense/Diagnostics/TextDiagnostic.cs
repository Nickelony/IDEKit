using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Infrastructure;

namespace Nickelony.IDEKit.IntelliSense.Diagnostics;

/// <summary>
/// Represents a single diagnostic produced for a document snapshot.
/// </summary>
/// <remarks>
/// <para>
/// Instances use structural value equality: two diagnostics are equal when their
/// <see cref="Severity"/>, <see cref="Message"/>, <see cref="Source"/>, <see cref="Code"/>,
/// <see cref="StartOffset"/>, and <see cref="EndOffset"/> are equal, so hosts can diff diagnostic
/// lists with the default comparer. Deduplication across producers compares the same tuple; use
/// <see cref="object.ReferenceEquals(object?, object?)"/> for identity-sensitive checks.
/// </para>
/// <para>
/// <see cref="Severity"/> is an open vocabulary: an undefined value is accepted and passed through
/// to the renderer, which chooses its presentation. Severity is lenient by design, unlike the closed
/// <see cref="TextMarkupKind"/> and <see cref="Signatures.TextSignatureHelpTriggerKind"/> vocabularies,
/// whose values must be defined. Severity describes how a host presents a diagnostic; a markup
/// format or trigger context is a protocol value the payload must carry exactly.
/// </para>
/// </remarks>
public sealed class TextDiagnostic : IEquatable<TextDiagnostic>
{
	private readonly TextRange _range;
	private string? _source;
	private string? _code;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextDiagnostic"/> class.
	/// </summary>
	/// <param name="severity">
	/// The diagnostic severity. Unknown values are accepted and passed through; a renderer maps them
	/// to its default presentation, consistent with <c>TextDiagnosticSeverity</c>.
	/// </param>
	/// <param name="message">The diagnostic message.</param>
	/// <param name="startOffset">The zero-based inclusive UTF-16 start offset.</param>
	/// <param name="endOffset">
	/// The zero-based exclusive UTF-16 end offset, or <paramref name="startOffset"/> for an empty
	/// span. Exact-offset selection still reaches an empty span at its offset.
	/// </param>
	/// <exception cref="ArgumentException"><paramref name="message"/> is blank.</exception>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="message"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="startOffset"/> is negative, or <paramref name="endOffset"/> is before
	/// <paramref name="startOffset"/>.
	/// </exception>
	public TextDiagnostic(TextDiagnosticSeverity severity, string message, int startOffset, int endOffset)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(message);
		ArgumentOutOfRangeException.ThrowIfNegative(startOffset);

		if (endOffset < startOffset)
			throw new ArgumentOutOfRangeException(nameof(endOffset), endOffset, "The end offset must not be before the start offset.");

		Severity = severity;
		Message = message;
		_range = new TextRange(startOffset, endOffset - startOffset);
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="TextDiagnostic"/> class from a range.
	/// </summary>
	/// <param name="severity">The diagnostic severity.</param>
	/// <param name="message">The diagnostic message.</param>
	/// <param name="range">The zero-based diagnostic range.</param>
	/// <exception cref="ArgumentException"><paramref name="message"/> is blank.</exception>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="message"/> is <see langword="null"/>.
	/// </exception>
	public TextDiagnostic(TextDiagnosticSeverity severity, string message, TextRange range)
		: this(severity, message, range.Offset, range.EndOffset)
	{
	}

	/// <summary>
	/// Gets the diagnostic severity.
	/// </summary>
	public TextDiagnosticSeverity Severity { get; }

	/// <summary>
	/// Gets the diagnostic message.
	/// </summary>
	public string Message { get; }

	/// <summary>
	/// Gets the optional identifier of the tool that produced the diagnostic (LSP
	/// <c>Diagnostic.source</c>), or <see langword="null"/> when the producer did not supply one.
	/// A blank assignment is treated as absent.
	/// </summary>
	public string? Source
	{
		get => _source;
		init => _source = OptionalText.Normalize(value);
	}

	/// <summary>
	/// Gets the optional producer-assigned code of the diagnostic (LSP <c>Diagnostic.code</c>),
	/// or <see langword="null"/> when the producer did not supply one. A blank assignment is treated
	/// as absent.
	/// </summary>
	public string? Code
	{
		get => _code;
		init => _code = OptionalText.Normalize(value);
	}

	/// <summary>
	/// Gets the zero-based inclusive UTF-16 start offset of the diagnostic span.
	/// </summary>
	public int StartOffset => _range.Offset;

	/// <summary>
	/// Gets the zero-based exclusive UTF-16 end offset of the diagnostic span.
	/// </summary>
	public int EndOffset => _range.EndOffset;

	/// <summary>
	/// Gets the diagnostic span as a <see cref="TextRange"/>.
	/// </summary>
	/// <remarks>
	/// The range is created together with the diagnostic, so a renderer that reads the span once per
	/// drawn line reuses the same value instead of rebuilding and revalidating it per read.
	/// </remarks>
	public TextRange Range => _range;

	/// <summary>
	/// Determines whether the diagnostic span contains the supplied offset.
	/// </summary>
	/// <remarks>
	/// The span uses an inclusive start and exclusive end. An empty span contains exactly its
	/// offset, so a zero-length diagnostic stays reachable through exact-offset selection; a
	/// negative offset never matches because the span starts at zero or later.
	/// </remarks>
	/// <param name="offset">The zero-based offset to check.</param>
	/// <returns>
	/// <see langword="true"/> when the offset falls within the diagnostic span; otherwise, <see langword="false"/>.
	/// </returns>
	public bool ContainsOffset(int offset)
		=> StartOffset == EndOffset
			? offset == StartOffset
			: offset >= StartOffset && offset < EndOffset;

	/// <summary>
	/// Determines whether the diagnostic span intersects the supplied range.
	/// </summary>
	/// <remarks>
	/// The range uses an inclusive start and exclusive end. Reversed or empty ranges do not
	/// intersect a diagnostic. An empty diagnostic span is treated as its offset: it intersects a
	/// range when that offset falls inside the range. A negative start offset is treated as a
	/// position before the document, so the range can still intersect spans at the document start.
	/// </remarks>
	/// <param name="startOffset">The zero-based inclusive start offset of the range.</param>
	/// <param name="endOffset">The zero-based exclusive end offset of the range.</param>
	/// <returns><see langword="true"/> when the ranges intersect; otherwise, <see langword="false"/>.</returns>
	public bool Intersects(int startOffset, int endOffset)
	{
		if (endOffset <= startOffset)
			return false;

		return StartOffset == EndOffset
			? StartOffset >= startOffset && StartOffset < endOffset
			: EndOffset > startOffset && StartOffset < endOffset;
	}

	/// <inheritdoc/>
	public bool Equals(TextDiagnostic? other)
	{
		if (other is null || Severity != other.Severity || StartOffset != other.StartOffset || EndOffset != other.EndOffset)
			return false;

		return string.Equals(Message, other.Message, StringComparison.Ordinal)
			&& string.Equals(Source, other.Source, StringComparison.Ordinal)
			&& string.Equals(Code, other.Code, StringComparison.Ordinal);
	}

	/// <inheritdoc/>
	public override bool Equals(object? obj) => obj is TextDiagnostic other && Equals(other);

	/// <summary>
	/// Determines whether two diagnostics are equal using the same structural value equality as
	/// <see cref="Equals(TextDiagnostic?)"/>; hosts can diff diagnostic lists with <c>==</c>.
	/// </summary>
	/// <param name="left">The left diagnostic.</param>
	/// <param name="right">The right diagnostic.</param>
	/// <returns><see langword="true"/> when the diagnostics are equal; otherwise, <see langword="false"/>.</returns>
	public static bool operator ==(TextDiagnostic? left, TextDiagnostic? right)
		=> left is null ? right is null : left.Equals(right);

	/// <summary>
	/// Determines whether two diagnostics are not equal using the same structural value equality as
	/// <see cref="Equals(TextDiagnostic?)"/>.
	/// </summary>
	/// <param name="left">The left diagnostic.</param>
	/// <param name="right">The right diagnostic.</param>
	/// <returns><see langword="true"/> when the diagnostics are not equal; otherwise, <see langword="false"/>.</returns>
	public static bool operator !=(TextDiagnostic? left, TextDiagnostic? right)
		=> !(left == right);

	/// <inheritdoc/>
	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(Severity);
		hash.Add(Message, StringComparer.Ordinal);
		hash.Add(Source, StringComparer.Ordinal);
		hash.Add(Code, StringComparer.Ordinal);
		hash.Add(StartOffset);
		hash.Add(EndOffset);

		return hash.ToHashCode();
	}
}
