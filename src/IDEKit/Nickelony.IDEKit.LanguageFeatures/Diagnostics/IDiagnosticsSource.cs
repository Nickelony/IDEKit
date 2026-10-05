namespace Nickelony.IDEKit.LanguageFeatures.Diagnostics;

/// <summary>Produces the diagnostics of documents without a request: a source pushes them, and a consumer pulls the current ones.</summary>
/// <remarks>
/// <para>
/// A language server pushes diagnostics, and a local analyzer may produce them on its own schedule, so a source
/// announces each change with <see cref="DiagnosticsUpdated"/> instead of answering a request. A producer that answers a request for one snapshot
/// implements <see cref="IDiagnosticsProvider"/>.
/// </para>
/// <para>
/// A set carries no document version, and a consumer shows it at its offsets in the text as it is now, without mapping it through the edits made
/// since the producer read the text. A source therefore raises a set only when it describes the text that the host last reported, and it drops a set
/// that a later edit superseded.
/// </para>
/// </remarks>
public interface IDiagnosticsSource
{
	/// <summary>Raised when the source's current diagnostics for a document change.</summary>
	/// <remarks>
	/// The diagnostics list is an owned immutable snapshot that remains valid after the callback returns.
	/// </remarks>
	event EventHandler<DiagnosticsUpdatedEventArgs>? DiagnosticsUpdated;

	/// <summary>Returns the current diagnostics for a document.</summary>
	/// <param name="documentId">The identifier of the document.</param>
	/// <returns>An owned immutable snapshot of the current diagnostics, or an empty list when the source holds none for the document.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="documentId"/> is <see langword="null"/>.</exception>
	IReadOnlyList<Diagnostic> GetDiagnostics(string documentId);
}
