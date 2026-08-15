namespace Nickelony.LanguageServer.Provider;

internal static partial class ResponseParser
{
	/// <summary>
	/// Parses a workspace edit from an LSP rename or code-action response.
	/// </summary>
	/// <remarks>
	/// The extraction rules - <c>documentChanges</c> precedence over <c>changes</c>, resource-operation
	/// fail-closed handling, and URI-to-local-path resolution - are protocol-level and live in the provider
	/// framework's <see cref="WorkspaceEditConversion"/>. This method is the parse closure that binds them
	/// to the provider response pipeline.
	/// </remarks>
	/// <param name="response">The workspace edit response payload, or <see langword="null"/> when unavailable.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for no logging.</param>
	/// <returns>
	/// The parsed workspace edit, or <see langword="null"/> when no valid edits are present or the response contains an
	/// unsupported resource operation.
	/// </returns>
	internal static TextWorkspaceEdit? ParseWorkspaceEdit(WorkspaceEditResponse? response, ILogger? logger = null)
		=> WorkspaceEditConversion.Parse(response, logger);
}
