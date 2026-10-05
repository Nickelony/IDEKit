using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Diagnostics;

/// <summary>Describes a document range and its diagnostic severity.</summary>
/// <param name="Range">The zero-based range the segment describes.</param>
/// <param name="Severity">
/// The severity; a renderer decides the visual treatment of every value, including <see cref="DiagnosticSeverity.None"/> (unspecified)
/// and unrecognized values.
/// </param>
/// <remarks>
/// Offsets are zero-based UTF-16 code-unit positions. A consumer clamps the range to the document when it renders the segment, and it widens an empty
/// range to one code unit when the document is non-empty.
/// </remarks>
public readonly record struct DiagnosticSegment(TextRange Range, DiagnosticSeverity Severity);
