using System.Text.Json;

namespace Nickelony.LanguageServer.Client;

/// <summary>Connects a host to the language-server session of one workspace.</summary>
/// <remarks>
/// The thread-safety, event, restart, and disposal contracts are defined by <see cref="ILanguageServerClient"/>.
/// </remarks>
public sealed partial class LanguageServerClient : ILanguageServerClient
{
	private readonly ILogger _logger;

	/// <summary>The collaborators of this client.</summary>
	internal ClientComposition Composition { get; }

	/// <inheritdoc/>
	public bool IsReady => Composition.State.IsReady;

	/// <inheritdoc/>
	public long ConnectionGeneration => Composition.State.ConnectionGeneration;

	/// <inheritdoc/>
	public NegotiatedServerCapabilities NegotiatedCapabilities => Composition.State.Capabilities;

	/// <inheritdoc/>
	/// <remarks>
	/// <see cref="EnsureStartedAsync"/> reports a startup failure by returning <see langword="false"/>; this property exposes
	/// the cause for diagnostics instead of requiring a log scrape. It is cleared when a new startup attempt begins, so a later successful attempt
	/// does not keep reporting an older failure.
	/// </remarks>
	public Exception? LastStartupException => Composition.Startup.LastStartupException;

	/// <inheritdoc/>
	public event EventHandler<ConnectionUnavailableEventArgs>? ConnectionUnavailable;

	/// <inheritdoc/>
	public event EventHandler<DiagnosticsPublishedEventArgs>? DiagnosticsPublished
	{
		add => Composition.Router.AddDiagnosticsSubscriber(value);
		remove => Composition.Router.RemoveDiagnosticsSubscriber(value);
	}

	/// <inheritdoc/>
	public event EventHandler? SemanticTokensRefreshRequested
	{
		add => Composition.Router.AddSemanticTokensRefreshSubscriber(value);
		remove => Composition.Router.RemoveSemanticTokensRefreshSubscriber(value);
	}

	/// <inheritdoc/>
	public event EventHandler? DiagnosticRefreshRequested
	{
		add => Composition.Router.AddDiagnosticRefreshSubscriber(value);
		remove => Composition.Router.RemoveDiagnosticRefreshSubscriber(value);
	}

	/// <inheritdoc/>
	public Task<bool> EnsureStartedAsync(CancellationToken cancellationToken) => Composition.Startup.EnsureStartedAsync(cancellationToken);

	/// <summary>Marks one specific connection generation unhealthy only when it is still the active generation.</summary>
	/// <param name="connectionGeneration">
	/// The observed connection generation to invalidate, as reported by <see cref="ConnectionGeneration"/>.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when the observed generation was still active and was marked unhealthy; otherwise, <see langword="false"/>.
	/// Connection generations start at <c>1</c>, so the pre-start generation <c>0</c> is never marked unhealthy.
	/// </returns>
	public bool TryMarkConnectionUnhealthy(long connectionGeneration)
	{
		// Generation 0 means that no connection exists yet, so there is nothing to invalidate.
		if (Composition.Lifetime.IsDisposed || connectionGeneration <= 0)
			return false;

		if (!Composition.State.TryMarkConnectionUnhealthy(connectionGeneration, out bool wasReady))
			return false;

		if (wasReady)
		{
			_logger.LogWarning(
				"Marked language server connection generation {Generation} unhealthy; the host will restart it before the next public request.",
				connectionGeneration
			);

			RaiseConnectionUnavailable(connectionGeneration);
		}
		else
		{
			_logger.LogDebug(
				"Marked language server connection generation {Generation} unhealthy before it published readiness; the generation stays invalidated so a late startup completion cannot republish it as ready.",
				connectionGeneration
			);
		}

		return true;
	}

	/// <inheritdoc/>
	public async Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(method);
		await Composition.Sender.SendNotificationAsync(method, parameters, cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc/>
	public Task PushSettingsAsync(CancellationToken cancellationToken) => Composition.Sender.PushSettingsAsync(cancellationToken);

	/// <inheritdoc/>
	public async Task<TResult> SendRequestAsync<TResult>(string method, object parameters, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(method);
		ArgumentNullException.ThrowIfNull(parameters);

		return await Composition.Sender.SendRequestAsync<TResult>(method, parameters, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Notifies subscribers that one active ready connection was lost.</summary>
	/// <param name="connectionGeneration">The generation that was lost.</param>
	private void RaiseConnectionUnavailable(long connectionGeneration)
	{
		if (Composition.Lifetime.IsDisposed)
			return;

		EventHandler<ConnectionUnavailableEventArgs>? handlers = ConnectionUnavailable;

		if (handlers is null)
			return;

		var eventArgs = new ConnectionUnavailableEventArgs(connectionGeneration);

		foreach (
			EventHandler<ConnectionUnavailableEventArgs> handler in handlers.GetInvocationList().Cast<EventHandler<ConnectionUnavailableEventArgs>>()
		)
		{
			if (Composition.Lifetime.IsDisposed)
				return;

			try
			{
				handler(this, eventArgs);
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Language server connection-unavailable subscriber threw; later subscribers will still be notified.");
			}
		}
	}

	/// <param name="workspaceRootDirectoryPaths">
	/// The workspace root directories to host, or an empty list for a folderless session. The first entry is the primary
	/// root published as the initialize <c>rootUri</c>; every entry is advertised as a workspace folder in the caller's order. Entries may be nested;
	/// duplicates are rejected by comparing normalized paths with the platform's local-path identity (case-insensitive on Windows and macOS,
	/// ordinal elsewhere).
	/// </param>
	/// <param name="transport">
	/// The transport that opens each connection, for example a <see cref="StdioLanguageServerTransport"/> that launches the server executable.
	/// </param>
	/// <param name="hooks">
	/// The delegates that receive the server messages the client does not surface by itself, or <see langword="null"/> for none.
	/// </param>
	/// <param name="options">The client configuration, or <see langword="null"/> for <see cref="LanguageServerClientOptions.Default"/>.</param>
	/// <param name="logger">The logger instance, or <see langword="null"/> for a no-op logger.</param>
	/// <exception cref="ArgumentException">
	/// <paramref name="workspaceRootDirectoryPaths"/> contains a <see langword="null"/>, empty, whitespace-only, relative, or duplicate entry.
	/// </exception>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="workspaceRootDirectoryPaths"/> or <paramref name="transport"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="NotSupportedException">A workspace root directory path uses a path form the host cannot normalize.</exception>
	/// <exception cref="PathTooLongException">A workspace root directory path exceeds the host path-length limit.</exception>
	public LanguageServerClient(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		ILanguageServerTransport transport,
		LanguageServerClientHooks? hooks = null,
		LanguageServerClientOptions? options = null,
		ILogger? logger = null
	)
		: this(workspaceRootDirectoryPaths, transport, hooks, options, logger, testHooks: null, serverMessageGate: null) { }

	/// <inheritdoc cref="LanguageServerClient(IReadOnlyList{string}, ILanguageServerTransport, LanguageServerClientHooks?, LanguageServerClientOptions?, ILogger?)"/>
	internal LanguageServerClient(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		ILanguageServerTransport transport,
		LanguageServerClientHooks? hooks,
		LanguageServerClientOptions? options,
		ILogger? logger,
		LanguageServerClientTestHooks? testHooks = null,
		Func<long, bool>? serverMessageGate = null
	)
	{
		ArgumentNullException.ThrowIfNull(workspaceRootDirectoryPaths);
		ArgumentNullException.ThrowIfNull(transport);

		_logger = logger ?? NullLogger.Instance;

		Composition = new ClientComposition(
			owner: this,
			hooks: hooks ?? new LanguageServerClientHooks(),
			options: options ?? LanguageServerClientOptions.Default,
			workspaceRoots: WorkspaceRoots.Create(workspaceRootDirectoryPaths),
			transport,
			testHooks: testHooks ?? LanguageServerClientTestHooks.None,
			serverMessageGate,
			TryMarkConnectionUnhealthy,
			RaiseConnectionUnavailable,
			ThrowIfDisposed,
			logger: _logger
		);
	}

	/// <summary>
	/// Creates serializer options that match the protocol payload convention this client uses: lower-camel member names, as the language-server
	/// protocol wire format requires.
	/// </summary>
	/// <returns>A new options instance owned by the caller.</returns>
	/// <remarks>
	/// <para>
	/// The client applies this naming policy to the payloads
	/// it serializes itself (client capabilities, initialization options, and the settings snapshot), and its built-in protocol payload
	/// records declare their wire names explicitly with <see cref="System.Text.Json.Serialization.JsonPropertyNameAttribute"/>. A host that sends
	/// a custom request or notification, or a payload built from its own DTO, must reach the same wire casing; serialize that payload
	/// with these options before passing it to <see cref="SendRequestAsync{TResult}"/> or <see cref="SendNotificationAsync"/>, for example:
	/// <c>client.SendRequestAsync&lt;JsonElement&gt;("vendor/method", JsonSerializer.SerializeToNode(payload, LanguageServerClient.CreateProtocolSerializerOptions())!, cancellationToken)</c>.
	/// </para>
	/// <para>
	/// A fresh instance is returned on every call, so a host can extend the options (for example with a converter) without changing what the client
	/// itself uses.
	/// </para>
	/// </remarks>
	public static JsonSerializerOptions CreateProtocolSerializerOptions() => new(ProtocolSerialization.ForPayloads());
}
