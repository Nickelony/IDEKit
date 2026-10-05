using Nickelony.IDEKit.Core.Text;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Runs a document-scoped request through startup, the capability gate, document synchronization and the request runner.</summary>
/// <param name="context">The client and the helpers every provider collaborator shares.</param>
/// <param name="requestRunner">The runner that sends each request under the timeout and restart policy.</param>
/// <param name="lifecycle">The coordinator that starts the connection and reports a missing client.</param>
/// <param name="synchronizer">The synchronizer that mirrors the requested document to the server.</param>
/// <param name="releaseRunner">The runner that releases the request's lease and completes the close it deferred.</param>
internal sealed class DocumentRequestPipeline(
	ProviderContext context,
	RequestRunner requestRunner,
	ProviderLifecycleCoordinator lifecycle,
	DocumentSynchronizer synchronizer,
	DocumentRequestReleaseRunner releaseRunner
) : IDocumentRequestPipeline
{
	/// <inheritdoc/>
	public async Task<TResult> SendAsync<TResponse, TResult>(
		string filePath,
		string documentText,
		DocumentRequestDescriptor<TResponse, TResult> request,
		CancellationToken cancellationToken
	)
	{
		cancellationToken.ThrowIfCancellationRequested();

		ILanguageServerClient? client = context.Client;

		if (client is null)
		{
			lifecycle.ReportMissingClientFailure();
			return request.FallbackValue;
		}

		if (!context.TryNormalizeDocumentEntryPath(filePath, "Request", out string? normalizedFilePath))
			return request.FallbackValue;

		// Tracks the temporary request lease this request acquired, so the release below never consumes a
		// lease owned by a concurrent request and still finds the record when it is renamed meanwhile.
		var requestLease = new DocumentRequestLease();

		try
		{
			if (!await lifecycle.TryEnsureStartedForOperationAsync(cancellationToken).ConfigureAwait(false))
				return request.FallbackValue;

			// Evaluate the capability gate before synchronizing: synchronizing a document for a request the
			// server can never answer would send avoidable open/close traffic for idle documents. A null gate
			// means the request is always attempted; hover, definition, completion and signature help have no gate.
			if (request.SupportsRequest is { } supportsRequest && !supportsRequest(client))
				return request.FallbackValue;

			// Keep post-edit refresh owned by UpdateDocument so request-driven synchronization does not
			// issue an additional refresh for every language-feature request.
			if (
				!await synchronizer
					.SynchronizeForRequestAsync(
						filePath: normalizedFilePath,
						content: documentText,
						requestLease,
						cancellationToken,
						documentId: filePath
					)
					.ConfigureAwait(false)
			)
			{
				return request.FallbackValue;
			}

			// Dispatch the request against the normalized document URI and classify the outcome instead of the
			// response value: a non-nullable struct response never compares equal to null, so a null check would
			// silently parse a default value instead of returning the fallback value.
			var document = new RequestedDocument(normalizedFilePath, documentText);
			var textDocument = new TextDocumentIdentifier(DocumentUriConversion.CreateFileUri(normalizedFilePath));

			// A document-scoped request has no offset and sends the default position, which its parameter builder ignores.
			TextPosition position = request.Offset is { } offset ? document.LineMap.GetPosition(offset) : default;

			(bool succeeded, TResponse? response) = await requestRunner
				.SendOutcomeAsync<TResponse>(
					request.Method,
					request.BuildParameters(textDocument, ProtocolPositionConversion.ToProtocolPosition(position)),
					cancellationToken
				)
				.ConfigureAwait(false);

			if (!succeeded || response is null)
				return request.FallbackValue;

			return request.ParseResponse(response, document);
		}
		finally
		{
			// Release only the lease this request actually acquired, and release it by identity: releasing
			// unconditionally can consume a concurrent request's lease, while a path-keyed release would miss
			// the record when its path is renamed while the request was in flight.
			if (requestLease.IsAcquired)
				await releaseRunner.ReleaseAsync(requestLease, cancellationToken).ConfigureAwait(false);
		}
	}
}
