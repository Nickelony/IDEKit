namespace Nickelony.LanguageServer.Provider;

internal static partial class ResponseParser
{
	/// <summary>
	/// Parses code actions from an LSP code-action response into the shared action model.
	/// </summary>
	/// <remarks>
	/// One action's edit is independent of the other actions, so an edit that cannot be represented
	/// (an unsupported resource operation or an unresolvable target URI) drops that action only
	/// instead of failing the whole response. Entries without a usable title or edit are skipped.
	/// The shared action model carries no disabled state or per-action diagnostics, so a disabled
	/// action is surfaced as enabled without its explanation.
	/// </remarks>
	/// <param name="response">The code-action response payload, or <see langword="null"/> when unavailable.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for no logging.</param>
	/// <returns>The parsed actions in response order, or an empty list when no usable action is present.</returns>
	internal static IReadOnlyList<TextCodeAction> ParseCodeActions(CodeActionsResponse? response, ILogger? logger = null)
	{
		if (response is null || response.CodeActions.Count == 0)
			return [];

		var result = new List<TextCodeAction>(response.CodeActions.Count);

		for (int i = 0; i < response.CodeActions.Count; i++)
		{
			CodeActionPayload payload = response.CodeActions[i];

			if (string.IsNullOrWhiteSpace(payload.Title) || payload.Edit is not { } editPayload)
				continue;

			TextWorkspaceEdit? edit = ParseWorkspaceEdit(editPayload, logger);

			if (edit is null)
			{
				logger?.LogWarning(
					"Ignoring code action '{Title}' because its workspace edit could not be represented.",
					payload.Title);

				continue;
			}

			result.Add(new TextCodeAction(payload.Title, edit, payload.Kind, payload.IsPreferred == true));
		}

		return result;
	}
}
