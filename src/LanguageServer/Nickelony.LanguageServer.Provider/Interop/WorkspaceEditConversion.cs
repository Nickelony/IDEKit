using Nickelony.IDEKit.Core.Text;
using System.Diagnostics.CodeAnalysis;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Converts an LSP <c>WorkspaceEdit</c> payload into the shared <see cref="TextWorkspaceEdit"/> model.
/// </summary>
/// <remarks>
/// <para>
/// The rules are protocol-level, not language-specific: LSP defines <c>changes</c> and <c>documentChanges</c> as
/// alternative representations of the same edit set, so <c>documentChanges</c> takes precedence; a list that
/// yields no usable edit falls back to a populated <c>changes</c> map instead of discarding the edit. An
/// unresolvable URI or an unsupported resource operation fails the whole response closed, because a partial
/// rename must not be produced from a target that cannot be represented.
/// </para>
/// <para>
/// Individual text edits that cannot be mapped to a document range, or that carry no replacement text, are
/// skipped rather than approximated: a malformed payload must not silently turn into a deletion.
/// </para>
/// </remarks>
public static class WorkspaceEditConversion
{
	/// <summary>
	/// Parses a workspace edit from a server response.
	/// </summary>
	/// <param name="response">The workspace edit response payload, or <see langword="null"/> when unavailable.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for no logging.</param>
	/// <returns>
	/// The parsed workspace edit, or <see langword="null"/> when no valid edits are present or the response contains an
	/// unsupported resource operation.
	/// </returns>
	public static TextWorkspaceEdit? Parse(WorkspaceEditResponse? response, ILogger? logger = null)
	{
		if (response is null)
			return null;

		var editsByFile = new Dictionary<string, List<TextEdit>>(LanguageServerPaths.LocalPathComparer);

		// documentChanges takes precedence; it fails closed for resource operations and unresolvable URIs.
		if (response.Value.DocumentChanges is not null
			&& !ParseDocumentChanges(response.Value.DocumentChanges, editsByFile, logger))
		{
			return null;
		}

		// A documentChanges list that produced no usable edit - because it is absent, empty, or every
		// edit was skipped - is treated as absent so it cannot silently discard a populated changes map.
		if (!HasUsableEdit(editsByFile)
			&& (!ParseChangeMap(response.Value.Changes, editsByFile, logger) || !HasUsableEdit(editsByFile)))
		{
			return null;
		}

		var documentEdits = new List<TextDocumentEdit>(editsByFile.Count);

		foreach ((string filePath, List<TextEdit> textEdits) in editsByFile)
		{
			if (textEdits.Count == 0)
				continue;

			documentEdits.Add(new TextDocumentEdit(filePath, textEdits));
		}

		return documentEdits.Count == 0
			? null
			: new TextWorkspaceEdit(documentEdits);
	}

	/// <summary>
	/// Appends the convertible edits from <paramref name="edits"/> to <paramref name="textEdits"/>.
	/// </summary>
	/// <param name="edits">The protocol edits to convert, or <see langword="null"/> when unavailable.</param>
	/// <param name="textEdits">The destination list that receives the converted edits.</param>
	public static void AppendTextEdits(IReadOnlyList<TextEditPayload>? edits, List<TextEdit> textEdits)
	{
		if (edits is null)
			return;

		for (int i = 0; i < edits.Count; i++)
		{
			if (TryParseTextEdit(edits[i], out TextEdit? textEdit))
				textEdits.Add(textEdit);
		}
	}

	private static bool HasUsableEdit(Dictionary<string, List<TextEdit>> editsByFile)
	{
		foreach (List<TextEdit> textEdits in editsByFile.Values)
		{
			if (textEdits.Count > 0)
				return true;
		}

		return false;
	}

	private static bool ParseChangeMap(IReadOnlyDictionary<string, IReadOnlyList<TextEditPayload>?>? changes,
		Dictionary<string, List<TextEdit>> editsByFile, ILogger? logger)
	{
		// A null map is an absent representation, not a failure.
		if (changes is null)
			return true;

		foreach ((string uri, IReadOnlyList<TextEditPayload>? edits) in changes)
		{
			if (!LanguageServerPaths.TryGetLocalPath(uri, out string filePath))
			{
				logger?.LogWarning(
					"Ignoring workspace edit because a change-map URI could not be resolved to a local file path (uri: '{Uri}').",
					uri);

				return false; // Fail closed so an unresolvable target cannot produce a partial edit.
			}

			List<TextEdit> textEdits = GetOrCreateTextEditBucket(editsByFile, filePath);
			AppendTextEdits(edits, textEdits);
		}

		return true;
	}

	private static bool ParseDocumentChanges(IReadOnlyList<WorkspaceDocumentChangePayload>? documentChanges,
		Dictionary<string, List<TextEdit>> editsByFile, ILogger? logger)
	{
		if (documentChanges is null)
			return true;

		for (int i = 0; i < documentChanges.Count; i++)
		{
			WorkspaceDocumentChangePayload documentChange = documentChanges[i];

			if (documentChange.IsResourceOperation)
			{
				logger?.LogWarning(
					"Ignoring workspace edit because it contains unsupported resource operation '{Kind}' (uri: '{Uri}', oldUri: '{OldUri}', newUri: '{NewUri}').",
					documentChange.Kind,
					documentChange.Uri ?? string.Empty,
					documentChange.OldUri ?? string.Empty,
					documentChange.NewUri ?? string.Empty);

				return false; // Resource operations cannot be represented by the shared edit model (see TextWorkspaceEdit), so fail closed.
			}

			if (!LanguageServerPaths.TryGetLocalPath(documentChange.TextDocument?.Uri, out string filePath))
			{
				logger?.LogWarning(
					"Ignoring workspace edit because a document-change URI could not be resolved to a local file path (uri: '{Uri}').",
					documentChange.TextDocument?.Uri ?? string.Empty);

				return false; // Fail closed so an unresolvable target cannot produce a partial edit.
			}

			List<TextEdit> textEdits = GetOrCreateTextEditBucket(editsByFile, filePath);
			AppendTextEdits(documentChange.Edits, textEdits);
		}

		return true;
	}

	/// <summary>
	/// Tries to convert a single protocol edit into a shared text edit.
	/// </summary>
	/// <param name="edit">The protocol edit to convert.</param>
	/// <param name="textEdit">The converted edit when successful.</param>
	/// <returns>
	/// <see langword="true"/> when the edit has a valid range and replacement text; otherwise, <see langword="false"/>.
	/// </returns>
	private static bool TryParseTextEdit(TextEditPayload edit, [NotNullWhen(true)] out TextEdit? textEdit)
	{
		textEdit = null;

		if (!ProtocolRangeConversion.TryGetTextPositionRange(edit.Range, out TextPositionRange range)
			|| edit.NewText is null)
		{
			return false;
		}

		textEdit = new TextEdit(range, edit.NewText);
		return true;
	}

	/// <summary>
	/// Gets the edit bucket for <paramref name="filePath"/>, creating it when absent.
	/// </summary>
	/// <param name="editsByFile">The per-file edit buckets.</param>
	/// <param name="filePath">The normalized target file path.</param>
	/// <returns>The bucket that receives the converted edits for the file.</returns>
	private static List<TextEdit> GetOrCreateTextEditBucket(Dictionary<string, List<TextEdit>> editsByFile, string filePath)
	{
		if (!editsByFile.TryGetValue(filePath, out List<TextEdit>? textEdits))
		{
			textEdits = [];
			editsByFile[filePath] = textEdits;
		}

		return textEdits;
	}
}
