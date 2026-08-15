namespace Nickelony.IDEKit.IntelliSense.Diagnostics;

/// <summary>
/// Produces diagnostics for a document snapshot.
/// </summary>
/// <remarks>
/// Implementations must be safe to call from any thread: the request is an immutable snapshot and no
/// UI state may be touched. This permits the host to run full-document detection on the thread pool
/// when the work is CPU-bound. Implementations must reject a <see langword="null"/> request with
/// <see cref="ArgumentNullException"/>.
/// </remarks>
public interface ITextDiagnosticsProvider
{
	/// <summary>
	/// Gets diagnostics for the supplied request.
	/// </summary>
	/// <param name="request">The document snapshot to evaluate.</param>
	/// <returns>
	/// The diagnostics produced for the supplied snapshot; an empty list when none apply. The return
	/// value is never <see langword="null"/>.
	/// </returns>
	IReadOnlyList<TextDiagnostic> GetDiagnostics(TextDiagnosticsRequest request);
}
