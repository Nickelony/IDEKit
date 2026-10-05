namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>Builds the diagnostics payloads that the server-message tests queue and reads back what a subscriber received.</summary>
internal static class ServerMessageTestHelpers
{
	/// <summary>Creates a payload for <paramref name="uri"/> with one diagnostic that carries <paramref name="message"/>.</summary>
	internal static PublishDiagnosticsParams CreateDiagnostics(string? uri, string message) =>
		new(uri, Version: null, [new DiagnosticPayload(Range: null, Severity: null, message, Source: "test", Code: null)]);

	/// <summary>Returns the message of the first diagnostic of <paramref name="parameters"/>, or <see langword="null"/> when it has none.</summary>
	internal static string? FirstMessage(PublishDiagnosticsParams parameters) => parameters.Diagnostics?.FirstOrDefault()?.Message;
}
