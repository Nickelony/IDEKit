namespace Nickelony.LanguageServer.Client;

/// <summary>Carries everything an <see cref="ILanguageServerTransport"/> needs from the client to open one connection to a language server.</summary>
/// <remarks>
/// The client builds one request per startup attempt. What to connect to, such as an executable and its launch settings,
/// belongs to the transport itself. The workspace roots are not part of the request either: the client sends them in the <c>initialize</c> request.
/// </remarks>
public sealed record LanguageServerConnectRequest
{
	/// <summary>The logger used for transport-level diagnostics: the logger of the client.</summary>
	public required ILogger Logger { get; init; }
}
