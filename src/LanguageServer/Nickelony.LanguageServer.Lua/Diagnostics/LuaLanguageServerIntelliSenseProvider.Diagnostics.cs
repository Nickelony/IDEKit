using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.LanguageServer.Lua;

public sealed partial class LuaLanguageServerIntelliSenseProvider
{
	/// <inheritdoc/>
	protected override IReadOnlyList<TextDiagnostic>? HandleDiagnosticsPayload(
		string filePath,
		PublishDiagnosticsParams parameters,
		DocumentSnapshot document)
	{
		if (!DiagnosticsParser.TryParse(parameters, filePath,
			document.Content, document.Version, LuaDiagnosticMapping.Policy, out PublishedDiagnostics? publishedDiagnostics))
		{
			Logger.LogDebug("Lua diagnostics payload for '{FilePath}' was stale for the tracked document version and was dropped.", filePath);
			return null;
		}

		if (IsDisposed)
			return null;

		if (!DocumentStore.TryStoreDiagnostics(publishedDiagnostics, document.Version, document.Content))
		{
			Logger.LogDebug("Lua diagnostics payload for '{FilePath}' was superseded before it could be stored and was dropped.", filePath);
			return null;
		}

		return publishedDiagnostics.Diagnostics;
	}

	/// <inheritdoc/>
	internal override (IReadOnlyList<DiagnosticEntry> Diagnostics, string? SourceContent) GetDiagnosticsSnapshot(string filePath)
		=> DocumentStore.GetDiagnosticsSnapshot(filePath);
}
