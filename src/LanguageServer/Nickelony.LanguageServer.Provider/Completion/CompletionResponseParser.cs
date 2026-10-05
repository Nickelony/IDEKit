using System.Text.Json;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Markup;
using Lsp = Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

internal static class CompletionResponseParser
{
	// The priority hint is a single descending number (higher sorts first). The base leaves room for the
	// response-order rank of the largest realistic list without reaching zero, and the preselection bonus
	// lifts a preselected item above every ranked item.
	private const double CompletionPriorityBase = 100000.0;
	private const double PreselectedPriorityBonus = 1000000.0;

	/// <summary>Parses a sequence of typed completion-item payloads into shared completion items.</summary>
	/// <param name="itemPayloads">The typed completion-item payloads.</param>
	/// <param name="content">The document content for resolving text-edit offsets.</param>
	/// <param name="resolveFactory">Builds an optional lazy-resolve callback for items that need a resolve round trip.</param>
	/// <returns>The valid parsed completion items in response order.</returns>
	/// <remarks>
	/// The projection is protocol-faithful: every payload with a usable label becomes one item, in response order, and duplicate entries
	/// stay visible exactly as the server sent them - the protocol permits duplicates and established clients pass the server list through unchanged.
	/// Each item receives its own priority hint and, when it needs a resolve round trip, its own resolve callback.
	/// </remarks>
	internal static IReadOnlyList<CompletionItem> Parse(
		IEnumerable<Lsp.CompletionItemPayload> itemPayloads,
		string content,
		CompletionResolveFactory? resolveFactory = null
	)
	{
		// Materialize the payloads so the protocol ordering key (sortText, falling back to the label)
		// can be ranked across the whole list before each item receives its priority hint.
		Lsp.CompletionItemPayload[] payloads = [.. itemPayloads];
		int[] priorityRanks = ComputePriorityRanks(payloads);

		var lineMap = TextLineMap.Build(content);

		var items = new List<CompletionItem>(payloads.Length);

		for (int itemIndex = 0; itemIndex < payloads.Length; itemIndex++)
		{
			CompletionItem? item = ParseItem(payloads[itemIndex], priorityRanks[itemIndex], lineMap);

			if (item is null)
				continue;

			// The callback is attached per item, so server-identical entries each carry their own
			// resolve round trip.
			if (resolveFactory is not null && ItemNeedsResolve(item, payloads[itemIndex]))
				item = item.WithResolveCallback(resolveFactory(item, payloads[itemIndex], priorityRanks[itemIndex]));

			items.Add(item);
		}

		return items;
	}

	/// <summary>Parses a single typed LSP completion-item payload into a <see cref="CompletionItem"/>.</summary>
	/// <param name="itemPayload">The typed completion-item payload.</param>
	/// <param name="priorityRank">The zero-based protocol ordering rank used for the priority hint.</param>
	/// <param name="content">The document content for resolving text-edit offsets.</param>
	/// <param name="resolveAsync">An optional lazy-resolve callback.</param>
	/// <returns>The parsed completion item, or <see langword="null"/> when the payload has no usable label.</returns>
	internal static CompletionItem? ParseItem(
		Lsp.CompletionItemPayload itemPayload,
		int priorityRank,
		string content,
		Func<CancellationToken, Task<CompletionItem>>? resolveAsync = null
	) => ParseItem(itemPayload, priorityRank, lineMap: TextLineMap.Build(content), resolveAsync);

	/// <summary>Parses a single typed LSP completion-item payload using a prebuilt line map.</summary>
	/// <param name="itemPayload">The typed completion-item payload.</param>
	/// <param name="priorityRank">The zero-based protocol ordering rank used for the priority hint.</param>
	/// <param name="lineMap">The line map of the document content for resolving text-edit offsets.</param>
	/// <param name="resolveAsync">An optional lazy-resolve callback.</param>
	/// <returns>The parsed completion item, or <see langword="null"/> when the payload has no usable label.</returns>
	internal static CompletionItem? ParseItem(
		Lsp.CompletionItemPayload itemPayload,
		int priorityRank,
		TextLineMap lineMap,
		Func<CancellationToken, Task<CompletionItem>>? resolveAsync = null
	)
	{
		string? label = itemPayload.Label;

		if (string.IsNullOrWhiteSpace(label))
			return null;

		CompletionTextEdit? textEdit = ExtractTextEdit(itemPayload, lineMap);

		// The edit supersedes the plain insertion text, so an unset value keeps the label fallback
		// while the edit carries the commit text. Insertion and filter text stay null when the payload
		// supplied none, which the shared model treats as the absent state: a later resolve can still
		// adopt the server's richer text (assigning the label itself would pin the fallback).
		// Snippet text passes through verbatim: the insert-text format travels with the item and the
		// consuming host expands the snippet at commit time.
		string? insertText = itemPayload.InsertText;

		CompletionInsertTextFormat insertTextFormat = CompletionInsertTextFormatConversion.FromProtocol(itemPayload.InsertTextFormat);

		string? detail = BuildDetail(itemPayload);
		MarkupContent? description = BuildDescription(itemPayload);

		// The protocol kind maps one-to-one through the shared taxonomy; a missing or out-of-range
		// value maps to the presentation fallback (Generic).
		CompletionItemKind completionKind = CompletionItemKindConversion.FromProtocol(itemPayload.Kind);

		return new(label)
		{
			InsertText = insertText,
			Documentation = description,
			Priority = BuildPriority(priorityRank, itemPayload.Preselect == true),
			SortText = itemPayload.SortText,
			IsPreselected = itemPayload.Preselect == true,
			Kind = completionKind,
			Detail = detail,
			FilterText = itemPayload.FilterText,
			ResolveCallback = resolveAsync,
			TextEdit = textEdit,
			InsertTextFormat = insertTextFormat,
			Tags = CompletionItemTagConversion.FromProtocol(itemPayload),
			CommitCharacters = BuildCommitCharacters(itemPayload.CommitCharacters),
			AdditionalTextEdits = BuildAdditionalEdits(itemPayload.AdditionalTextEdits, lineMap),
		};
	}

	private static CompletionTextEdit? ExtractTextEdit(Lsp.CompletionItemPayload itemPayload, TextLineMap lineMap)
	{
		if (itemPayload.TextEdit is not { } textEditPayload)
			return null;

		return ParseTextEdit(textEditPayload, lineMap);
	}

	private static CompletionTextEdit? ParseTextEdit(Lsp.CompletionTextEditPayload textEditPayload, TextLineMap lineMap)
	{
		switch (textEditPayload)
		{
			case Lsp.CompletionRangeTextEditPayload rangeEdit:
				return TryParseRange(rangeEdit.Range, lineMap, out TextRange range) ? new(range, newText: rangeEdit.NewText) : null;

			case Lsp.CompletionInsertReplaceTextEditPayload insertReplaceEdit:
				if (
					!TryParseRange(insertReplaceEdit.Insert, lineMap, out TextRange insertRange)
					|| !TryParseRange(insertReplaceEdit.Replace, lineMap, out TextRange replaceRange)
				)
				{
					return null;
				}

				// A pair whose replace range does not share the insert range's start violates the shared
				// edit relation but still carries the server's replacement; degrade to a replace-range
				// edit instead of dropping the edit and inserting the plain text at the caret.
				return CompletionTextEdit.TryCreate(
					insertRange,
					replaceRange,
					newText: insertReplaceEdit.NewText,
					edit: out CompletionTextEdit insertReplaceTextEdit
				)
					? insertReplaceTextEdit
					: new CompletionTextEdit(replaceRange, newText: insertReplaceEdit.NewText);

			default:
				return null;
		}
	}

	private static bool TryParseRange(Lsp.RangePayload rangePayload, TextLineMap lineMap, out TextRange range)
	{
		range = default;

		// Reject an inverted range on the raw protocol coordinates first: clamping the line and
		// character values would collapse the endpoints and hide the inversion.
		if (rangePayload.IsReversed)
			return false;

		if (
			!TryResolveOffset(rangePayload.Start, lineMap, out int startOffset)
			|| !TryResolveOffset(rangePayload.End, lineMap, out int endOffset)
			|| endOffset < startOffset
		)
		{
			return false;
		}

		range = TextRange.FromBounds(startOffset, endOffset);
		return true;
	}

	private static bool TryResolveOffset(Lsp.PositionPayload position, TextLineMap lineMap, out int offset)
	{
		offset = 0;

		// A line beyond the document is dropped instead of clamped: a stale item's edit target does
		// not exist, and clamping would commit its text at the wrong place; the item stays usable
		// through its label/insert text at the caret. Characters beyond the line length are clamped
		// because the end-of-line target still exists.
		if (position.Line < 0 || position.Character < 0 || position.Line >= lineMap.LineCount)
			return false;

		offset = lineMap.GetOffset(position.Line, position.Character);
		return true;
	}

	private static bool ItemNeedsResolve(CompletionItem item, Lsp.CompletionItemPayload itemPayload)
	{
		return string.IsNullOrEmpty(item.Detail)
			|| item.Documentation is null
			// A server can signal resolve-only additions (such as an auto-import additional edit) through the
			// item's opaque data member; request resolve for such an item even when detail and documentation are
			// already present, or the additions would never arrive for an item that carries no additional edits yet.
			|| (HasResolveData(itemPayload) && item.AdditionalTextEdits.Count == 0);
	}

	private static bool HasResolveData(Lsp.CompletionItemPayload itemPayload)
	{
		return itemPayload.ExtensionData is { Count: > 0 } extensionData
			&& extensionData.TryGetValue("data", out JsonElement data)
			&& data.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
	}

	/// <summary>
	/// Maps the protocol commit characters into the shared list, dropping <see langword="null"/> entries: the shared model rejects
	/// a <see langword="null"/> entry because it can never match a typed character, and a malformed payload must degrade instead of failing the whole
	/// completion request.
	/// </summary>
	private static List<string> BuildCommitCharacters(IReadOnlyList<string>? commitCharacters)
	{
		if (commitCharacters is null || commitCharacters.Count == 0)
			return [];

		var mapped = new List<string>(commitCharacters.Count);

		foreach (string character in commitCharacters)
		{
			if (character is not null)
				mapped.Add(character);
		}

		return mapped;
	}

	/// <summary>Resolves the protocol additional text edits into offset-based edits.</summary>
	/// <remarks>
	/// A malformed entry is skipped individually - the item and its other edits stay usable, matching the primary-edit contract.
	/// </remarks>
	private static List<CompletionTextEdit> BuildAdditionalEdits(IReadOnlyList<Lsp.TextEditPayload>? additionalTextEdits, TextLineMap lineMap)
	{
		if (additionalTextEdits is null || additionalTextEdits.Count == 0)
			return [];

		var edits = new List<CompletionTextEdit>(additionalTextEdits.Count);

		foreach (Lsp.TextEditPayload edit in additionalTextEdits)
		{
			// An entry whose range cannot be resolved or that misses its replacement text is dropped
			// individually, so one malformed side effect cannot fail the whole completion item.
			if (edit.Range is not { } range || edit.NewText is null || !TryParseRange(range, lineMap, out TextRange textRange))
				continue;

			edits.Add(new CompletionTextEdit(textRange, newText: edit.NewText));
		}

		return edits;
	}

	/// <summary>Builds the derived priority hint from the protocol ordering rank and the preselect flag.</summary>
	/// <remarks>
	/// Higher values sort first for consumers that order by a single number. The rank follows the protocol ordering key (<c>sortText</c>,
	/// falling back to the label), so the numeric hint agrees with <see cref="CompletionItem.SortText"/>; the protocol fields remain authoritative
	/// and are carried separately on the item. A preselected item is surfaced above every other item.
	/// </remarks>
	private static double BuildPriority(int priorityRank, bool isPreselected)
	{
		double priority = CompletionPriorityBase - priorityRank;

		if (isPreselected)
			priority += PreselectedPriorityBonus;

		return priority;
	}

	/// <summary>Assigns every payload its zero-based rank in protocol order; each parsed item uses its rank as the priority hint input.</summary>
	private static int[] ComputePriorityRanks(Lsp.CompletionItemPayload[] itemPayloads)
	{
		int[] indices = new int[itemPayloads.Length];

		// Precompute each payload's ordering key once: the comparator runs O(n log n) times, so resolving the key
		// inside it would re-read and, for a label fallback, re-materialize the same string on every comparison.
		string[] sortKeys = new string[itemPayloads.Length];

		for (int i = 0; i < indices.Length; i++)
		{
			indices[i] = i;
			sortKeys[i] = GetPrioritySortKey(itemPayloads[i]);
		}

		Array.Sort(
			indices,

			(left, right) =>
			{
				int comparison = string.CompareOrdinal(sortKeys[left], sortKeys[right]);

				// Equal sort keys keep the server's response order.
				return comparison != 0 ? comparison : left.CompareTo(right);
			}
		);

		int[] ranks = new int[itemPayloads.Length];

		for (int rank = 0; rank < indices.Length; rank++)
			ranks[indices[rank]] = rank;

		return ranks;
	}

	/// <summary>Returns a payload's protocol ordering key: <c>sortText</c> when present, otherwise the label.</summary>
	private static string GetPrioritySortKey(Lsp.CompletionItemPayload itemPayload) =>
		string.IsNullOrWhiteSpace(itemPayload.SortText) ? itemPayload.Label ?? string.Empty : itemPayload.SortText;

	private static string? BuildDetail(Lsp.CompletionItemPayload itemPayload) => OptionalTextNormalizer.Normalize(itemPayload.Detail);

	private static MarkupContent? BuildDescription(Lsp.CompletionItemPayload itemPayload) => MarkupContentParser.Parse(itemPayload.Documentation);
}
