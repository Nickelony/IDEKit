namespace Nickelony.IDEKit.LanguageFeatures.Diagnostics;

/// <summary>Produces diagnostics for a document snapshot on request.</summary>
/// <remarks>
/// Implementations must be safe to call from any thread: the request is an immutable snapshot and no UI state
/// may be touched. This permits the host to run full-document detection on the thread pool when the work is CPU-bound. Implementations must reject
/// a <see langword="null"/> request with <see cref="ArgumentNullException"/>. A producer that pushes diagnostics on its own schedule
/// implements <see cref="IDiagnosticsSource"/>.
/// </remarks>
public interface IDiagnosticsProvider
{
	/// <summary>Returns the diagnostics for the supplied request.</summary>
	/// <param name="request">The document snapshot to evaluate.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>
	/// The diagnostics produced for the supplied snapshot; an empty list when none apply. The result is never <see langword="null"/>.
	/// </returns>
	Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(DiagnosticsRequest request, CancellationToken cancellationToken = default);
}
