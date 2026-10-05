using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Builds the diagnostics payloads that tests publish through a client double.</summary>
internal static class DiagnosticsTestHelpers
{
	/// <summary>Creates a <c>textDocument/publishDiagnostics</c> payload with one warning on the first line of a document.</summary>
	/// <param name="filePath">The local file path of the document.</param>
	/// <param name="version">The document version the payload claims, or <see langword="null"/> for none.</param>
	/// <param name="startCharacter">The character the warning starts at.</param>
	/// <param name="endCharacter">The character the warning ends at.</param>
	/// <param name="message">The diagnostic message.</param>
	internal static PublishDiagnosticsParams CreateDiagnostics(string filePath, int? version, int startCharacter, int endCharacter, string message)
	{
		return new(
			new Uri(filePath).AbsoluteUri,
			version,

			[
				new DiagnosticPayload(
					Range: new RangePayload(new PositionPayload(0, startCharacter), new PositionPayload(0, endCharacter)),
					Severity: Lsp.DiagnosticSeverity.Warning,
					message,
					Source: null,
					Code: null
				),
			]
		);
	}
}
