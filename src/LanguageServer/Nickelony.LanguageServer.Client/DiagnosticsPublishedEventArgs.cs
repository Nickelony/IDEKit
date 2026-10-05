using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>Provides the data for the <see cref="ILanguageServerClient.DiagnosticsPublished"/> event.</summary>
/// <remarks>
/// Each subscriber receives an owned detached diagnostics snapshot that later payload mutations cannot change. The coalescing rule is described
/// on <see cref="ILanguageServerClient.DiagnosticsPublished"/>.
/// </remarks>
public sealed class DiagnosticsPublishedEventArgs : EventArgs
{
	/// <param name="parameters">The <c>textDocument/publishDiagnostics</c> notification payload.</param>
	/// <exception cref="ArgumentNullException"><paramref name="parameters"/> is <see langword="null"/>.</exception>
	public DiagnosticsPublishedEventArgs(PublishDiagnosticsParams parameters)
	{
		ArgumentNullException.ThrowIfNull(parameters);
		Parameters = parameters;
	}

	/// <summary>The <c>textDocument/publishDiagnostics</c> notification payload with a detached diagnostics snapshot.</summary>
	public PublishDiagnosticsParams Parameters { get; }
}
