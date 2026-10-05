namespace Nickelony.LanguageServer.Provider;

/// <summary>Owns a provider's startup flags and counters and the connection generations that fence stale connection events.</summary>
/// <remarks>
/// Startup state and every write to the owning provider's state
/// value are serialized by one lock, so connection-unavailable callbacks and startup completions observe a consistent view: a completion that records
/// success on a generation fences itself against unavailability notifications for that same generation, and vice versa. The rationale lives
/// in the <see href="https://github.com/Nickelony/LanguageServer/blob/main/docs/LanguageServerInternals.md">language-server internals guide</see>.
/// </remarks>
internal sealed class StartupState
{
	private readonly object _syncRoot = new();
	private readonly ILanguageServerClient? _client;
	private readonly ProviderLifetime _disposal;

	private int _providerState = (int)LanguageServerFeatureProviderState.Unavailable;

	private bool _startupSucceeded;
	private bool _hasCompletedStartupOnce;

	private long _readyConnectionGeneration;
	private long _lastUnavailableConnectionGeneration;

	private int _consecutiveStartupFailures;

	private bool _persistentStartupFailureReported;
	private bool _transientStartupFailureReported;

	/// <param name="client">The language-server client, or <see langword="null"/> when no server is configured.</param>
	/// <param name="disposal">The disposal whose start freezes every state transition except the move to the terminal disposed state.</param>
	internal StartupState(ILanguageServerClient? client, ProviderLifetime disposal)
	{
		_client = client;
		_disposal = disposal;
	}

	internal LanguageServerFeatureProviderState State => (LanguageServerFeatureProviderState)Volatile.Read(ref _providerState);

	/// <summary>Indicates whether the most recent startup completed on the current connection generation.</summary>
	internal bool StartupSucceeded
	{
		get
		{
			lock (_syncRoot)
				return _startupSucceeded;
		}
	}

	/// <summary>Indicates whether a startup completed successfully at least once, so a current start attempt is a restart.</summary>
	internal bool HasCompletedStartupOnce
	{
		get
		{
			lock (_syncRoot)
				return _hasCompletedStartupOnce;
		}
	}

	internal int ConsecutiveStartupFailures
	{
		get
		{
			lock (_syncRoot)
				return _consecutiveStartupFailures;
		}
	}

	/// <summary>Attempts a provider-state transition.</summary>
	/// <param name="state">The requested provider state.</param>
	/// <param name="notifyCapabilitiesChanged">
	/// <see langword="true"/> to request a capabilities-changed notification when the state actually changed.
	/// </param>
	/// <returns><see langword="true"/> when the caller should raise the capabilities-changed event; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// Transitions are ignored once the provider has started disposing, and the disposed state is terminal: any later transition away
	/// from it is ignored.
	/// </remarks>
	internal bool TrySetState(LanguageServerFeatureProviderState state, bool notifyCapabilitiesChanged)
	{
		LanguageServerFeatureProviderState previousState;

		lock (_syncRoot)
		{
			if (_disposal.IsDisposed && state != LanguageServerFeatureProviderState.Disposed)
				return false;

			previousState = (LanguageServerFeatureProviderState)_providerState;

			if (previousState == LanguageServerFeatureProviderState.Disposed && state != LanguageServerFeatureProviderState.Disposed)
				return false;

			Volatile.Write(ref _providerState, (int)state);
		}

		return notifyCapabilitiesChanged && previousState != state;
	}

	/// <summary>Registers a failed startup attempt.</summary>
	/// <returns>The new consecutive startup-failure count.</returns>
	internal int RegisterStartupFailure()
	{
		lock (_syncRoot)
		{
			_startupSucceeded = false;
			return ++_consecutiveStartupFailures;
		}
	}

	/// <summary>Claims the one-time right to report a startup failure of the given persistence.</summary>
	/// <param name="isPersistentFailure">Whether the failure is persistent for this provider instance.</param>
	/// <returns><see langword="true"/> when the failure has not been reported yet; otherwise, <see langword="false"/>.</returns>
	internal bool TryMarkStartupFailureReported(bool isPersistentFailure)
	{
		lock (_syncRoot)
		{
			if (isPersistentFailure)
			{
				if (_persistentStartupFailureReported)
					return false;

				_persistentStartupFailureReported = true;
				return true;
			}

			if (_transientStartupFailureReported)
				return false;

			_transientStartupFailureReported = true;
			return true;
		}
	}

	/// <summary>
	/// Invalidates the startup-succeeded flag when the provider observes a connection failure itself, rather than through the client's
	/// unavailable event.
	/// </summary>
	internal void InvalidateStartupSucceeded()
	{
		lock (_syncRoot)
			_startupSucceeded = false;
	}

	/// <summary>
	/// Handles a connection-unavailable notification raised by the client (the client-event counterpart of <see cref="InvalidateStartupSucceeded"/>).
	/// </summary>
	/// <param name="connectionGeneration">The connection generation reported as unavailable.</param>
	/// <returns>
	/// <see langword="true"/> when the notification invalidated a startup that had completed on that generation and the caller should transition
	/// the provider state; otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// The unavailable generation is recorded even when the notification does not invalidate a completed startup, so a startup that completes
	/// afterwards on the same generation still fails its fencing check.
	/// </remarks>
	internal bool HandleClientConnectionUnavailable(long connectionGeneration)
	{
		lock (_syncRoot)
		{
			if (_disposal.IsDisposed)
				return false;

			if (connectionGeneration > _lastUnavailableConnectionGeneration)
				_lastUnavailableConnectionGeneration = connectionGeneration;

			if (!_startupSucceeded || _readyConnectionGeneration != connectionGeneration)
				return false;

			_startupSucceeded = false;
			return true;
		}
	}

	/// <summary>
	/// Completes a successful startup on <paramref name="connectionGeneration"/> when the client is still ready on that generation.
	/// </summary>
	/// <param name="connectionGeneration">The connection generation observed at startup.</param>
	/// <param name="previousState">The provider state before the transition; meaningful when the method returns <see langword="true"/>.</param>
	/// <returns><see langword="true"/> when the startup is accepted; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The startup fails when the provider is disposing, when the generation is zero, when the generation was already reported unavailable,
	/// when no client is configured, or when the client is not ready on the same generation. A successful startup resets the failure counters
	/// and reporting flags.
	/// </remarks>
	internal bool TryCompleteSuccessfulStart(long connectionGeneration, out LanguageServerFeatureProviderState previousState)
	{
		lock (_syncRoot)
		{
			previousState = (LanguageServerFeatureProviderState)_providerState;

			if (
				_disposal.IsDisposed
				|| connectionGeneration == 0
				|| _lastUnavailableConnectionGeneration == connectionGeneration
				|| _client is null
				|| !_client.IsReady
				|| _client.ConnectionGeneration != connectionGeneration
			)
			{
				_startupSucceeded = false;
				return false;
			}

			_startupSucceeded = true;
			_hasCompletedStartupOnce = true;
			_readyConnectionGeneration = connectionGeneration;
			_consecutiveStartupFailures = 0;
			_transientStartupFailureReported = false;
			_persistentStartupFailureReported = false;

			Volatile.Write(ref _providerState, (int)LanguageServerFeatureProviderState.Ready);
			return true;
		}
	}
}
