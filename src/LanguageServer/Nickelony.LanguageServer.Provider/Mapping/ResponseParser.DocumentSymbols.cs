using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.DocumentSymbols;

namespace Nickelony.LanguageServer.Provider;

internal static partial class ResponseParser
{
	/// <summary>
	/// Parses document symbols from an LSP document-symbol response into the shared offset-based model.
	/// </summary>
	/// <param name="response">The document-symbol response payload, or <see langword="null"/> when unavailable.</param>
	/// <param name="content">The document content the ranges are resolved against.</param>
	/// <param name="documentFilePath">
	/// The normalized local path of the requested document, used to keep flat symbol entries that target
	/// the same document.
	/// </param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for no logging.</param>
	/// <returns>
	/// The parsed symbols in response order with nested children preserved, or an empty list when no
	/// usable symbol is present.
	/// </returns>
	internal static IReadOnlyList<TextDocumentSymbol> ParseDocumentSymbols(
		DocumentSymbolsResponse? response, string content, string documentFilePath, ILogger? logger = null)
	{
		if (response is null || response.Symbols.Count == 0)
			return [];

		IReadOnlyList<DocumentSymbolPayload> symbols = response.Symbols;

		// Out-of-document coordinates are clamped by the line map; negative coordinates are rejected
		// per range, matching the hover and workspace-edit parsers, so a malformed position yields an
		// entry without a range instead of an entry pointing at the file start.
		TextLineMap lineMap = TextLineMap.Build(content);

		var result = new List<TextDocumentSymbol>(symbols.Count);

		for (int i = 0; i < symbols.Count; i++)
		{
			TextDocumentSymbol? symbol = ParseDocumentSymbol(symbols[i], lineMap, documentFilePath, logger);

			if (symbol is not null)
				result.Add(symbol);
		}

		return result;
	}

	/// <summary>
	/// Parses one document-symbol entry.
	/// </summary>
	/// <param name="payload">The symbol payload to parse.</param>
	/// <param name="lineMap">The line map of the document content.</param>
	/// <param name="documentFilePath">The normalized local path of the requested document.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for no logging.</param>
	/// <returns>The parsed symbol, or <see langword="null"/> when the entry is not usable.</returns>
	private static TextDocumentSymbol? ParseDocumentSymbol(
		DocumentSymbolPayload payload, TextLineMap lineMap, string documentFilePath, ILogger? logger)
	{
		// A server may name an anonymous symbol with a single space and give its children the same placeholder, so
		// only a null or empty name is unusable here; whitespace-only names are deliberate and are kept.
		if (payload.Name is null || payload.Name.Length == 0)
			return null;

		// The protocol kind is required by the payload contract; a hand-built payload without one carries the
		// undefined enum value zero, which maps to the protocol's first kind at the call site because the
		// protocol defines no default symbol kind.
		TextDocumentSymbolKind kind = TextDocumentSymbolKindConversion.TryFromLspKind((int)payload.Kind, out TextDocumentSymbolKind mappedKind)
			? mappedKind
			: TextDocumentSymbolKind.File;

		// A flat SymbolInformation entry describes one location; entries that target another document
		// do not belong in this document's outline and are skipped.
		if (payload.Location is { } location)
		{
			if (!LanguageServerPaths.TryGetLocalPath(location.Uri, out string locationFilePath)
				|| !LanguageServerPaths.AreLocalPathsEqual(locationFilePath, documentFilePath))
			{
				return null;
			}

			// A payload that carries both shapes silently loses the hierarchical half for this entry;
			// the flat location wins by contract, so at least make the choice observable.
			if (logger is not null && (payload.Range is not null || payload.Children is { Count: > 0 }))
			{
				logger.LogDebug("Document symbol '{Name}' carried both a flat location and hierarchical ranges; the flat entry wins.",
					payload.Name);
			}

			return new TextDocumentSymbol(
				payload.Name,
				kind,
				TryConvertRange(location.Range, lineMap, out TextRange locationRange) ? locationRange : null,
				selectionRange: null)
			{
				Detail = payload.Detail ?? payload.ContainerName
			};
		}

		return new TextDocumentSymbol(
			payload.Name,
			kind,
			TryConvertRange(payload.Range, lineMap, out TextRange range) ? range : null,
			TryConvertRange(payload.SelectionRange, lineMap, out TextRange selectionRange) ? selectionRange : null)
		{
			Detail = payload.Detail,
			Children = ParseChildSymbols(payload.Children, lineMap, documentFilePath, logger)
		};
	}

	/// <summary>
	/// Parses the nested children of a hierarchical symbol entry.
	/// </summary>
	/// <param name="children">The child payloads, or <see langword="null"/> when none are present.</param>
	/// <param name="lineMap">The line map of the document content.</param>
	/// <param name="documentFilePath">The normalized local path of the requested document.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for no logging.</param>
	/// <returns>The parsed children in response order, or an empty list when none are usable.</returns>
	private static List<TextDocumentSymbol> ParseChildSymbols(
		IReadOnlyList<DocumentSymbolPayload>? children, TextLineMap lineMap, string documentFilePath, ILogger? logger)
	{
		if (children is not { Count: > 0 })
			return [];

		var result = new List<TextDocumentSymbol>(children.Count);

		for (int i = 0; i < children.Count; i++)
		{
			TextDocumentSymbol? child = ParseDocumentSymbol(children[i], lineMap, documentFilePath, logger);

			if (child is not null)
				result.Add(child);
		}

		return result;
	}

	/// <summary>
	/// Converts a protocol range to the corresponding offset range.
	/// </summary>
	/// <param name="range">The protocol range, or <see langword="null"/> when absent.</param>
	/// <param name="lineMap">The line map of the document content.</param>
	/// <param name="textRange">Receives the converted offset range.</param>
	/// <returns><see langword="true"/> when the range maps to a representable offset range.</returns>
	private static bool TryConvertRange(ProtocolRangePayload? range, TextLineMap lineMap, out TextRange textRange)
	{
		textRange = default;

		if (range is not { } protocolRange)
			return false;

		// Reject an inverted range on the raw protocol coordinates first: clamping the line and
		// character values would collapse the endpoints and hide the inversion.
		if (!IsOrderedRange(protocolRange.Start, protocolRange.End))
			return false;

		// Negative coordinates are rejected like the other response parsers reject them: an outline
		// entry pointing at the file start would misrepresent a malformed source position.
		if (protocolRange.Start.Line < 0 || protocolRange.Start.Character < 0
			|| protocolRange.End.Line < 0 || protocolRange.End.Character < 0)
		{
			return false;
		}

		var positionRange = new TextPositionRange(
			new TextPosition(protocolRange.Start.Line, protocolRange.Start.Character),
			new TextPosition(protocolRange.End.Line, protocolRange.End.Character));

		return lineMap.TryGetOffsets(positionRange, out textRange);
	}
}
