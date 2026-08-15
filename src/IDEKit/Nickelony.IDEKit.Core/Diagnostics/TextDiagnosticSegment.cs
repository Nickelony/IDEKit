namespace Nickelony.IDEKit.Core.Diagnostics;

/// <summary>
/// Describes a document range and its diagnostic severity.
/// </summary>
/// <remarks>
/// <para>
/// Offsets are zero-based UTF-16 code-unit positions.
/// </para>
/// <para>
/// A consumer clamps the range to the document when it renders the segment.
/// An empty or reversed range becomes a segment of length 1 UTF-16 code unit when the document is non-empty.
/// </para>
/// </remarks>
/// <param name="StartOffset">The zero-based inclusive start offset of the described range.</param>
/// <param name="EndOffset">The zero-based exclusive end offset of the described range.</param>
/// <param name="Severity">
/// The diagnostic severity classification. <see cref="TextDiagnosticSeverity.None"/> means
/// unspecified; a renderer decides the visual treatment of every severity, and unrecognized values
/// are the renderer's choice as well.
/// </param>
public readonly record struct TextDiagnosticSegment(int StartOffset, int EndOffset, TextDiagnosticSeverity Severity);
