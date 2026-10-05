using System.Text.Json;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Answers code-action requests with the language server's <c>textDocument/codeAction</c> response, resolves a chosen
/// action through <c>codeAction/resolve</c>, and runs its command through <c>workspace/executeCommand</c>.
/// </summary>
/// <param name="pipeline">The pipeline that sends the document-scoped request.</param>
/// <param name="context">The client, the tracked documents, the path comparison and the logger the requests need.</param>
/// <param name="requestRunner">The runner that sends the resolve and command requests, which are not scoped to a document.</param>
internal sealed class CodeActionProvider(IDocumentRequestPipeline pipeline, ProviderContext context, RequestRunner requestRunner)
	: ICodeActionProvider,
		ICodeActionResolver,
		ICodeActionCommandRunner
{
	// Host-built command arguments reach the server with the wire casing the client uses for every payload it serializes itself.
	private static readonly JsonSerializerOptions s_argumentOptions = LanguageServerClient.CreateProtocolSerializerOptions();

	/// <inheritdoc/>
	public Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(CodeActionRequest request, CancellationToken cancellationToken = default)
	{
		return pipeline.SendAsync(
			filePath: request.DocumentId,
			documentText: request.DocumentText,

			request: new DocumentRequestDescriptor<CodeActionResponse?, IReadOnlyList<CodeAction>>(
				method: LspMethodNames.CodeAction,

				buildParameters: (textDocument, _) =>
				{
					var lineMap = TextLineMap.Build(request.DocumentText);

					TextPositionRange range = lineMap.GetPositionRange(request.Range);

					(IReadOnlyList<DiagnosticEntry> diagnostics, string? sourceContent) = context.Documents.GetDiagnosticsWithSourceContent(
						request.DocumentId
					);

					return new CodeActionParams(
						textDocument,
						ProtocolRangeConversion.ToProtocolRange(range),

						// The protocol context carries the diagnostics currently reported for the requested
						// range, because a server derives its quick-fix family from them, so an empty context
						// would suppress it. The build runs inside the parameter factory, after the request gates.
						CodeActionContextConversion.ToProtocol(request, lineMap, diagnostics, sourceContent)
					);
				},

				parseResponse: (response, document) =>
					RequestedDocumentNormalizer.Normalize(
						actions: CodeActionResponseParser.Parse(
							response,
							request,
							capabilities: context.Client?.NegotiatedCapabilities ?? NegotiatedServerCapabilities.None,
							pathComparison: context.Options.PathComparison,
							logger: context.Logger
						),

						requestDocumentId: request.DocumentId,
						requestFilePath: document.NormalizedFilePath,
						pathComparison: context.Options.PathComparison
					),

				fallbackValue: [],
				supportsRequest: static client => client.NegotiatedCapabilities.SupportsCodeActions
			),

			cancellationToken
		);
	}

	/// <inheritdoc/>
	public async Task<CodeAction> ResolveAsync(CodeAction action, CancellationToken cancellationToken = default)
	{
		if (action.Data is not CodeActionOffer offer || context.Client is not { } client || !client.NegotiatedCapabilities.SupportsCodeActionResolve)
			return action;

		// The server resolves the action against the text it holds, so an action offered for other text is refused instead of resolved against it.
		if (!DocumentHolds(offer))
		{
			context.Logger.LogDebug(
				"Not resolving code action '{Title}' because '{DocumentId}' changed since the action was offered.",
				action.Title,
				offer.DocumentId
			);

			return action;
		}

		try
		{
			CodeActionPayload? resolved = await requestRunner
				.SendAsync<CodeActionPayload?>(
					method: LspMethodNames.CodeActionResolve,
					parameters: offer.Payload,
					fallbackValue: null,
					cancellationToken
				)
				.ConfigureAwait(false);

			if (resolved is null)
				return action;

			// The edit of the answer addresses the offered text, and the document can change while the server computes it.
			if (!DocumentHolds(offer))
			{
				context.Logger.LogDebug(
					"Discarding the resolution of code action '{Title}' because '{DocumentId}' changed while the server computed it.",
					action.Title,
					offer.DocumentId
				);

				return action;
			}

			CodeAction? resolvedAction = CodeActionResponseParser.ParseResolved(
				resolved,
				offer,
				capabilities: client.NegotiatedCapabilities,
				pathComparison: context.Options.PathComparison,
				logger: context.Logger
			);

			if (resolvedAction is null)
				return action;

			// An offer whose document cannot be normalized names no requested document, so the resolved action stays as the server described it.
			return LocalPath.TryNormalize(offer.DocumentId, out string offerFilePath)
				? RequestedDocumentNormalizer.Normalize(
					actions: [resolvedAction],
					requestDocumentId: offer.DocumentId,
					requestFilePath: offerFilePath,
					pathComparison: context.Options.PathComparison
				)[0]
				: resolvedAction;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			context.Logger.LogWarning(
				exception,
				"Failed to resolve {DisplayName} code action '{Title}'; falling back to the unresolved action.",
				context.ProviderDisplayName,
				action.Title
			);

			return action;
		}
	}

	/// <inheritdoc/>
	public async Task<bool> TryRunAsync(CodeActionCommand command, CancellationToken cancellationToken = default)
	{
		if (context.Client is not { } client || !client.NegotiatedCapabilities.ExecuteCommands.Contains(command.Name, StringComparer.Ordinal))
		{
			context.Logger.LogDebug(
				"Not running command '{Command}' because the {DisplayName} server does not execute it.",
				command.Name,
				context.ProviderDisplayName
			);

			return false;
		}

		// The command can change state on the server, so the runner sends it once: no timeout, no restart count and no retry across a connection boundary.
		(bool succeeded, _) = await requestRunner
			.SendOnceAsync<JsonElement?>(
				LspMethodNames.ExecuteCommand,
				new ExecuteCommandParams(command.Name, ToElements(command.Arguments)),
				cancellationToken
			)
			.ConfigureAwait(false);

		return succeeded;
	}

	// The server resolves against the text it holds, and the action addresses the text it was offered for.
	private bool DocumentHolds(CodeActionOffer offer)
	{
		return context.Documents.TryGetContent(offer.DocumentId, out string? trackedContent)
			&& string.Equals(trackedContent, offer.DocumentText, StringComparison.Ordinal);
	}

	// The arguments of an action this provider parsed are the server's own elements; an argument that a host built is serialized with the protocol options.
	private static List<JsonElement>? ToElements(IReadOnlyList<object?> arguments)
	{
		return arguments.Count == 0
			? null
			:
			[
				.. arguments.Select(static argument =>
					argument is JsonElement element ? element : JsonSerializer.SerializeToElement(argument, s_argumentOptions)
				),
			];
	}
}
