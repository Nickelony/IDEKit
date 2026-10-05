using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.LanguageServer.Provider;

public sealed partial class LanguageServerFeatureProvider
{
	/// <inheritdoc/>
	/// <remarks>
	/// The event is raised synchronously on the thread that produced the diagnostics, whether a published payload, a pull, or a rename.
	/// For a published payload, the diagnostic mapping and every subscriber handler run before the next payload for the same client is dispatched,
	/// so a slow mapping or handler applies back-pressure to diagnostics delivery for that client. Handlers must therefore be cheap
	/// and must not block.
	/// </remarks>
	public event EventHandler<DiagnosticsUpdatedEventArgs>? DiagnosticsUpdated
	{
		add => Composition.DiagnosticsSource.DiagnosticsUpdated += value;
		remove => Composition.DiagnosticsSource.DiagnosticsUpdated -= value;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// A supported document's tokens refresh after each successful open or change synchronization and after a restart reopen, and a server
	/// refresh request re-requests the full set. The event is raised when a refreshed set is stored, when a rename carries a document's cached
	/// tokens to its new path, and with an empty list when a content-changing rename clears them. A failed refresh keeps the last cached tokens
	/// and raises nothing.
	/// </remarks>
	public event EventHandler<SemanticTokensUpdatedEventArgs>? SemanticTokensUpdated
	{
		add => Composition.SemanticTokensSource.SemanticTokensUpdated += value;
		remove => Composition.SemanticTokensSource.SemanticTokensUpdated -= value;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The read never starts the server, and it returns an empty list before the first diagnostics arrive and after disposal. On a document
	/// identifier that cannot be resolved to a local file path the read degrades to an empty list, and the provider logs the identifier.
	/// </remarks>
	public IReadOnlyList<Diagnostic> GetDiagnostics(string documentId)
	{
		ArgumentNullException.ThrowIfNull(documentId);
		return Composition.DiagnosticsSource.GetDiagnostics(documentId);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// A session without full semantic-token support never raises <see cref="SemanticTokensUpdated"/> and reads an empty list. On a document
	/// identifier that cannot be resolved to a local file path the read degrades to an empty list, and the provider logs the identifier.
	/// </remarks>
	public IReadOnlyList<SemanticToken> GetSemanticTokens(string documentId)
	{
		ArgumentNullException.ThrowIfNull(documentId);
		return Composition.SemanticTokensSource.GetSemanticTokens(documentId);
	}
}
