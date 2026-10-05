using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

internal static class CodeActionResponseParser
{
	/// <summary>Parses code actions from an LSP code-action response into the shared action model.</summary>
	/// <param name="response">The code-action response payload, or <see langword="null"/> when unavailable.</param>
	/// <param name="request">The request that asked for the actions, whose document each action is offered for.</param>
	/// <param name="capabilities">What the server negotiated, which decides whether an action without an edit can be resolved or run.</param>
	/// <param name="pathComparison">The comparison that decides whether two edited paths are one document.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for no logging.</param>
	/// <returns>The parsed actions in response order, or an empty list when no usable action is present.</returns>
	/// <remarks>
	/// <para>
	/// One action's edit is independent of the other actions, so an edit that cannot be represented (an unsupported resource operation
	/// or an unresolvable target URI) drops that action only instead of failing the whole response. An action is kept when it carries an edit,
	/// a command that the server executes, or resolve data for a server that resolves actions; otherwise, it does nothing and is skipped.
	/// An action whose command the server does not execute, such as a command that the client is to interpret, is skipped whole.
	/// </para>
	/// <para>
	/// The shared action model carries no disabled state or per-action diagnostics, so a disabled action is surfaced as enabled
	/// without its explanation.
	/// </para>
	/// </remarks>
	internal static IReadOnlyList<CodeAction> Parse(
		CodeActionResponse? response,
		CodeActionRequest request,
		NegotiatedServerCapabilities capabilities,
		LocalPathComparisonPolicy pathComparison,
		ILogger? logger = null
	)
	{
		if (response is null || response.CodeActions.Count == 0)
			return [];

		var result = new List<CodeAction>(response.CodeActions.Count);

		for (int i = 0; i < response.CodeActions.Count; i++)
		{
			CodeActionPayload payload = response.CodeActions[i];
			var offer = new CodeActionOffer(payload, request.DocumentId, request.DocumentText);

			if (ParseAction(payload, offer, capabilities, pathComparison, logger) is not { } action)
				continue;

			bool canBeResolved = payload.Data is not null && capabilities.SupportsCodeActionResolve;

			if (action.Edit is not null || action.Command is not null || canBeResolved)
				result.Add(action);
		}

		return result;
	}

	/// <summary>Parses the answer to a <c>codeAction/resolve</c> request into the shared action model.</summary>
	/// <param name="resolved">The resolved action payload.</param>
	/// <param name="offer">The offer of the action that was resolved, which the result keeps.</param>
	/// <param name="capabilities">What the server negotiated.</param>
	/// <param name="pathComparison">The comparison that decides whether two edited paths are one document.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for no logging.</param>
	/// <returns>
	/// The resolved action, or <see langword="null"/> when the answer carries neither an edit nor a command that the server executes.
	/// </returns>
	/// <remarks>
	/// The server answers with the action it was asked about, so a title, a kind or a preferred flag that the answer omits is the offered one.
	/// </remarks>
	internal static CodeAction? ParseResolved(
		CodeActionPayload resolved,
		CodeActionOffer offer,
		NegotiatedServerCapabilities capabilities,
		LocalPathComparisonPolicy pathComparison,
		ILogger? logger = null
	)
	{
		CodeActionPayload payload = resolved with
		{
			Title = resolved.Title ?? offer.Payload.Title,
			Kind = resolved.Kind ?? offer.Payload.Kind,
			IsPreferred = resolved.IsPreferred ?? offer.Payload.IsPreferred,
		};

		CodeAction? action = ParseAction(payload, offer, capabilities, pathComparison, logger);

		return action is { Edit: not null } or { Command: not null } ? action : null;
	}

	private static CodeAction? ParseAction(
		CodeActionPayload payload,
		CodeActionOffer offer,
		NegotiatedServerCapabilities capabilities,
		LocalPathComparisonPolicy pathComparison,
		ILogger? logger
	)
	{
		if (string.IsNullOrWhiteSpace(payload.Title))
			return null;

		WorkspaceEdit? edit = null;

		if (payload.Edit is { } editPayload)
		{
			edit = WorkspaceEditParser.Parse(editPayload, pathComparison, logger);

			if (edit is null)
			{
				logger?.LogWarning("Ignoring code action '{Title}' because its workspace edit could not be represented.", payload.Title);
				return null;
			}
		}

		CodeActionCommand? command = null;

		if (payload.Command is { } commandPayload)
		{
			if (commandPayload.Command is not { } name || !capabilities.ExecuteCommands.Contains(name, StringComparer.Ordinal))
			{
				logger?.LogDebug(
					"Ignoring code action '{Title}' because the server does not execute its command '{Command}'.",
					payload.Title,
					commandPayload.Command
				);

				return null;
			}

			command = new CodeActionCommand(
				string.IsNullOrWhiteSpace(commandPayload.Title) ? payload.Title : commandPayload.Title,
				name,
				commandPayload.Arguments is { } arguments ? [.. arguments.Select(static argument => (object?)argument)] : null
			);
		}

		return new CodeAction(title: payload.Title, edit, kind: payload.Kind, isPreferred: payload.IsPreferred == true, data: offer, command);
	}
}
