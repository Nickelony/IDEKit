using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Provider;

public abstract partial class LanguageServerIntelliSenseProviderBase
{
	// Upper bound for following a renamed record while completing its deferred close: each hop completes the close
	// through the record's current path, and a rename beyond this bound leaves the close pending for a later release.
	private const int PendingCloseRenameFollowLimit = 4;

	/// <summary>
	/// Synchronizes the requested document, executes a document-scoped language-server request, and
	/// releases the temporary request reference afterwards.
	/// </summary>
	/// <typeparam name="TResponse">
	/// The expected response payload type. A reference type may arrive as a JSON null and then returns
	/// <paramref name="fallbackValue"/> instead of reaching <paramref name="parseResponse"/>; a value type is
	/// classified through the request outcome, never a null check, so both shapes are supported.
	/// </typeparam>
	/// <typeparam name="TResult">The parsed result type.</typeparam>
	/// <param name="filePath">The absolute local file path of the document.</param>
	/// <param name="documentText">The current document content.</param>
	/// <param name="method">The LSP method name.</param>
	/// <param name="supportsRequest">Reports whether the connected client supports the request.</param>
	/// <param name="buildParameters">Builds the request parameters from the normalized document identifier.</param>
	/// <param name="parseResponse">Parses the response payload.</param>
	/// <param name="fallbackValue">The fallback value returned when the request cannot be issued, the server rejects it, or the response is unusable.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The parsed result, or <paramref name="fallbackValue"/>.</returns>
	/// <remarks>
	/// <para>
	/// The capability gate is evaluated after the transport is ensured but before the document is synchronized, so
	/// a request the connected client does not support neither synchronizes the document nor temporarily tracks it.
	/// The gate cannot run before startup: negotiated capabilities only exist after the initialize handshake. A
	/// response payload that cannot be deserialized as <typeparamref name="TResponse"/> is treated like any other
	/// unusable response and returns the fallback value. The outcome is classified from the request itself, not from
	/// the response value, so a value-type response - for which a null check can never fire - still returns the
	/// fallback value when the request settles with the fallback outcome.
	/// </para>
	/// <para>
	/// A throwing <paramref name="buildParameters"/> or <paramref name="parseResponse"/> delegate propagates
	/// unchanged: those delegates are caller-supplied code rather than framework policy.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/>, <paramref name="documentText"/>, <paramref name="method"/>,
	/// <paramref name="supportsRequest"/>, <paramref name="buildParameters"/>, or <paramref name="parseResponse"/> is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="OperationCanceledException">
	/// <paramref name="cancellationToken"/> is canceled. The temporary request reference is still
	/// released before the exception propagates.
	/// </exception>
	protected async Task<TResult> SendDocumentRequestAsync<TResponse, TResult>(
		string filePath, string documentText,
		string method,
		Func<ILanguageServerClient, bool> supportsRequest,
		Func<TextDocumentIdentifier, object> buildParameters,
		Func<TResponse, TResult> parseResponse,
		TResult fallbackValue,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(documentText);
		ArgumentNullException.ThrowIfNull(method);
		ArgumentNullException.ThrowIfNull(supportsRequest);
		ArgumentNullException.ThrowIfNull(buildParameters);
		ArgumentNullException.ThrowIfNull(parseResponse);

		cancellationToken.ThrowIfCancellationRequested();

		ILanguageServerClient? client = _client;

		if (client is null)
		{
			ReportMissingClientFailure();
			return fallbackValue;
		}

		if (!TryNormalizeDocumentEntryPath(filePath, "Request", out string? normalizedFilePath))
			return fallbackValue;

		// Tracks the temporary request reference this request acquired, so the release below never consumes a
		// reference owned by a concurrent request and still finds the record when it is renamed meanwhile.
		var requestReference = new DocumentRequestReference();

		try
		{
			if (!await TryEnsureStartedForOperationAsync(cancellationToken).ConfigureAwait(false))
				return fallbackValue;

			// Evaluate the capability gate before synchronizing: synchronizing a document for a request the
			// server can never answer would send avoidable open/close traffic for idle documents.
			if (!supportsRequest(client))
				return fallbackValue;

			// Keep post-edit refresh owned by UpdateDocument so request-driven synchronization does not
			// issue an additional refresh for every IntelliSense request.
			if (!(await SynchronizeDocumentAsync(normalizedFilePath, documentText, DocumentSynchronizationMode.Request,
				requestReference, cancellationToken).ConfigureAwait(false)).Succeeded)
			{
				return fallbackValue;
			}

			// Dispatch the request against the normalized document URI and classify the outcome instead of the
			// response value: a non-nullable struct response never compares equal to null, so a null check would
			// silently parse a default value instead of returning the fallback value.
			var textDocument = new TextDocumentIdentifier(LanguageServerPaths.CreateFileUri(normalizedFilePath));

			var (succeeded, response) = await _requestDispatcher.SendOutcomeAsync<TResponse>(method,
				buildParameters(textDocument), cancellationToken).ConfigureAwait(false);

			if (!succeeded || response is null)
				return fallbackValue;

			return parseResponse(response!);
		}
		finally
		{
			// Release only the reference this request actually acquired, and release it by identity: releasing
			// unconditionally can consume a concurrent request's reference, while a path-keyed release would miss
			// the record when it was renamed while the request was in flight.
			if (requestReference.IsAcquired)
				await ReleaseRequestDocumentAsync(requestReference, cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Synchronizes the requested document, executes a position-based language-server request, and
	/// releases the temporary request reference afterwards.
	/// </summary>
	/// <typeparam name="TResponse">The expected response payload type.</typeparam>
	/// <typeparam name="TResult">The parsed result type.</typeparam>
	/// <param name="filePath">The absolute local file path of the document.</param>
	/// <param name="documentText">The current document content.</param>
	/// <param name="position">The zero-based line and character position. Negative values are clamped to zero.</param>
	/// <param name="method">The LSP method name.</param>
	/// <param name="buildParameters">Builds the request parameters from the normalized document identifier and clamped position.</param>
	/// <param name="parseResponse">Parses the response payload.</param>
	/// <param name="fallbackValue">The fallback value returned when the request cannot be issued, the server rejects it, or the response is unusable.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The parsed result, or <paramref name="fallbackValue"/>.</returns>
	/// <remarks>
	/// Position-based requests pass a capability gate that always grants them, because the Abstractions contract
	/// defines completion, hover, definition, and signature help as always attempted; a server that does not
	/// implement one of them rejects the request and the documented fallback value is returned.
	/// The position is clamped here as well as by each request record's public constructor: this is a
	/// <see langword="protected"/> boundary that accepts a caller-supplied <see cref="TextPosition"/>, so a derived
	/// provider that builds the payload itself still cannot put negative coordinates on the wire.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="filePath"/>, <paramref name="documentText"/>, <paramref name="method"/>,
	/// <paramref name="buildParameters"/>, or <paramref name="parseResponse"/> is
	/// <see langword="null"/>.
	/// </exception>
	/// <exception cref="OperationCanceledException">
	/// <paramref name="cancellationToken"/> is canceled. The temporary request reference is still
	/// released before the exception propagates.
	/// </exception>
	protected Task<TResult> SendDocumentPositionRequestAsync<TResponse, TResult>(
		string filePath, string documentText, TextPosition position,
		string method,
		Func<TextDocumentIdentifier, ProtocolPosition, object> buildParameters,
		Func<TResponse, TResult> parseResponse,
		TResult fallbackValue,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(filePath);
		ArgumentNullException.ThrowIfNull(documentText);
		ArgumentNullException.ThrowIfNull(method);
		ArgumentNullException.ThrowIfNull(buildParameters);
		ArgumentNullException.ThrowIfNull(parseResponse);

		ProtocolPosition protocolPosition = ToProtocolPosition(position);

		return SendDocumentRequestAsync<TResponse, TResult>(
			filePath, documentText, method,
			supportsRequest: static _ => true,
			buildParameters: textDocument => buildParameters(textDocument, protocolPosition),
			parseResponse, fallbackValue, cancellationToken);
	}

	/// <summary>
	/// Converts an editor position to a protocol position, clamping negative coordinates to zero so a protected
	/// request seam never forwards an out-of-range position to the server.
	/// </summary>
	/// <param name="position">The editor position to convert.</param>
	/// <returns>The protocol position with non-negative coordinates.</returns>
	private static ProtocolPosition ToProtocolPosition(TextPosition position)
	{
		TextPosition clampedPosition = position.ClampNegativeToZero();
		return new ProtocolPosition(clampedPosition.Line, clampedPosition.Character);
	}

	/// <summary>
	/// Resolves the normalized document path a parse closure needs for same-document filtering or edit targeting.
	/// </summary>
	/// <remarks>
	/// The request pipeline always normalizes the document path first, before it invokes a parse closure, and
	/// returns the request fallback when that normalization fails. The raw-path branch therefore only serves a
	/// parse closure that is invoked outside the pipeline.
	/// </remarks>
	/// <param name="filePath">The caller-supplied document path.</param>
	/// <returns>The normalized path, or the raw path when normalization fails.</returns>
	private static string ResolveDocumentFilePath(string filePath)
		=> LanguageServerPaths.TryNormalizeLocalPath(filePath, out string normalizedFilePath) ? normalizedFilePath : filePath;

	/// <summary>
	/// Ensures the transport is running before an operation enters its per-document scheduler slot, containing the
	/// startup failure modes a slot cannot recover from.
	/// </summary>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns><see langword="true"/> when the transport is ready; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="OperationCanceledException">The caller's cancellation token was canceled.</exception>
	private async Task<bool> TryEnsureStartedForOperationAsync(CancellationToken cancellationToken)
	{
		try
		{
			return await EnsureTransportStartedAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (OperationCanceledException) when (_isDisposed)
		{
			return false;
		}
		catch (OperationCanceledException)
		{
			// An unowned cancellation (neither the caller token nor disposal) is classified like the dispatcher
			// classifies it: the documented fallback value instead of a leaked cancellation.
			return false;
		}
		catch (Exception exception) when (exception is IOException or ObjectDisposedException)
		{
			// A disposal race needs no unavailable transition, and TrySetState ignores one anyway.
			if (!_isDisposed)
				MarkStartupTransportUnavailable();

			return false;
		}
	}

	/// <summary>
	/// Releases one request reference inside the record's per-document scheduler slot and completes a close that was
	/// deferred while the reference was active.
	/// </summary>
	/// <param name="requestReference">The reference bound to the tracked record.</param>
	/// <param name="cancellationToken">A token that can cancel the request; the release still runs when it is canceled.</param>
	private async Task ReleaseRequestDocumentAsync(DocumentRequestReference requestReference, CancellationToken cancellationToken)
	{
		if (_client is null || requestReference.CurrentFilePath is not { } releasePath)
			return;

		try
		{
			IReadOnlyList<DocumentSnapshot> documentsToClose = await _documentScheduler.EnqueuePerDocumentAsync(
				releasePath,
				async token =>
				{
					// Caller cancellation must not skip request-reference cleanup, otherwise a canceled
					// IntelliSense request can leave a request-only tracked document pinned indefinitely.
					_documents.ReleaseRequestByReference(requestReference);

					// A rename that landed before this release ran moved any deferred close to the record's current
					// path; completing it needs that path's own scheduler slot, so the follow-up loop handles it.
					if (requestReference.CurrentFilePath is { } currentPath && LanguageServerPaths.AreLocalPathsEqual(currentPath, releasePath))
						await CompletePendingDocumentCloseAsync(releasePath).ConfigureAwait(false);

					return _documents.TrimIdleDocuments(_options.MaxTrackedIdleDocuments);
				},
				CancellationToken.None).ConfigureAwait(false);

			for (int i = 0; i < documentsToClose.Count; i++)
				await CloseTrimmedDocumentAsync(documentsToClose[i]).ConfigureAwait(false);

			await FollowRenamedDocumentCloseAsync(requestReference, releasePath).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _isDisposed)
		{ }
		catch (Exception exception)
		{
			LogBestEffortFailure("request-document release", releasePath, exception);
		}
	}

	/// <summary>
	/// Follows a record that was renamed while its request reference was being released and completes its deferred
	/// close through the record's current path.
	/// </summary>
	/// <param name="requestReference">The released reference bound to the record.</param>
	/// <param name="releasePath">The document path whose scheduler slot the release already went through.</param>
	/// <remarks>
	/// The follow is bounded by <see cref="PendingCloseRenameFollowLimit"/> hops, so a record renamed more often
	/// than that during one release leaves its close pending. The pending marker is retained, and any later
	/// release, open, close or idle trim for the path completes the deferred close.
	/// </remarks>
	private async Task FollowRenamedDocumentCloseAsync(DocumentRequestReference requestReference, string releasePath)
	{
		for (int i = 0; i < PendingCloseRenameFollowLimit; i++)
		{
			if (requestReference.CurrentFilePath is not { } currentPath
				|| LanguageServerPaths.AreLocalPathsEqual(currentPath, releasePath)
				|| !_pendingDocumentCloses.ContainsKey(currentPath))
			{
				return;
			}

			string closePath = currentPath;

			await _documentScheduler.EnqueuePerDocumentAsync(
				closePath,
				async _ =>
				{
					await CompletePendingDocumentCloseAsync(closePath).ConfigureAwait(false);
					return true;
				},
				CancellationToken.None).ConfigureAwait(false);

			releasePath = closePath;
		}
	}

	/// <summary>
	/// Closes the server copy of one document evicted by idle-document trimming. The close runs through the evicted
	/// document's own scheduler slot and re-checks the tracked state first, so a record that was recreated after the
	/// trim keeps its server copy open.
	/// </summary>
	/// <param name="document">The snapshot evicted by the tracked-document store.</param>
	internal async Task CloseTrimmedDocumentAsync(DocumentSnapshot document)
	{
		if (_client is null || _isDisposed)
			return;

		try
		{
			await _documentScheduler.EnqueuePerDocumentAsync(
				document.FilePath,
				async token =>
				{
					// A concurrent open or update recreated the record after the trim; its own lifecycle owns the
					// server copy now, so an unguarded close here would close a document the client considers
					// open again.
					if (_documents.IsTracked(document.FilePath))
						return false;

					_pendingDocumentCloses.TryRemove(document.FilePath, out _);
					InvokeContainedHook(() => OnTrackedDocumentInvalidated(document.FilePath), "tracked-document invalidation");
					CancelSemanticTokenRequest(document.FilePath);
					CancelDiagnosticRequest(document.FilePath);

					if (!_client.IsReady)
						return false;

					return await SendDocumentSynchronizationNotificationAsync(
						new DocumentSynchronizationRequest(DocumentSynchronizationKind.Close, document),
						CancellationToken.None).ConfigureAwait(false);
				},
				CancellationToken.None).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (_isDisposed)
		{ }
		catch (Exception exception)
		{
			LogBestEffortFailure("trimmed-document close", document.FilePath, exception);
		}
	}

	/// <summary>
	/// Completes a close that was deferred while a temporary request reference kept the document tracked.
	/// The caller must hold the document's scheduler slot.
	/// </summary>
	/// <param name="filePath">The normalized document path whose deferred close should be completed.</param>
	private async Task CompletePendingDocumentCloseAsync(string filePath)
	{
		if (!_pendingDocumentCloses.TryRemove(filePath, out _))
			return;

		DocumentCloseOutcome closeResult = _documents.TryClose(filePath, out DocumentSnapshot? document);

		if (closeResult == DocumentCloseOutcome.BusyWithRequests)
		{
			// Another request reference is still active; keep the close pending for its release.
			_pendingDocumentCloses[filePath] = 0;
			return;
		}

		if (closeResult != DocumentCloseOutcome.Closed || document is null)
			return;

		InvokeContainedHook(() => OnTrackedDocumentInvalidated(filePath), "tracked-document invalidation");
		CancelSemanticTokenRequest(filePath);
		CancelDiagnosticRequest(filePath);

		// The startup-succeeded flag is not consulted here either (see CloseDocumentAsync in the documents slice): a
		// deferred close must still run while a restart replay owns the server session.
		if (_client is null || !_client.IsReady)
			return;

		try
		{
			await SendDocumentSynchronizationNotificationAsync(
				new DocumentSynchronizationRequest(DocumentSynchronizationKind.Close, document),
				CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			LogBestEffortFailure("deferred document close", filePath, exception);
		}
	}
}
