using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.Diagnostics;

/// <summary>Represents a single diagnostic produced for a document snapshot.</summary>
/// <remarks>
/// Instances use
/// structural value equality over <see cref="Severity"/>, <see cref="Message"/>, <see cref="Source"/>, <see cref="Code"/>, and <see cref="Range"/>,
/// so hosts can diff diagnostic lists with the default comparer and deduplicate across producers;
/// use <see cref="object.ReferenceEquals(object?, object?)"/> for identity-sensitive checks.
/// </remarks>
public sealed class Diagnostic : IEquatable<Diagnostic>
{
	private string? _source;
	private string? _code;

	/// <param name="severity">
	/// The diagnostic severity. Unknown values are accepted and passed through; a renderer maps them to its default presentation.
	/// </param>
	/// <param name="message">The diagnostic message.</param>
	/// <param name="range">
	/// The zero-based diagnostic range. An empty range marks a position, and exact-offset selection still reaches it at its offset.
	/// </param>
	/// <exception cref="ArgumentException"><paramref name="message"/> is blank.</exception>
	/// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
	public Diagnostic(DiagnosticSeverity severity, string message, TextRange range)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(message);

		Severity = severity;
		Message = message;
		Range = range;
	}

	public DiagnosticSeverity Severity { get; }

	public string Message { get; }

	/// <summary>
	/// The optional identifier of the tool that produced the diagnostic (LSP <c>Diagnostic.source</c>), or <see langword="null"/> when the producer
	/// did not supply one; a blank assignment is treated as absent.
	/// </summary>
	public string? Source
	{
		get => _source;
		init => _source = OptionalTextNormalizer.Normalize(value);
	}

	/// <summary>
	/// The optional producer-assigned code of the diagnostic (LSP <c>Diagnostic.code</c>), or <see langword="null"/> when the producer
	/// did not supply one; a blank assignment is treated as absent.
	/// </summary>
	public string? Code
	{
		get => _code;
		init => _code = OptionalTextNormalizer.Normalize(value);
	}

	/// <summary>The diagnostic span as a <see cref="TextRange"/>.</summary>
	/// <remarks>
	/// A host finds the diagnostics near a range with <see cref="TextRange.Intersects(TextRange)"/>: <c>diagnostic.Range.Intersects(range)</c>.
	/// </remarks>
	public TextRange Range { get; }

	/// <summary>Determines whether the diagnostic span contains the supplied offset.</summary>
	/// <param name="offset">The zero-based offset to check.</param>
	/// <returns><see langword="true"/> when the offset falls within the diagnostic span; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The span uses an inclusive start and exclusive end. An empty span contains exactly its offset, so a zero-length diagnostic stays reachable
	/// through exact-offset selection; a negative offset never matches because the span starts at zero or later.
	/// </remarks>
	public bool ContainsOffset(int offset) => Range.IsEmpty ? offset == Range.Offset : offset >= Range.Offset && offset < Range.EndOffset;

	/// <inheritdoc/>
	public bool Equals(Diagnostic? other)
	{
		if (other is null || Severity != other.Severity || Range != other.Range)
			return false;

		return string.Equals(Message, other.Message, StringComparison.Ordinal)
			&& string.Equals(Source, other.Source, StringComparison.Ordinal)
			&& string.Equals(Code, other.Code, StringComparison.Ordinal);
	}

	/// <inheritdoc/>
	public override bool Equals(object? obj) => obj is Diagnostic other && Equals(other);

	/// <summary>
	/// Determines whether two diagnostics are equal using the same structural value equality as <see cref="Equals(Diagnostic?)"/>; hosts can diff
	/// diagnostic lists with <c>==</c>.
	/// </summary>
	/// <param name="left">The left diagnostic.</param>
	/// <param name="right">The right diagnostic.</param>
	/// <returns><see langword="true"/> when the diagnostics are equal; otherwise, <see langword="false"/>.</returns>
	public static bool operator ==(Diagnostic? left, Diagnostic? right) => left is null ? right is null : left.Equals(right);

	/// <summary>
	/// Determines whether two diagnostics are not equal using the same structural value equality as <see cref="Equals(Diagnostic?)"/>.
	/// </summary>
	/// <param name="left">The left diagnostic.</param>
	/// <param name="right">The right diagnostic.</param>
	/// <returns><see langword="true"/> when the diagnostics are not equal; otherwise, <see langword="false"/>.</returns>
	public static bool operator !=(Diagnostic? left, Diagnostic? right) => !(left == right);

	/// <inheritdoc/>
	public override int GetHashCode()
	{
		var hash = new HashCode();

		hash.Add(Severity);
		hash.Add(Message, StringComparer.Ordinal);
		hash.Add(Source, StringComparer.Ordinal);
		hash.Add(Code, StringComparer.Ordinal);
		hash.Add(Range);

		return hash.ToHashCode();
	}
}
