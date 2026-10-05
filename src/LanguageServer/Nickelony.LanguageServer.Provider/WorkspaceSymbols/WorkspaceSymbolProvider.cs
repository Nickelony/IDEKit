using Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Answers workspace-symbol requests with the language server's <c>workspace/symbol</c> response, and resolves the range of a listed symbol
/// through <c>workspaceSymbol/resolve</c>.
/// </summary>
/// <param name="context">The client, the logger and the provider name the requests need.</param>
/// <param name="requestRunner">The runner that sends the requests, which are not scoped to a document.</param>
/// <param name="lifecycle">The coordinator that starts the connection and reports a missing client.</param>
/// <remarks>
/// A search starts a stopped server, as a request for a document does, and answers once the server has initialized. The start does not take the token
/// of the search, because the next keystroke cancels that token and a server that needs seconds to start would never finish; the searches that arrive
/// meanwhile share the one start. The wait is bounded by the request timeout, and a search that ends on the bound answers an empty list
/// while the start goes on. The request is sent without a partial-result token, so the server answers the whole list in its response.
/// </remarks>
internal sealed class WorkspaceSymbolProvider(ProviderContext context, RequestRunner requestRunner, ProviderLifecycleCoordinator lifecycle)
	: IWorkspaceSymbolProvider,
		IWorkspaceSymbolResolver
{
	/// <inheritdoc/>
	public async Task<IReadOnlyList<WorkspaceSymbol>> GetWorkspaceSymbolsAsync(
		WorkspaceSymbolRequest request,
		CancellationToken cancellationToken = default
	)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (context.Client is not { } client)
		{
			lifecycle.ReportMissingClientFailure();
			return [];
		}

		if (!await EnsureStartedAsync(cancellationToken).ConfigureAwait(false) || !client.NegotiatedCapabilities.SupportsWorkspaceSymbols)
			return [];

		// The generation that is active when the request goes out minted the data of the answer, even if the request is retried on a newer one.
		long connectionGeneration = client.ConnectionGeneration;

		(bool succeeded, WorkspaceSymbolResponse? response) = await requestRunner
			.SendOutcomeAsync<WorkspaceSymbolResponse?>(LspMethodNames.WorkspaceSymbol, new WorkspaceSymbolParams(request.Query), cancellationToken)
			.ConfigureAwait(false);

		return succeeded ? WorkspaceSymbolResponseParser.Parse(response, owner: this, connectionGeneration, logger: context.Logger) : [];
	}

	/// <inheritdoc/>
	public async Task<WorkspaceSymbol> ResolveAsync(WorkspaceSymbol symbol, CancellationToken cancellationToken = default)
	{
		// The data of a symbol means something to the server that listed it, on the connection that listed it, and to no other.
		if (
			symbol.Location.Range is not null
			|| symbol.Data is not WorkspaceSymbolOffer offer
			|| !ReferenceEquals(offer.Owner, this)
			|| context.Client is not { } client
			|| !client.NegotiatedCapabilities.SupportsWorkspaceSymbolResolve
			|| offer.ConnectionGeneration != client.ConnectionGeneration
		)
		{
			return symbol;
		}

		try
		{
			(bool succeeded, WorkspaceSymbolPayload? resolved) = await requestRunner
				.SendOutcomeAsync<WorkspaceSymbolPayload?>(LspMethodNames.WorkspaceSymbolResolve, offer.Payload, cancellationToken)
				.ConfigureAwait(false);

			// A connection that changed while the server computed the answer, or that the request was retried on, never saw the data it was minted by.
			if (!succeeded || resolved is null || offer.ConnectionGeneration != client.ConnectionGeneration)
				return symbol;

			// An answer without a range gains the host nothing, so the symbol the user chose stays as it was.
			return WorkspaceSymbolResponseParser.ParseResolved(resolved, offer, context.Logger) is { Location.Range: not null } resolvedSymbol
				? resolvedSymbol
				: symbol;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			context.Logger.LogWarning(
				exception,
				"Failed to resolve {DisplayName} workspace symbol '{Name}'; falling back to the unresolved symbol.",
				context.ProviderDisplayName,
				symbol.Name
			);

			return symbol;
		}
	}

	private async Task<bool> EnsureStartedAsync(CancellationToken cancellationToken)
	{
		Task<bool> start = lifecycle.TryEnsureStartedForOperationAsync(CancellationToken.None);

		try
		{
			return await start.WaitAsync(context.Options.RequestTimeout, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception)
			when (exception is TimeoutException || (exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
		{
			// The start goes on without this search, and its failure is logged instead of surfacing as an unobserved task exception.
			context.ObserveBackgroundTask(start, "workspace symbol search startup");

			if (exception is not TimeoutException)
				throw;

			context.Logger.LogDebug(
				"The {DisplayName} server did not initialize within {Timeout} for a workspace symbol search; answering an empty list.",
				context.ProviderDisplayName,
				context.Options.RequestTimeout
			);

			return false;
		}
	}
}
