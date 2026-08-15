#if AVALONIAEDIT
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Diagnostics;
#else
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Diagnostics;
#endif
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Diagnostics;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Diagnostics;
#else
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Diagnostics;
#endif

/// <summary>
/// Projects IntelliSense diagnostics into the editor's diagnostic renderer segment model and creates
/// wired-up renderers, so hosts do not have to hand-map <see cref="TextDiagnostic"/> values.
/// </summary>
/// <remarks>
/// <para>
/// The renderer queries its segment provider during every render pass, so <see cref="CreateSegments"/> is the
/// caching path: project once, hand the returned list back from the host's provider, and invalidate the
/// text view when the diagnostics change (for example with
/// <see cref="TextView.Redraw()"/>). <see cref="CreateSegmentProvider"/> does
/// not cache; it re-projects the diagnostics on every render pass, which is convenient for small diagnostic
/// sets but allocates per pass for large ones.
/// </para>
/// <para>
/// The projection itself is the editor-neutral <see cref="TextDiagnosticSegmentProjection"/>; this factory
/// only composes it into the editor's renderer.
/// </para>
/// <para>
/// A diagnostics provider that temporarily has no data returns an empty list; <see cref="CreateSegments"/>
/// rejects a <see langword="null"/> list with <see cref="ArgumentNullException"/> so the direct projection API
/// keeps its fail-fast contract. The provider-backed entry points (<see cref="CreateSegmentProvider"/> and
/// <see cref="CreateRenderer"/>) run during the render pass, so they tolerate a provider that returns a list
/// containing <see langword="null"/> entries, or <see langword="null"/> itself, by rendering nothing for the
/// offending entries instead of throwing inside the renderer.
/// </para>
/// </remarks>
public static class TextDiagnosticSegmentFactory
{
	/// <summary>
	/// Projects diagnostics into renderer segments. The segment offsets match the diagnostic offsets; the
	/// renderer clamps them against the drawn document.
	/// </summary>
	/// <param name="diagnostics">The diagnostics to project.</param>
	/// <returns>The projected segments, in the diagnostics' order.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="diagnostics"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The diagnostics list contains a null entry.</exception>
	public static IReadOnlyList<TextDiagnosticSegment> CreateSegments(IReadOnlyList<TextDiagnostic> diagnostics)
		=> TextDiagnosticSegmentProjection.Project(diagnostics);

	/// <summary>
	/// Creates a segment provider for the renderer from a diagnostics provider.
	/// </summary>
	/// <param name="diagnosticsProvider">Provides the diagnostics to render.</param>
	/// <returns>
	/// A provider that projects the provided diagnostics on each render pass, without caching the
	/// projection. A provider that returns <see langword="null"/>, or a list containing null entries, renders no
	/// segment for those entries instead of throwing during the render pass.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="diagnosticsProvider"/> is <see langword="null"/>.</exception>
	public static Func<IReadOnlyList<TextDiagnosticSegment>> CreateSegmentProvider(
		Func<IReadOnlyList<TextDiagnostic>> diagnosticsProvider)
	{
		ArgumentNullException.ThrowIfNull(diagnosticsProvider);

		return () => TextDiagnosticSegmentProjection.ProjectToleratingNulls(diagnosticsProvider());
	}

	/// <summary>
	/// Creates a diagnostic renderer whose segments are the projection of the provided diagnostics.
	/// Add it to the text view's background renderers and invalidate the view when the diagnostics change.
	/// </summary>
	/// <param name="diagnosticsProvider">Provides the diagnostics to render.</param>
	/// <returns>The created renderer.</returns>
	/// <remarks>
	/// Behaves like <see cref="CreateSegmentProvider"/>: a provider that returns <see langword="null"/>, or a
	/// list containing null entries, renders no segments for them instead of throwing during the render pass.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="diagnosticsProvider"/> is <see langword="null"/>.</exception>
	public static DiagnosticsRenderer CreateRenderer(
		Func<IReadOnlyList<TextDiagnostic>> diagnosticsProvider)
		=> new(CreateSegmentProvider(diagnosticsProvider));
}
