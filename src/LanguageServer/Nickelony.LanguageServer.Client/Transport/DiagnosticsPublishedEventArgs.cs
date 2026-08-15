namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Provides the data for the <see cref="ILanguageServerClient.DiagnosticsPublished"/> event.
/// </summary>
/// <remarks>
/// Each subscriber receives an owned detached diagnostics snapshot that later payload mutations cannot change.
/// Diagnostics coalesce per document URI by arrival order; the LSP version field is not used for routing.
/// </remarks>
public sealed class DiagnosticsPublishedEventArgs : EventArgs
{
	/// <summary>
	/// Initializes a new instance of the <see cref="DiagnosticsPublishedEventArgs"/> class.
	/// </summary>
	/// <param name="parameters">The <c>textDocument/publishDiagnostics</c> notification payload.</param>
	/// <exception cref="ArgumentNullException"><paramref name="parameters"/> is <see langword="null"/>.</exception>
	public DiagnosticsPublishedEventArgs(PublishDiagnosticsParams parameters)
	{
		ArgumentNullException.ThrowIfNull(parameters);

		Parameters = parameters;
	}

	/// <summary>
	/// Gets the <c>textDocument/publishDiagnostics</c> notification payload with a detached diagnostics snapshot.
	/// </summary>
	public PublishDiagnosticsParams Parameters { get; }
}
