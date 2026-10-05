using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Answers completion requests with the language server's <c>textDocument/completion</c> response and resolves a chosen item on demand.
/// </summary>
/// <param name="pipeline">The pipeline that sends the document-scoped request.</param>
/// <param name="context">The client, the tracked documents and the logger the resolve request needs.</param>
/// <param name="requestRunner">The runner that sends the resolve request.</param>
internal sealed class CompletionProvider(IDocumentRequestPipeline pipeline, ProviderContext context, RequestRunner requestRunner)
	: ICompletionProvider
{
	/// <inheritdoc/>
	public async Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(CompletionRequest request, CancellationToken cancellationToken = default)
	{
		return await pipeline
			.SendAsync(
				filePath: request.DocumentId,
				documentText: request.DocumentText,

				request: new DocumentRequestDescriptor<CompletionResponse?, IReadOnlyList<CompletionItem>>(
					method: LspMethodNames.Completion,

					buildParameters: (textDocument, protocolPosition) =>
						new CompletionParams(textDocument, protocolPosition, CompletionContextConversion.ToProtocol(request.Context)),

					parseResponse: (response, _) =>
					{
						IReadOnlyList<CompletionItemPayload> itemPayloads = response?.Items ?? [];

						if (itemPayloads.Count == 0)
							return [];

						ILanguageServerClient? client = context.Client;

						CompletionResolveFactory? resolveFactory =
							client is not null && client.NegotiatedCapabilities.SupportsCompletionResolve
								? (unresolvedItem, itemPayload, priorityRank) =>
									resolveCancellationToken =>
										ResolveCompletionItemAsync(
											client,
											unresolvedItem,
											itemPayload,
											priorityRank,
											filePath: request.DocumentId,
											content: request.DocumentText,
											cancellationToken: resolveCancellationToken
										)
								: null;

						return CompletionResponseParser.Parse(itemPayloads, request.DocumentText, resolveFactory);
					},

					fallbackValue: [],
					offset: request.CaretOffset
				),

				cancellationToken
			)
			.ConfigureAwait(false);
	}

	private async Task<CompletionItem> ResolveCompletionItemAsync(
		ILanguageServerClient client,
		CompletionItem unresolvedItem,
		CompletionItemPayload itemPayload,
		int priorityRank,
		string filePath,
		string content,
		CancellationToken cancellationToken
	)
	{
		// The callback captures the client instance that negotiated resolve support, and the instance
		// never changes; the re-check below still covers a capability reset since the capture.
		if (!client.NegotiatedCapabilities.SupportsCompletionResolve)
			return unresolvedItem;

		try
		{
			CompletionItemPayload? resolvedItem = await requestRunner
				.SendAsync<CompletionItemPayload?>(
					method: LspMethodNames.CompletionResolve,
					parameters: itemPayload,
					fallbackValue: null,
					cancellationToken
				)
				.ConfigureAwait(false);

			if (resolvedItem is not null)
			{
				// The resolved payload's text edits are offset-mapped against the content the completion request
				// captured. If the tracked document has moved on since, those offsets do not describe the current
				// text, so the edit fields are dropped before parsing and only the content fields are adopted, rather
				// than committing text at stale offsets.
				CompletionItemPayload payloadToParse = ContentMatchesTrackedSnapshot(filePath, content)
					? resolvedItem
					: resolvedItem with
					{
						TextEdit = null,
						AdditionalTextEdits = null,
					};

				CompletionItem? parsedItem = CompletionResponseParser.ParseItem(payloadToParse, priorityRank, content);

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
			context.Logger.LogWarning(
				exception,
				"Failed to resolve {DisplayName} completion item '{Label}'; falling back to the unresolved item.",
				context.ProviderDisplayName,
				unresolvedItem.Label
			);
		}

		return unresolvedItem;
	}

	// Reports whether the tracked document still holds the content the completion request captured, so the
	// resolved payload's offset-based edits describe the current text. A document that is not tracked (or
	// that has moved on) does not match.
	private bool ContentMatchesTrackedSnapshot(string filePath, string content) =>
		context.Documents.TryGetContent(filePath, out string? trackedContent) && string.Equals(trackedContent, content, StringComparison.Ordinal);
}
