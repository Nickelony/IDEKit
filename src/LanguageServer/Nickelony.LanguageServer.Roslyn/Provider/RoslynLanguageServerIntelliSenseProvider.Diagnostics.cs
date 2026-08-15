using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.LanguageServer.Roslyn;

public abstract partial class RoslynLanguageServerIntelliSenseProvider
{
	/// <inheritdoc/>
	/// <remarks>
	/// The Roslyn language server is pull-oriented: it delivers diagnostics through <c>textDocument/diagnostic</c>
	/// rather than <c>textDocument/publishDiagnostics</c>, and the framework feeds both delivery models through
	/// this one hook. A full report is parsed and cached here, so the cache and the <c>DiagnosticsUpdated</c> event
	/// behave exactly as they do for a push-only server.
	/// </remarks>
	protected override IReadOnlyList<TextDiagnostic>? HandleDiagnosticsPayload(
		string filePath,
		PublishDiagnosticsParams parameters,
		DocumentSnapshot document)
	{
		if (!DiagnosticsParser.TryParse(parameters, filePath,
			document.Content, document.Version, RoslynDiagnosticMapping.Policy, out PublishedDiagnostics? publishedDiagnostics))
		{
			Logger.LogDebug("Roslyn diagnostics payload for '{FilePath}' was stale for the tracked document version and was dropped.", filePath);
			return null;
		}

		if (IsDisposed)
			return null;

		if (!DocumentStore.TryStoreDiagnostics(publishedDiagnostics, document.Version, document.Content))
		{
			Logger.LogDebug("Roslyn diagnostics payload for '{FilePath}' was superseded before it could be stored and was dropped.", filePath);
			return null;
		}

		return publishedDiagnostics.Diagnostics;
	}

	/// <inheritdoc/>
	internal override (IReadOnlyList<DiagnosticEntry> Diagnostics, string? SourceContent) GetDiagnosticsSnapshot(string filePath)
		=> DocumentStore.GetDiagnosticsSnapshot(filePath);
}
