using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

internal static class WorkspaceSymbolResponseParser
{
	/// <summary>Parses workspace symbols from an LSP workspace-symbol response into the shared model.</summary>
	/// <param name="response">The workspace-symbol response payload, or <see langword="null"/> when unavailable.</param>
	/// <param name="owner">The provider that sent the request, which a symbol remembers as its owner.</param>
	/// <param name="connectionGeneration">The connection generation that was active when the provider sent the request.</param>
	/// <param name="logger">The logger that records a symbol the parser had to drop.</param>
	/// <returns>The parsed symbols in response order, or an empty list when no usable symbol is present.</returns>
	/// <remarks>
	/// A symbol whose URI is no local file, such as a metadata document of the server, is dropped individually, because a host opens
	/// local documents only. A symbol whose range is missing or malformed keeps no range, so a host resolves it before it opens the symbol.
	/// </remarks>
	internal static IReadOnlyList<WorkspaceSymbol> Parse(WorkspaceSymbolResponse? response, object owner, long connectionGeneration, ILogger logger)
	{
		if (response is null || response.Symbols.Count == 0)
			return [];

		var result = new List<WorkspaceSymbol>(response.Symbols.Count);

		foreach (WorkspaceSymbolPayload payload in response.Symbols)
		{
			if (Create(payload, owner, connectionGeneration, logger) is { } symbol)
				result.Add(symbol);
		}

		return result;
	}

	/// <summary>Parses the symbol that a <c>workspaceSymbol/resolve</c> request answered.</summary>
	/// <param name="resolved">The resolved symbol payload.</param>
	/// <param name="offer">The offer of the symbol that was resolved, whose owner and generation the answer inherits.</param>
	/// <param name="logger">The logger that records a symbol the parser had to drop.</param>
	/// <returns>The resolved symbol, or <see langword="null"/> when the payload is not usable.</returns>
	internal static WorkspaceSymbol? ParseResolved(WorkspaceSymbolPayload resolved, WorkspaceSymbolOffer offer, ILogger logger) =>
		Create(payload: resolved, owner: offer.Owner, connectionGeneration: offer.ConnectionGeneration, logger);

	private static WorkspaceSymbol? Create(WorkspaceSymbolPayload payload, object owner, long connectionGeneration, ILogger logger)
	{
		if (string.IsNullOrWhiteSpace(payload.Name) || payload.Location is not WorkspaceSymbolLocationPayload location)
			return null;

		if (!DocumentUriConversion.TryGetLocalPath(location.Uri, out string filePath))
		{
			logger.LogDebug("Dropping workspace symbol '{Name}' because its URI '{Uri}' is no local file.", payload.Name, location.Uri);
			return null;
		}

		// The protocol defines no default symbol kind, so a kind that a newer server sends maps to File, as the document symbols map it.
		DocumentSymbolKind kind = DocumentSymbolKindConversion.TryFromProtocol(payload.Kind, out DocumentSymbolKind mapped)
			? mapped
			: DocumentSymbolKind.File;

		TextPositionRange? range = ProtocolRangeConversion.TryGetTextPositionRange(location.Range, out TextPositionRange converted)
			? converted
			: null;

		return new WorkspaceSymbol(payload.Name, kind, new WorkspaceSymbolLocation(filePath, range))
		{
			ContainerName = payload.ContainerName,
			Data = new WorkspaceSymbolOffer(payload, owner, connectionGeneration),
		};
	}
}
