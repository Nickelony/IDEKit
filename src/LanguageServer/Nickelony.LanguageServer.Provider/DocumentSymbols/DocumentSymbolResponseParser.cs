using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

internal static class DocumentSymbolResponseParser
{
	/// <summary>Parses document symbols from an LSP document-symbol response into the shared offset-based model.</summary>
	/// <param name="response">The document-symbol response payload, or <see langword="null"/> when unavailable.</param>
	/// <param name="lineMap">The line map of the document content the ranges are resolved against.</param>
	/// <param name="normalizedFilePath">
	/// The normalized local path of the requested document, which keeps flat symbol entries that target the same document.
	/// </param>
	/// <param name="pathComparison">The comparison that decides whether a flat symbol entry targets the requested document.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for no logging.</param>
	/// <returns>The parsed symbols in response order with nested children preserved, or an empty list when no usable symbol is present.</returns>
	internal static IReadOnlyList<DocumentSymbol> Parse(
		DocumentSymbolResponse? response,
		TextLineMap lineMap,
		string normalizedFilePath,
		LocalPathComparisonPolicy pathComparison,
		ILogger? logger = null
	)
	{
		if (response is null || response.Symbols.Count == 0)
			return [];

		IReadOnlyList<DocumentSymbolPayload> symbols = response.Symbols;

		var result = new List<DocumentSymbol>(symbols.Count);

		for (int i = 0; i < symbols.Count; i++)
		{
			DocumentSymbol? symbol = ParseSymbol(payload: symbols[i], lineMap, normalizedFilePath, pathComparison, logger);

			if (symbol is not null)
				result.Add(symbol);
		}

		return result;
	}

	/// <summary>Parses one document-symbol entry.</summary>
	/// <param name="payload">The symbol payload to parse.</param>
	/// <param name="lineMap">The line map of the document content.</param>
	/// <param name="normalizedFilePath">The normalized local path of the requested document.</param>
	/// <param name="pathComparison">The comparison that decides whether a flat symbol entry targets the requested document.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for no logging.</param>
	/// <returns>The parsed symbol, or <see langword="null"/> when the entry is not usable.</returns>
	private static DocumentSymbol? ParseSymbol(
		DocumentSymbolPayload payload,
		TextLineMap lineMap,
		string normalizedFilePath,
		LocalPathComparisonPolicy pathComparison,
		ILogger? logger
	)
	{
		// A server may name an anonymous symbol with a single space and give its children the same placeholder, so
		// only a null or empty name is unusable here; whitespace-only names are deliberate and are kept.
		if (payload.Name is null || payload.Name.Length == 0)
			return null;

		// The protocol kind is required by the payload contract; a hand-built payload without one carries the
		// undefined enum value zero, which maps to File, the protocol's first kind, because the
		// protocol defines no default symbol kind.
		DocumentSymbolKind kind = DocumentSymbolKindConversion.TryFromProtocol(payload.Kind, out DocumentSymbolKind mappedKind)
			? mappedKind
			: DocumentSymbolKind.File;

		// A flat SymbolInformation entry describes one location; entries that target another document
		// do not belong in this document's outline and are skipped.
		if (payload.Location is { } location)
		{
			if (
				!DocumentUriConversion.TryGetLocalPath(location.Uri, out string locationFilePath)
				|| !pathComparison.Comparer.Equals(locationFilePath, normalizedFilePath)
			)
			{
				return null;
			}

			// A payload that carries a flat location and hierarchical ranges silently loses the hierarchical half
			// for this entry; the flat location wins by contract, so at least make the choice observable.
			if (logger is not null && (payload.Range is not null || payload.Children is { Count: > 0 }))
				logger.LogDebug("Document symbol '{Name}' carried both a flat location and hierarchical ranges; the flat entry wins.", payload.Name);

			return new DocumentSymbol(
				name: payload.Name,
				kind,
				range: TryConvertRange(location.Range, lineMap, out TextRange locationRange) ? locationRange : null,
				selectionRange: null
			)
			{
				Detail = payload.Detail ?? payload.ContainerName,
			};
		}

		return new DocumentSymbol(
			name: payload.Name,
			kind,
			range: TryConvertRange(payload.Range, lineMap, out TextRange range) ? range : null,
			selectionRange: TryConvertRange(payload.SelectionRange, lineMap, out TextRange selectionRange) ? selectionRange : null
		)
		{
			Detail = payload.Detail,
			Children = ParseChildSymbols(children: payload.Children, lineMap, normalizedFilePath, pathComparison, logger),
		};
	}

	/// <summary>Parses the nested children of a hierarchical symbol entry.</summary>
	/// <param name="children">The child payloads, or <see langword="null"/> when none are present.</param>
	/// <param name="lineMap">The line map of the document content.</param>
	/// <param name="normalizedFilePath">The normalized local path of the requested document.</param>
	/// <param name="pathComparison">The comparison that decides whether a flat symbol entry targets the requested document.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for no logging.</param>
	/// <returns>The parsed children in response order, or an empty list when none are usable.</returns>
	private static List<DocumentSymbol> ParseChildSymbols(
		IReadOnlyList<DocumentSymbolPayload>? children,
		TextLineMap lineMap,
		string normalizedFilePath,
		LocalPathComparisonPolicy pathComparison,
		ILogger? logger
	)
	{
		if (children is not { Count: > 0 })
			return [];

		var result = new List<DocumentSymbol>(children.Count);

		for (int i = 0; i < children.Count; i++)
		{
			DocumentSymbol? child = ParseSymbol(payload: children[i], lineMap, normalizedFilePath, pathComparison, logger);

			if (child is not null)
				result.Add(child);
		}

		return result;
	}

	/// <summary>Converts a protocol range to the corresponding offset range.</summary>
	/// <param name="range">The protocol range, or <see langword="null"/> when absent.</param>
	/// <param name="lineMap">The line map of the document content.</param>
	/// <param name="textRange">Receives the converted offset range.</param>
	/// <returns><see langword="true"/> when the range maps to a representable offset range.</returns>
	/// <remarks>
	/// A malformed range yields an entry without a range instead of an entry that points at the file start, because the shared rule rejects it.
	/// </remarks>
	private static bool TryConvertRange(RangePayload? range, TextLineMap lineMap, out TextRange textRange) =>
		ProtocolRangeConversion.TryGetOffsetRange(range, lineMap, out textRange);
}
