using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Creates the client hooks that hand the server messages of a provider-created client to the host as shared models.</summary>
/// <remarks>
/// The client hooks speak the protocol's wire types, and the host hooks of the provider speak the shared models, so the factory converts both ways
/// and leaves a client hook unset for each host hook that is unset. That keeps the client's default for the message, and leaves off the capability
/// that the hook would enable.
/// </remarks>
internal static class ClientHooksFactory
{
	/// <summary>Creates the client hooks for the host hooks of a provider.</summary>
	/// <param name="hooks">The host hooks.</param>
	/// <param name="pathComparison">The comparison that decides whether two edited paths are one document.</param>
	/// <param name="logger">The logger that records an edit that the shared model cannot represent.</param>
	internal static LanguageServerClientHooks Create(
		LanguageServerFeatureProviderHooks hooks,
		LocalPathComparisonPolicy pathComparison,
		ILogger logger
	)
	{
		return new LanguageServerClientHooks
		{
			ShowMessage = hooks.ShowMessage is { } showMessage ? parameters => ShowMessage(showMessage, parameters) : null,

			ShowMessageRequest = hooks.ShowMessageRequest is { } showMessageRequest
				? (parameters, cancellationToken) => ShowMessageRequestAsync(showMessageRequest, parameters, cancellationToken)
				: null,

			Progress = hooks.Progress is { } progress ? parameters => progress(ToProgress(parameters)) : null,

			ApplyWorkspaceEdit = hooks.ApplyWorkspaceEdit is { } applyWorkspaceEdit
				? (parameters, cancellationToken) =>
					ApplyWorkspaceEditAsync(hook: applyWorkspaceEdit, parameters, pathComparison, logger, cancellationToken)
				: null,
		};
	}

	private static void ShowMessage(Action<WindowMessage> hook, WindowMessageParams parameters)
	{
		if (!string.IsNullOrWhiteSpace(parameters.Message))
			hook(new WindowMessage(WindowMessageKindConversion.FromProtocol(parameters.Type), parameters.Message));
	}

	private static async Task<MessageActionItemPayload?> ShowMessageRequestAsync(
		Func<WindowMessage, CancellationToken, Task<string?>> hook,
		ShowMessageRequestParams parameters,
		CancellationToken cancellationToken
	)
	{
		if (string.IsNullOrWhiteSpace(parameters.Message))
			return null;

		string[] actions =
		[
			.. (parameters.Actions ?? [])
				.Select(static action => action.Title)
				.OfType<string>()
				.Where(static title => !string.IsNullOrWhiteSpace(title)),
		];

		var message = new WindowMessage(WindowMessageKindConversion.FromProtocol(parameters.Type), parameters.Message, actions);

		string? chosen = await hook(message, cancellationToken).ConfigureAwait(false);

		// The client rejects a title that names none of the offered actions, so this conversion only wraps the title.
		return chosen is null ? null : new MessageActionItemPayload(chosen);
	}

	private static async Task<ApplyWorkspaceEditResponse> ApplyWorkspaceEditAsync(
		Func<WorkspaceEdit, CancellationToken, Task<bool>> hook,
		ApplyWorkspaceEditParams parameters,
		LocalPathComparisonPolicy pathComparison,
		ILogger logger,
		CancellationToken cancellationToken
	)
	{
		WorkspaceEdit? edit = WorkspaceEditParser.Parse(parameters.Edit, pathComparison, logger);

		if (edit is null)
		{
			return new ApplyWorkspaceEditResponse(
				Applied: false,
				"The edit changes no local file text, or it contains a file operation or a document that is not a local file."
			);
		}

		bool applied = await hook(edit, cancellationToken).ConfigureAwait(false);

		return applied
			? new ApplyWorkspaceEditResponse(Applied: true)
			: new ApplyWorkspaceEditResponse(Applied: false, "The host did not apply the edit.");
	}

	private static WorkDoneProgress ToProgress(ProgressParams parameters)
	{
		WorkDoneProgressPayload value = parameters.Value;

		WorkDoneProgressKind kind = value.Kind switch
		{
			"begin" => WorkDoneProgressKind.Begin,
			"end" => WorkDoneProgressKind.End,
			_ => WorkDoneProgressKind.Report,
		};

		return new WorkDoneProgress(Token: parameters.Token, kind, Title: value.Title, Message: value.Message, Percentage: value.Percentage);
	}
}
