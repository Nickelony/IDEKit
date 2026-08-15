using Nickelony.IDEKit.Core.Diagnostics;

namespace Nickelony.IDEKit.IntelliSense.Diagnostics;

/// <summary>
/// Projects <see cref="TextDiagnostic"/> values onto the editor-neutral
/// <see cref="TextDiagnosticSegment"/> render model.
/// </summary>
/// <remarks>
/// <para>
/// The projection is the shared mapping every editor binding uses to feed its diagnostic renderer
/// from IntelliSense diagnostics, so the segment construction cannot drift between bindings.
/// <see cref="Project"/> keeps the fail-fast contract of a direct call;
/// <see cref="ProjectToleratingNulls"/> is the render-pass shape.
/// </para>
/// <para>
/// The segment offsets match the diagnostic offsets; a consumer clamps them against the drawn
/// document. The severity is passed through unchanged.
/// </para>
/// </remarks>
public static class TextDiagnosticSegmentProjection
{
	/// <summary>
	/// Projects diagnostics into renderer segments, in the diagnostics' order.
	/// </summary>
	/// <param name="diagnostics">The diagnostics to project.</param>
	/// <returns>The projected segments, in the diagnostics' order.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="diagnostics"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The diagnostics list contains a null entry.</exception>
	public static IReadOnlyList<TextDiagnosticSegment> Project(IReadOnlyList<TextDiagnostic> diagnostics)
	{
		ArgumentNullException.ThrowIfNull(diagnostics);

		var segments = new TextDiagnosticSegment[diagnostics.Count];

		for (int index = 0; index < diagnostics.Count; index++)
		{
			TextDiagnostic diagnostic = diagnostics[index]
				?? throw new ArgumentException("The diagnostics list must not contain null entries.", nameof(diagnostics));

			segments[index] = CreateSegment(diagnostic);
		}

		return segments;
	}

	/// <summary>
	/// Projects diagnostics into renderer segments, tolerating a missing list or null entries.
	/// </summary>
	/// <param name="diagnostics">
	/// The diagnostics to project, or <see langword="null"/> for no segments.
	/// </param>
	/// <returns>
	/// The projected segments; a <see langword="null"/> list, or a list containing null entries,
	/// yields no segment for those entries instead of throwing.
	/// </returns>
	/// <remarks>
	/// A render pass iterates the projected list without a null check, so a provider that runs during
	/// the render pass uses this overload to degrade to no segment rather than tear down the pass.
	/// </remarks>
	public static IReadOnlyList<TextDiagnosticSegment> ProjectToleratingNulls(
		IReadOnlyList<TextDiagnostic>? diagnostics)
	{
		if (diagnostics is null)
			return Array.Empty<TextDiagnosticSegment>();

		var segments = new List<TextDiagnosticSegment>(diagnostics.Count);

		foreach (TextDiagnostic? diagnostic in diagnostics)
		{
			if (diagnostic is not null)
				segments.Add(CreateSegment(diagnostic));
		}

		return segments;
	}

	private static TextDiagnosticSegment CreateSegment(TextDiagnostic diagnostic)
		=> new(diagnostic.StartOffset, diagnostic.EndOffset, diagnostic.Severity);
}
