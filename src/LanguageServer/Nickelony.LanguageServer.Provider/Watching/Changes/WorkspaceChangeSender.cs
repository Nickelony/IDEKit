using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Sends one forwarded batch of workspace changes to the language server as LSP notifications.</summary>
/// <remarks>
/// <para>
/// A settings push precedes the watched-files notification when the batch touched a configuration path. The configuration-path hook
/// belongs to the language package, so a fault in it is contained and logged instead of breaking the forwarding; the client contains a fault
/// of its own settings factory the same way.
/// </para>
/// <para>
/// A connection failure marks the connection generation observed before the send unhealthy and propagates, so the forwarder buffers the batch
/// for a later replay.
/// </para>
/// </remarks>
internal sealed class WorkspaceChangeSender
{
	private readonly Func<ILanguageServerClient?> _clientAccessor;
	private readonly ProviderLifetime _disposal;
	private readonly Action<long> _markConnectionUnhealthy;

	private readonly Func<string, bool> _isConfigurationPath;

	private readonly string _providerDisplayName;
	private readonly ILogger _logger;

	/// <param name="owner">The owner callbacks used for client access, disposal probing and connection marking.</param>
	/// <param name="isConfigurationPath">Reports whether a normalized changed path requires a settings refresh.</param>
	/// <param name="providerDisplayName">The provider display name used in hook-containment diagnostics.</param>
	/// <param name="logger">The logger instance.</param>
	internal WorkspaceChangeSender(
		WorkspaceChangeOwnerContext owner,
		Func<string, bool> isConfigurationPath,
		string providerDisplayName,
		ILogger logger
	)
	{
		_clientAccessor = owner.ClientAccessor;
		_disposal = owner.Disposal;
		_markConnectionUnhealthy = owner.MarkConnectionUnhealthy;
		_isConfigurationPath = isConfigurationPath;
		_providerDisplayName = providerDisplayName;
		_logger = logger;
	}

	/// <summary>
	/// Sends one forwarded batch: a settings push (when the batch touched a configuration path) precedes the watched-files notification,
	/// and a connection failure marks the generation observed before the send unhealthy.
	/// </summary>
	/// <param name="changes">The forwarded file changes.</param>
	/// <param name="cancellationToken">A token that can cancel the sends.</param>
	internal async Task SendAsync(IReadOnlyList<FileChange> changes, CancellationToken cancellationToken)
	{
		ILanguageServerClient? client = _clientAccessor();

		if (client is null || _disposal.IsDisposed || changes.Count == 0)
			return;

		long connectionGeneration = client.ConnectionGeneration;

		bool shouldRefreshConfiguration = false;
		var payloads = new List<FileEventPayload>(changes.Count);

		for (int i = 0; i < changes.Count; i++)
		{
			FileChange change = changes[i];

			// The containment hook is an extension point, so it runs only until the batch has proven to
			// contain a configuration path instead of once per remaining path.
			if (!shouldRefreshConfiguration)
				shouldRefreshConfiguration = RequiresConfigurationRefresh(change.Path);

			payloads.Add(
				new FileEventPayload(DocumentUriConversion.CreateFileUri(change.Path), ProtocolFileChangeConversion.ToProtocol(change.Kind))
			);
		}

		try
		{
			if (shouldRefreshConfiguration)
				await client.PushSettingsAsync(cancellationToken).ConfigureAwait(false);

			await client
				.SendNotificationAsync(LspMethodNames.DidChangeWatchedFiles, new DidChangeWatchedFilesParams([.. payloads]), cancellationToken)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			_markConnectionUnhealthy(connectionGeneration);
			throw;
		}
		catch (IOException)
		{
			_markConnectionUnhealthy(connectionGeneration);
			throw;
		}
		catch (ObjectDisposedException) when (!_disposal.IsDisposed)
		{
			_markConnectionUnhealthy(connectionGeneration);
			throw;
		}
	}

	/// <summary>Invokes the configuration-path hook, containing failures so a provider defect cannot break file forwarding.</summary>
	/// <param name="normalizedPath">The normalized path of the changed file.</param>
	/// <returns><see langword="true"/> when the path requires a settings refresh; otherwise, <see langword="false"/>.</returns>
	private bool RequiresConfigurationRefresh(string normalizedPath)
	{
		return CallbackContainment.Invoke(
			callback: () => _isConfigurationPath(normalizedPath),
			fallbackValue: false,

			reportFault: exception =>
				_logger.LogWarning(
					exception,
					"The configuration-path hook failed for the {DisplayName} provider; the failure is contained.",
					_providerDisplayName
				)
		);
	}
}
