using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Infrastructure;
using Nickelony.IDEKit.IntelliSense.Completion;
using System.Text.Json;

namespace Nickelony.LanguageServer.Provider;

internal static partial class ResponseParser
{
	// The priority hint is a single descending number (higher sorts first). The base leaves room for the
	// response-order rank of the largest realistic list without reaching zero, and the preselection bonus
	// lifts a preselected item above every ranked item.
	private const double CompletionPriorityBase = 100000.0;
	private const double PreselectedPriorityBonus = 1000000.0;

	/// <summary>
	/// Parses a sequence of typed completion-item payloads into shared completion items.
	/// </summary>
	/// <param name="itemPayloads">The typed completion-item payloads.</param>
	/// <param name="content">The document content used to resolve text-edit offsets.</param>
	/// <param name="resolveFactory">Builds an optional lazy-resolve callback for items that need a resolve round trip.</param>
	/// <returns>The valid parsed completion items in response order.</returns>
	/// <remarks>
	/// The projection is protocol-faithful: every payload with a usable label becomes one item, in
	/// response order, and duplicate entries stay visible exactly as the server sent them - the
	/// protocol permits duplicates and established clients pass the server list through unchanged.
	/// Each item receives its own priority hint and, when it needs a resolve round trip, its own
	/// resolve callback.
	/// </remarks>
	internal static IReadOnlyList<TextCompletionItem> ParseCompletionItems(IEnumerable<CompletionItemPayload> itemPayloads,
		string content,
		CompletionResolveFactory? resolveFactory = null)
	{
		// Materialize the payloads so the protocol ordering key (sortText, falling back to the label)
		// can be ranked across the whole list before each item receives its priority hint.
		CompletionItemPayload[] payloads = [.. itemPayloads];
		int[] priorityRanks = ComputePriorityRanks(payloads);
		TextLineMap lineMap = TextLineMap.Build(content);

		var items = new List<TextCompletionItem>(payloads.Length);

		for (int itemIndex = 0; itemIndex < payloads.Length; itemIndex++)
		{
			TextCompletionItem? item = ParseCompletionItem(payloads[itemIndex], priorityRanks[itemIndex], lineMap);

			if (item is null)
				continue;

			// The callback is attached per item, so two server-identical entries each carry their own
			// resolve round trip.
			if (resolveFactory is not null && CompletionItemNeedsResolve(item, payloads[itemIndex]))
				item = item.WithResolveCallback(resolveFactory(item, payloads[itemIndex], priorityRanks[itemIndex]));

			items.Add(item);
		}

		return items;
	}

	/// <summary>
	/// Parses a single typed LSP completion-item payload into a <see cref="TextCompletionItem"/>.
	/// </summary>
	/// <param name="itemPayload">The typed completion-item payload.</param>
	/// <param name="priorityRank">The zero-based protocol ordering rank used for the priority hint.</param>
	/// <param name="content">The document content used to resolve text-edit offsets.</param>
	/// <param name="resolveAsync">An optional lazy-resolve callback.</param>
	/// <returns>The parsed completion item, or <see langword="null"/> when the payload has no usable label.</returns>
	internal static TextCompletionItem? ParseCompletionItem(CompletionItemPayload itemPayload, int priorityRank,
		string content,
		Func<CancellationToken, Task<TextCompletionItem>>? resolveAsync = null)
		=> ParseCompletionItem(itemPayload, priorityRank, TextLineMap.Build(content), resolveAsync);

	/// <summary>
	/// Parses a single typed LSP completion-item payload using a prebuilt line map.
	/// </summary>
	/// <param name="itemPayload">The typed completion-item payload.</param>
	/// <param name="priorityRank">The zero-based protocol ordering rank used for the priority hint.</param>
	/// <param name="lineMap">The line map of the document content used to resolve text-edit offsets.</param>
	/// <param name="resolveAsync">An optional lazy-resolve callback.</param>
	/// <returns>The parsed completion item, or <see langword="null"/> when the payload has no usable label.</returns>
	internal static TextCompletionItem? ParseCompletionItem(CompletionItemPayload itemPayload, int priorityRank,
		TextLineMap lineMap,
		Func<CancellationToken, Task<TextCompletionItem>>? resolveAsync = null)
	{
		string? label = itemPayload.Label;

		if (string.IsNullOrWhiteSpace(label))
			return null;

		TextCompletionTextEdit? textEdit = ExtractCompletionTextEdit(itemPayload, lineMap);

		// The edit supersedes the plain insertion text, so an unset value keeps the label fallback
		// while the edit carries the commit text. Insertion and filter text stay null when the payload
		// supplied none, which the shared model treats as the absent state: a later resolve can still
		// adopt the server's richer text (assigning the label itself would pin the fallback).
		// Snippet text passes through verbatim: the insert-text format travels with the item and the
		// consuming host expands the snippet at commit time.
		string? insertText = itemPayload.InsertText;

		// An absent or unknown protocol format reads as plain text, matching the contract on the
		// protocol enum.
		TextCompletionInsertTextFormat insertTextFormat = itemPayload.InsertTextFormat == InsertTextFormat.Snippet
			? TextCompletionInsertTextFormat.Snippet
			: TextCompletionInsertTextFormat.PlainText;

		string? detail = BuildCompletionDetail(itemPayload);
		ProtocolMarkupContent description = BuildCompletionDescription(itemPayload);

		// The protocol kind maps one-to-one through the shared taxonomy; a missing or out-of-range
		// value maps to the presentation fallback (Generic).
		TextCompletionItemKind completionKind = itemPayload.Kind is { } protocolKind
			? TextCompletionItemKindConversion.FromLspKind((int)protocolKind)
			: TextCompletionItemKind.Generic;

		return new(label)
		{
			InsertText = insertText,
			Documentation = description.Text,
			DocumentationKind = description.Kind,
			Priority = BuildCompletionPriority(priorityRank, itemPayload.Preselect == true),
			SortText = itemPayload.SortText,
			IsPreselected = itemPayload.Preselect == true,
			Kind = completionKind,
			Detail = detail,
			FilterText = itemPayload.FilterText,
			ResolveCallback = resolveAsync,
			TextEdit = textEdit,
			InsertTextFormat = insertTextFormat,
			Tags = BuildCompletionTags(itemPayload),
			CommitCharacters = BuildCompletionCommitCharacters(itemPayload.CommitCharacters),
			AdditionalTextEdits = BuildCompletionAdditionalEdits(itemPayload.AdditionalTextEdits, lineMap)
		};
	}

	private static TextCompletionTextEdit? ExtractCompletionTextEdit(CompletionItemPayload itemPayload, TextLineMap lineMap)
	{
		if (itemPayload.TextEdit is not { } textEditPayload)
			return null;

		return ParseCompletionTextEdit(textEditPayload, lineMap);
	}

	private static TextCompletionTextEdit? ParseCompletionTextEdit(CompletionTextEditPayload textEditPayload, TextLineMap lineMap)
	{
		switch (textEditPayload)
		{
			case CompletionRangeTextEditPayload rangeEdit:
				return TryParseCompletionRange(rangeEdit.Range, lineMap, out TextRange range)
					? new(range, newText: rangeEdit.NewText)
					: null;

			case CompletionInsertReplaceTextEditPayload insertReplaceEdit:
				if (!TryParseCompletionRange(insertReplaceEdit.Insert, lineMap, out TextRange insertRange)
					|| !TryParseCompletionRange(insertReplaceEdit.Replace, lineMap, out TextRange replaceRange))
				{
					return null;
				}

				// A pair whose replace range does not share the insert range's start violates the shared
				// edit relation but still carries the server's replacement; degrade to a replace-range
				// edit instead of dropping the edit and inserting the plain text at the caret.
				return TextCompletionTextEdit.TryCreate(
					insertRange,
					replaceRange,
					insertReplaceEdit.NewText,
					out TextCompletionTextEdit insertReplaceTextEdit)
						? insertReplaceTextEdit
						: new TextCompletionTextEdit(replaceRange, newText: insertReplaceEdit.NewText);

			default:
				return null;
		}
	}

	private static bool TryParseCompletionRange(ProtocolRangePayload rangePayload, TextLineMap lineMap, out TextRange range)
	{
		range = default;

		// Reject an inverted range on the raw protocol coordinates first: clamping the line and
		// character values would collapse the endpoints and hide the inversion.
		if (!IsOrderedRange(rangePayload.Start, rangePayload.End))
			return false;

		if (!TryResolveCompletionOffset(rangePayload.Start, lineMap, out int startOffset)
			|| !TryResolveCompletionOffset(rangePayload.End, lineMap, out int endOffset)
			|| endOffset < startOffset)
		{
			return false;
		}

		range = new TextRange(startOffset, endOffset - startOffset);
		return true;
	}

	private static bool TryResolveCompletionOffset(ProtocolPosition position, TextLineMap lineMap, out int offset)
	{
		offset = 0;

		// A line beyond the document is dropped instead of clamped: a stale item's edit target no
		// longer exists, and clamping would commit its text at the wrong place; the item stays usable
		// through its label/insert text at the caret. Characters beyond the line length are clamped
		// because the end-of-line target still exists.
		if (position.Line < 0 || position.Character < 0 || position.Line >= lineMap.LineCount)
			return false;

		offset = lineMap.GetOffset(position.Line, position.Character);
		return true;
	}

	private static bool CompletionItemNeedsResolve(TextCompletionItem item, CompletionItemPayload itemPayload)
		=> string.IsNullOrEmpty(item.Detail)
			|| string.IsNullOrEmpty(item.Documentation)
			// A server can signal resolve-only additions (such as an auto-import additional edit) through the
			// item's opaque data member; request resolve for such an item even when detail and documentation are
			// already present, or the additions would never arrive for an item that carries no additional edits yet.
			|| (HasResolveData(itemPayload) && item.AdditionalTextEdits.Count == 0);

	private static bool HasResolveData(CompletionItemPayload itemPayload)
		=> itemPayload.ExtensionData is { Count: > 0 } extensionData
			&& extensionData.TryGetValue("data", out JsonElement data)
			&& data.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;

	/// <summary>
	/// Maps the protocol commit characters into the shared list, dropping null entries: the shared
	/// model rejects a null entry because it can never match a typed character, and a malformed
	/// payload must degrade instead of failing the whole completion request.
	/// </summary>
	private static List<string> BuildCompletionCommitCharacters(IReadOnlyList<string>? commitCharacters)
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

	/// <summary>
	/// Maps the item's deprecation onto the shared tag set.
	/// </summary>
	/// <remarks>
	/// Both the modern <c>tags</c> array and the legacy <c>deprecated</c> boolean are honoured, because a server
	/// may emit only the legacy flag; relying on <c>tags</c> alone would leave deprecated functions
	/// unmarked in the completion list.
	/// </remarks>
	private static List<TextCompletionTag> BuildCompletionTags(CompletionItemPayload itemPayload)
		=> itemPayload.IsDeprecated ? [TextCompletionTag.Deprecated] : [];

	/// <summary>
	/// Resolves the protocol additional text edits into offset-based edits. A malformed entry is
	/// skipped individually - the item and its other edits stay usable, matching the primary-edit
	/// contract.
	/// </summary>
	private static List<TextCompletionTextEdit> BuildCompletionAdditionalEdits(
		IReadOnlyList<TextEditPayload>? additionalTextEdits,
		TextLineMap lineMap)
	{
		if (additionalTextEdits is null || additionalTextEdits.Count == 0)
			return [];

		var edits = new List<TextCompletionTextEdit>(additionalTextEdits.Count);

		foreach (TextEditPayload edit in additionalTextEdits)
		{
			// An entry whose range cannot be resolved or that misses its replacement text is dropped
			// individually, so one malformed side effect cannot fail the whole completion item.
			if (edit.Range is not { } range
				|| edit.NewText is null
				|| !TryParseCompletionRange(range, lineMap, out TextRange textRange))
			{
				continue;
			}

			edits.Add(new TextCompletionTextEdit(textRange, newText: edit.NewText));
		}

		return edits;
	}

	/// <summary>
	/// Builds the derived priority hint from the protocol ordering rank and the preselect flag.
	/// Higher values sort first for consumers that order by a single number.
	/// </summary>
	/// <remarks>
	/// The rank follows the protocol ordering key (<c>sortText</c>, falling back to the label), so the
	/// numeric hint agrees with <see cref="TextCompletionItem.SortText"/>; the protocol fields remain
	/// authoritative and are carried separately on the item. A preselected item is surfaced above
	/// every other item.
	/// </remarks>
	private static double BuildCompletionPriority(int priorityRank, bool isPreselected)
	{
		double priority = CompletionPriorityBase - priorityRank;

		if (isPreselected)
			priority += PreselectedPriorityBonus;

		return priority;
	}

	/// <summary>
	/// Assigns every payload its zero-based rank in protocol order; each parsed item uses its rank as
	/// the priority hint input.
	/// </summary>
	private static int[] ComputePriorityRanks(CompletionItemPayload[] payloads)
	{
		var indices = new int[payloads.Length];

		// Precompute each payload's ordering key once: the comparator runs O(n log n) times, so resolving the key
		// inside it would re-read and, for a label fallback, re-materialize the same string on every comparison.
		var sortKeys = new string[payloads.Length];

		for (int i = 0; i < indices.Length; i++)
		{
			indices[i] = i;
			sortKeys[i] = GetPrioritySortKey(payloads[i]);
		}

		Array.Sort(indices, (left, right) =>
		{
			int comparison = string.CompareOrdinal(sortKeys[left], sortKeys[right]);

			// Equal sort keys keep the server's response order.
			return comparison != 0 ? comparison : left.CompareTo(right);
		});

		var ranks = new int[payloads.Length];

		for (int rank = 0; rank < indices.Length; rank++)
			ranks[indices[rank]] = rank;

		return ranks;
	}

	/// <summary>
	/// Gets a payload's protocol ordering key: <c>sortText</c> when present, otherwise the label.
	/// </summary>
	private static string GetPrioritySortKey(CompletionItemPayload payload)
		=> string.IsNullOrWhiteSpace(payload.SortText) ? payload.Label ?? string.Empty : payload.SortText;

	private static string? BuildCompletionDetail(CompletionItemPayload itemPayload)
		=> OptionalText.Normalize(itemPayload.Detail);

	private static ProtocolMarkupContent BuildCompletionDescription(CompletionItemPayload itemPayload)
		=> itemPayload.Documentation is { } documentationElement
			? ParseMarkupContent(documentationElement)
			: default;
}
