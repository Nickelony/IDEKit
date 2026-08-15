using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.LanguageServer.Provider;

public abstract partial class LanguageServerIntelliSenseProviderBase
{
	/// <inheritdoc/>
	/// <remarks>
	/// Every call sends a fresh <c>textDocument/completion</c> request; the server's
	/// <c>isIncomplete</c> flag is not acted upon because the provider keeps no completion-list
	/// cache, so each refresh (keystroke or manual trigger) already re-queries the server, which
	/// satisfies the protocol requirement to re-fetch incomplete lists.
	/// </remarks>
	public virtual async Task<IReadOnlyList<TextCompletionItem>> GetCompletionItemsAsync(LanguageServerCompletionRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		return await SendDocumentPositionRequestAsync<CompletionResponse?, IReadOnlyList<TextCompletionItem>>(
			request.FilePath, request.DocumentText, request.Position, LspMethodNames.Completion,
			(textDocument, protocolPosition) => new CompletionParams(textDocument, protocolPosition, BuildCompletionContext(request.TriggerCharacter)),
			response =>
			{
				IReadOnlyList<CompletionItemPayload> itemPayloads = response?.Items ?? [];

				if (itemPayloads.Count == 0)
					return [];

				ILanguageServerClient? client = Client;
				CompletionResolveFactory? resolveFactory =
					client is not null && client.SupportsCompletionResolve
						? (unresolvedItem, itemPayload, priorityRank) =>
							resolveCancellationToken => ResolveCompletionItemAsync(client, unresolvedItem, itemPayload, priorityRank, request.FilePath, request.DocumentText, resolveCancellationToken)
						: null;

				return ResponseParser.ParseCompletionItems(itemPayloads, request.DocumentText, resolveFactory);
			},
			fallbackValue: [],
			cancellationToken).ConfigureAwait(false);
	}

	private static CompletionContextPayload BuildCompletionContext(string? triggerCharacter)
	{
		// The request record normalizes the trigger: a blank value is already null and other values
		// are trimmed, so the protocol projection only distinguishes invoked from trigger-character.
		return triggerCharacter is null
			? new CompletionContextPayload(TriggerKind: CompletionTriggerKind.Invoked)
			: new CompletionContextPayload(TriggerKind: CompletionTriggerKind.TriggerCharacter, triggerCharacter);
	}

	private async Task<TextCompletionItem> ResolveCompletionItemAsync(ILanguageServerClient client, TextCompletionItem unresolvedItem, CompletionItemPayload itemPayload, int priorityRank, string filePath, string content, CancellationToken cancellationToken)
	{
		// The callback captures the client instance that negotiated resolve support, and the instance
		// never changes; the re-check below still covers a capability reset since the capture.
		if (!client.SupportsCompletionResolve)
			return unresolvedItem;

		try
		{
			CompletionItemPayload? resolvedItem = await SendRequestAsync<CompletionItemPayload?>(LspMethodNames.CompletionResolve, itemPayload,
				fallbackValue: null, cancellationToken).ConfigureAwait(false);

			if (resolvedItem is not null)
			{
				// The resolved payload's text edits are offset-mapped against the content the completion request
				// captured. If the tracked document has moved on since, those offsets no longer describe the current
				// text, so the edit fields are dropped before parsing and only the content fields are adopted, rather
				// than committing text at stale offsets.
				CompletionItemPayload payloadToParse = ContentMatchesTrackedSnapshot(filePath, content)
					? resolvedItem
					: resolvedItem with { TextEdit = null, AdditionalTextEdits = null };

				TextCompletionItem? parsedItem = ResponseParser.ParseCompletionItem(payloadToParse, priorityRank, content);

				if (parsedItem is not null)
					return unresolvedItem.WithResolvedContent(parsedItem);
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			Logger.LogWarning(exception, "Failed to resolve {DisplayName} completion item '{Label}'; falling back to the unresolved item.", ProviderDisplayName, unresolvedItem.Label);
		}

		return unresolvedItem;
	}

	// Reports whether the tracked document still holds the content the completion request captured, so the
	// resolved payload's offset-based edits describe the current text. A document that is no longer tracked (or
	// that has moved on) does not match.
	private bool ContentMatchesTrackedSnapshot(string filePath, string content)
		=> _documents.GetDocumentSnapshot(filePath) is { } document
			&& string.Equals(document.Content, content, StringComparison.Ordinal);
}
