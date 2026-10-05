using Nickelony.IDEKit.LanguageFeatures.Markup;

namespace Nickelony.IDEKit.LanguageFeatures.Completion;

/// <summary>Merges the richer content of a resolved completion item onto the item a host already shows.</summary>
/// <remarks>
/// The per-field rules are the table in the language features guide. The merge reads the item's backing fields, which are internal for that reason.
/// </remarks>
internal static class CompletionItemMerge
{
	// Merges the resolved item's richer content into the original item, returning the original
	// instance unchanged when no field would change.
	internal static CompletionItem Apply(CompletionItem item, CompletionItem resolvedItem)
	{
		string? detail = string.IsNullOrWhiteSpace(resolvedItem.Detail) ? item.Detail : resolvedItem.Detail;
		MarkupContent? documentation = resolvedItem.Documentation ?? item.Documentation;

		CompletionItemKind kind = resolvedItem._kind ?? item.Kind;
		CompletionTextEdit? textEdit = item.TextEdit ?? resolvedItem.TextEdit;
		CompletionInsertTextFormat insertTextFormat = resolvedItem._insertTextFormat ?? item.InsertTextFormat;
		string? sortText = item.SortText ?? resolvedItem.SortText;
		IReadOnlyList<CompletionItemTag> tags = MergeTags(item._tags, resolvedItem._tags);
		IReadOnlyList<string> commitCharacters = item._commitCharacters.Count == 0 ? resolvedItem._commitCharacters : item._commitCharacters;

		IReadOnlyList<CompletionTextEdit> additionalTextEdits =
			item._additionalTextEdits.Count == 0 ? resolvedItem._additionalTextEdits : item._additionalTextEdits;

		string? insertText = item.TextEdit is null && item._insertText is null ? resolvedItem._insertText : item._insertText;
		string? filterText = item._filterText ?? resolvedItem._filterText;

		if (
			string.Equals(detail, item.Detail, StringComparison.Ordinal)
			&& Equals(documentation, item.Documentation)
			&& kind == item.Kind
			&& textEdit == item.TextEdit
			&& insertTextFormat == item.InsertTextFormat
			&& string.Equals(sortText, item.SortText, StringComparison.Ordinal)
			&& ReferenceEquals(tags, item._tags)
			&& ReferenceEquals(commitCharacters, item._commitCharacters)
			&& ReferenceEquals(additionalTextEdits, item._additionalTextEdits)
			&& string.Equals(insertText, item._insertText, StringComparison.Ordinal)
			&& string.Equals(filterText, item._filterText, StringComparison.Ordinal)
		)
		{
			return item;
		}

		CompletionItem merged = new(item)
		{
			_insertText = insertText,
			_documentation = documentation,
			_kind = kind,
			_detail = detail,
			_filterText = filterText,
			_textEdit = textEdit,
			_insertTextFormat = insertTextFormat,
			_sortText = sortText,
			_tags = tags,
			_commitCharacters = commitCharacters,
			_additionalTextEdits = additionalTextEdits,
			_resolveAsync = null,
		};

		return merged;
	}

	private static IReadOnlyList<CompletionItemTag> MergeTags(IReadOnlyList<CompletionItemTag> current, IReadOnlyList<CompletionItemTag> resolved)
	{
		if (resolved.Count == 0)
			return current;

		var merged = new List<CompletionItemTag>(current);

		foreach (CompletionItemTag tag in resolved)
		{
			if (!merged.Contains(tag))
				merged.Add(tag);
		}

		return merged.Count == current.Count ? current : merged.AsReadOnly();
	}
}
