using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Rename;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols;

namespace Nickelony.LanguageServer.Provider;

public sealed partial class LanguageServerFeatureProvider
	: ICompletionTriggerCharacters,
		ISignatureHelpTriggerCharacters,
		ICodeActionResolver,
		ICodeActionCommandRunner,
		IWorkspaceSymbolResolver
{
	/// <inheritdoc/>
	/// <remarks>
	/// The list is the negotiated <see cref="LanguageServerFeatureProviderCapabilities.CompletionTriggerCharacters"/>, so it is empty
	/// while the provider is not available.
	/// </remarks>
	IReadOnlyList<string> ICompletionTriggerCharacters.TriggerCharacters => Capabilities.CompletionTriggerCharacters;

	/// <inheritdoc/>
	/// <remarks>
	/// The list is the negotiated <see cref="LanguageServerFeatureProviderCapabilities.SignatureHelpTriggerCharacters"/>, so it is empty
	/// while the provider is not available.
	/// </remarks>
	IReadOnlyList<string> ISignatureHelpTriggerCharacters.TriggerCharacters => Capabilities.SignatureHelpTriggerCharacters;

	/// <inheritdoc/>
	/// <remarks>
	/// The list is the negotiated <see cref="LanguageServerFeatureProviderCapabilities.SignatureHelpRetriggerCharacters"/>, so it is empty
	/// while the provider is not available.
	/// </remarks>
	IReadOnlyList<string> ISignatureHelpTriggerCharacters.RetriggerCharacters => Capabilities.SignatureHelpRetriggerCharacters;

	/// <inheritdoc/>
	/// <remarks>
	/// Every call sends a fresh <c>textDocument/completion</c> request. The server's <c>isIncomplete</c> flag is not acted upon because the provider
	/// keeps no completion-list cache: each refresh (keystroke or manual trigger) already re-queries the server, which satisfies the protocol
	/// requirement to re-fetch incomplete lists.
	/// </remarks>
	public Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(CompletionRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		return Composition.Completion.GetCompletionItemsAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<HoverInformation?> GetHoverAsync(HoverRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		return Composition.Hover.GetHoverAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// A language server resolves a definition by position, so the provider uses <see cref="DefinitionRequest.SymbolOffset"/> and ignores
	/// the symbol name and the discriminator. A request without a symbol offset has nothing to send and resolves to <see langword="null"/>.
	/// The returned location always names its document, including a definition inside the requested document, so the navigation helpers hand
	/// every result to the host's cross-file callback instead of moving the caret themselves.
	/// </remarks>
	public Task<DefinitionLocation?> GetDefinitionAsync(DefinitionRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		return Composition.Definition.GetDefinitionAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<IReadOnlyList<ReferenceLocation>> GetReferencesAsync(ReferenceRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		return Composition.References.GetReferencesAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<WorkspaceEdit?> GetRenameEditsAsync(RenameRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		return Composition.Rename.GetRenameEditsAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<WorkspaceEdit?> GetFormattingEditsAsync(FormattingRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		return Composition.Formatting.GetFormattingEditsAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<SignatureHelpInformation?> GetSignatureHelpAsync(SignatureHelpRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		return Composition.SignatureHelp.GetSignatureHelpAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The provider narrows the server's outline with <see cref="DocumentSymbolFilter.FilterByName"/> after the response arrives,
	/// because the protocol request carries no filter.
	/// </remarks>
	public Task<IReadOnlyList<DocumentSymbol>> GetDocumentSymbolsAsync(DocumentSymbolRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		return Composition.DocumentSymbols.GetDocumentSymbolsAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// <para>
	/// The provider sends <c>workspace/symbol</c> to its own server when the server
	/// advertises the request. A search starts a stopped server, as a request for a document does, and answers once the server has initialized:
	/// the wait is bounded by <see cref="LanguageServerFeatureProviderOptions.RequestTimeout"/> and an empty
	/// list ends it, while the start goes on. The start does not take <paramref name="cancellationToken"/>, so a newer search that cancels the older
	/// one does not cancel the start. The provider sends no partial-result token, so the server answers the whole list, and it drops a symbol
	/// whose URI is no local file. The server filters by the query.
	/// </para>
	/// <para>
	/// A host that searches several servers asks each provider and merges the answers, because one provider serves one server.
	/// </para>
	/// </remarks>
	public Task<IReadOnlyList<WorkspaceSymbol>> GetWorkspaceSymbolsAsync(
		WorkspaceSymbolRequest request,
		CancellationToken cancellationToken = default
	)
	{
		ArgumentNullException.ThrowIfNull(request);
		return Composition.WorkspaceSymbols.GetWorkspaceSymbolsAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The provider sends <c>workspaceSymbol/resolve</c> with the symbol as the server listed it, and only for a symbol
	/// without a range, when the server advertises the request. A symbol that this provider did not list, a symbol that an earlier connection listed,
	/// a resolution that fails and an answer without a range return the symbol unchanged, and the first two send no request.
	/// </remarks>
	Task<WorkspaceSymbol> IWorkspaceSymbolResolver.ResolveAsync(WorkspaceSymbol symbol, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(symbol);
		return Composition.WorkspaceSymbols.ResolveAsync(symbol, cancellationToken);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// <para>
	/// The protocol context carries the diagnostics of <see cref="CodeActionRequest.Diagnostics"/>
	/// when the request names them, and the kinds of <see cref="CodeActionRequest.Only"/>. Otherwise, the provider reconstructs the diagnostics
	/// from the ones it has cached for the requested range, and only when the cached snapshot matches the request's <c>DocumentText</c> exactly.
	/// A diagnostic whose range touches the requested range counts, so a caret at either end of a diagnostic includes it. Because a server derives
	/// its quick-fix family from that context, quick fixes become available once diagnostics for the request's exact content have been published,
	/// and an empty context is sent when they have not.
	/// </para>
	/// <para>
	/// An action is kept when it carries an edit, a command that the server lists in its <c>executeCommandProvider</c>,
	/// or resolve data for a server that resolves actions. The host resolves the last kind through <see cref="ICodeActionResolver"/> and runs
	/// a command through <see cref="ICodeActionCommandRunner"/>. An action with a command the server does not execute is omitted, because the server
	/// expects the client to interpret that command.
	/// </para>
	/// </remarks>
	public Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(CodeActionRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		return Composition.CodeActions.GetCodeActionsAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The provider sends <c>codeAction/resolve</c> with the action as the server offered it, and only when the server
	/// advertises the request and the document still holds the text the action was offered for. It discards the answer when the document changed
	/// while the server computed it. An action that this provider did not offer, or whose resolution fails, comes back unchanged.
	/// </remarks>
	Task<CodeAction> ICodeActionResolver.ResolveAsync(CodeAction action, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(action);
		return Composition.CodeActions.ResolveAsync(action, cancellationToken);
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The provider sends <c>workspace/executeCommand</c> for a command
	/// that the server lists in its <c>executeCommandProvider</c>. The server may ask the host to apply an edit while it runs the command,
	/// which reaches the host through <see cref="LanguageServerFeatureProviderHooks.ApplyWorkspaceEdit"/>. The provider sends
	/// the command once. The request has no timeout, so a command that runs long is not abandoned, and a connection that ends before the server
	/// answers is not followed by a second send, because the server may already have run the command; the call then returns <see langword="false"/>.
	/// </remarks>
	Task<bool> ICodeActionCommandRunner.TryRunAsync(CodeActionCommand command, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(command);
		return Composition.CodeActions.TryRunAsync(command, cancellationToken);
	}
}
