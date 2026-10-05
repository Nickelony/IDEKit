using Nickelony.IDEKit.Core.Diagnostics;

namespace Nickelony.IDEKit.LanguageFeatures.Diagnostics;

/// <summary>Projects <see cref="Diagnostic"/> values onto the editor-neutral <see cref="DiagnosticSegment"/> render model.</summary>
/// <remarks>
/// The mapping a host applies before it supplies segments to a diagnostics renderer. Segment offsets match the diagnostic offsets and a consumer
/// clamps them against the drawn document, while the severity passes through unchanged. A renderer's <c>ApplySegments</c> copies the projected list,
/// so a host projects once when its diagnostics change.
/// </remarks>
public static class DiagnosticSegmentConversion
{
	/// <summary>Projects diagnostics into renderer segments.</summary>
	/// <param name="diagnostics">The diagnostics to project.</param>
	/// <returns>The projected segments, in the diagnostics' order.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="diagnostics"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The diagnostics list contains a <see langword="null"/> entry.</exception>
	public static IReadOnlyList<DiagnosticSegment> Project(IReadOnlyList<Diagnostic> diagnostics)
	{
		ArgumentNullException.ThrowIfNull(diagnostics);

		var segments = new DiagnosticSegment[diagnostics.Count];

		for (int index = 0; index < diagnostics.Count; index++)
		{
			Diagnostic diagnostic =
				diagnostics[index] ?? throw new ArgumentException("The diagnostics list must not contain null entries.", nameof(diagnostics));

			segments[index] = new DiagnosticSegment(diagnostic.Range, diagnostic.Severity);
		}

		return segments;
	}
}
