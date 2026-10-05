using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>The payload a listed workspace symbol carries in <c>Data</c>, which <c>workspaceSymbol/resolve</c> sends back to the server.</summary>
/// <param name="Payload">The symbol as the server listed it, with its opaque <c>data</c>.</param>
/// <param name="Owner">The provider that listed the symbol, which is the only one whose server minted the <c>data</c>.</param>
/// <param name="ConnectionGeneration">
/// The connection generation that was active when the provider sent the request, whose server minted the <c>data</c>.
/// </param>
internal sealed record WorkspaceSymbolOffer(WorkspaceSymbolPayload Payload, object Owner, long ConnectionGeneration);
