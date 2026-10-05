using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Sends the open, change, or close notification that a document-synchronization directive stands for.</summary>
/// <remarks>
/// A session that negotiated <see cref="TextDocumentSyncKind.None"/> still receives the open and the close: the server holds the document so requests
/// that do not depend on incremental synchronization can be served against it, and only the change is skipped because it cannot be expressed
/// without a synchronization mode. The first skipped change of a connection generation is logged at warning level and the later ones at debug level,
/// so a session without a mode does not emit one warning per keystroke.
/// </remarks>
internal sealed class DocumentNotificationSender
{
	private readonly ProviderContext _context;
	private readonly ILanguageServerClient? _client;
	private readonly Func<string> _languageId;

	// The connection generation whose first no-synchronization-mode change skip was logged at warning level; later
	// skips on the same generation drop to debug so a per-keystroke warning is not emitted.
	private long _changeSkipWarningConnectionGeneration = long.MinValue;

	/// <param name="context">The client, the logger and the display name the notifications and their log text use.</param>
	/// <param name="languageId">Returns the language identifier sent as the <c>languageId</c> of opened documents.</param>
	internal DocumentNotificationSender(ProviderContext context, Func<string> languageId)
	{
		_context = context;
		_client = context.Client;
		_languageId = languageId;
	}

	/// <summary>
	/// Sends the open, change, or close notification for one synchronization directive; a change for a session without a negotiated synchronization
	/// mode is skipped and logged.
	/// </summary>
	/// <param name="request">The synchronization directive to deliver.</param>
	/// <param name="cancellationToken">A token that can cancel the send.</param>
	/// <returns>
	/// <see langword="true"/> when a notification was delivered; otherwise, <see langword="false"/> (no client is configured, or a change
	/// cannot be expressed by the negotiated synchronization mode).
	/// </returns>
	internal async Task<bool> SendAsync(DocumentSynchronizationDirective request, CancellationToken cancellationToken)
	{
		if (_client is null)
			return false;

		if (request.Kind == DocumentSynchronizationKind.Close)
		{
			await _client
				.SendNotificationAsync(
					LspMethodNames.DidClose,
					new DidCloseTextDocumentParams(new TextDocumentIdentifier(request.Document.Uri)),
					cancellationToken
				)
				.ConfigureAwait(false);

			return true;
		}

		if (request.Kind == DocumentSynchronizationKind.Open)
		{
			await _client
				.SendNotificationAsync(
					LspMethodNames.DidOpen,

					new DidOpenTextDocumentParams(
						new TextDocumentItemPayload(
							Uri: request.Document.Uri,
							LanguageId: _languageId(),
							Version: request.Document.Version,
							Text: request.Document.Content
						)
					),

					cancellationToken
				)
				.ConfigureAwait(false);

			return true;
		}

		if (request.Kind != DocumentSynchronizationKind.Change)
			return false;

		// A session without a negotiated synchronization mode cannot express the change and may also report
		// None after its active connection was detached. Skip the change with a warning instead of failing the
		// operation: the tracked content stays committed, nothing is sent while the mode is unavailable, and
		// the next session re-establishes the full content through the restart replay.
		if (_client.NegotiatedCapabilities.TextDocumentSyncKind == TextDocumentSyncKind.None)
		{
			long connectionGeneration = _client.ConnectionGeneration;

			// Log the first skip on a connection generation at warning level and later skips at debug, so a session
			// that negotiated no synchronization mode does not emit one warning per keystroke.
			if (Interlocked.Exchange(ref _changeSkipWarningConnectionGeneration, connectionGeneration) != connectionGeneration)
			{
				_context.Logger.LogWarning(
					"{DisplayName} skipped a document change for '{FilePath}' because the language server session negotiated no document synchronization mode.",
					_context.ProviderDisplayName,
					request.Document.FilePath
				);
			}
			else
			{
				_context.Logger.LogDebug(
					"{DisplayName} skipped a document change for '{FilePath}' because the language server session negotiated no document synchronization mode.",
					_context.ProviderDisplayName,
					request.Document.FilePath
				);
			}

			return false;
		}

		TextDocumentContentChangeEventPayload contentChange = _client.NegotiatedCapabilities.TextDocumentSyncKind switch
		{
			TextDocumentSyncKind.Incremental when request.ChangeRange is { } changeRange => new(
				changeRange.Text,
				ProtocolRangeConversion.ToProtocolRange(changeRange.Range)
			),

			// Full synchronization, and incremental requests without a computed range, send the whole content.
			_ => new(request.Document.Content),
		};

		await _client
			.SendNotificationAsync(
				LspMethodNames.DidChange,
				new DidChangeTextDocumentParams(new VersionedTextDocumentIdentifier(request.Document.Uri, request.Document.Version), [contentChange]),
				cancellationToken
			)
			.ConfigureAwait(false);

		return true;
	}
}
