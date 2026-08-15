namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Tracks the startup-succeeded flag, consecutive startup-failure counter, failure-reporting flags,
/// and transport generations used to fence stale transport events for a language-server provider.
/// </summary>
/// <remarks>
/// All startup state and the owning provider's state value are serialized by one lock, so transport-unavailable
/// callbacks and startup completions observe a consistent view: a completion that records success on a generation
/// fences itself against unavailability notifications for that same generation, and vice versa. The rationale lives
/// in <c>docs/LanguageServerInternals.md</c>.
/// </remarks>
internal sealed class StartupState
{
	private readonly object _syncRoot = new();
	private readonly ILanguageServerClient? _client;
	private readonly Func<bool> _isDisposedAccessor;
	private readonly LanguageServerProviderState _readyState;
	private readonly LanguageServerProviderState _disposedState;

	private int _providerState;
	private bool _startupSucceeded;
	private bool _hasCompletedStartupOnce;
	private long _readyTransportGeneration;
	private long _lastUnavailableTransportGeneration;
	private int _consecutiveStartupFailures;
	private bool _persistentStartupFailureReported;
	private bool _transientStartupFailureReported;

	/// <summary>
	/// Initializes a new instance of the <see cref="StartupState"/> class.
	/// </summary>
	/// <param name="client">The language-server client, or <see langword="null"/> when no server is configured.</param>
	/// <param name="initialState">The provider state used before the first startup completes.</param>
	/// <param name="readyState">The provider state set when a startup completes successfully.</param>
	/// <param name="disposedState">The terminal provider state; no other transition is allowed after it.</param>
	/// <param name="isDisposedAccessor">Returns whether the owning provider has started disposing.</param>
	internal StartupState(
		ILanguageServerClient? client,
		LanguageServerProviderState initialState,
		LanguageServerProviderState readyState,
		LanguageServerProviderState disposedState,
		Func<bool> isDisposedAccessor)
	{
		_client = client;
		_providerState = (int)initialState;
		_readyState = readyState;
		_disposedState = disposedState;
		_isDisposedAccessor = isDisposedAccessor;
	}

	/// <summary>
	/// Gets the current provider state.
	/// </summary>
	internal LanguageServerProviderState State => (LanguageServerProviderState)Volatile.Read(ref _providerState);

	/// <summary>
	/// Gets a value indicating whether the most recent startup completed on the current transport generation.
	/// </summary>
	internal bool StartupSucceeded
	{
		get
		{
			lock (_syncRoot)
				return _startupSucceeded;
		}
	}

	/// <summary>
	/// Gets a value indicating whether a startup completed successfully at least once, so a current start attempt
	/// is a restart.
	/// </summary>
	internal bool HasCompletedStartupOnce
	{
		get
		{
			lock (_syncRoot)
				return _hasCompletedStartupOnce;
		}
	}

	/// <summary>
	/// Gets the number of consecutive failed startup attempts.
	/// </summary>
	internal int ConsecutiveStartupFailures
	{
		get
		{
			lock (_syncRoot)
				return _consecutiveStartupFailures;
		}
	}

	/// <summary>
	/// Attempts a provider-state transition.
	/// </summary>
	/// <param name="state">The requested provider state.</param>
	/// <param name="notifyCapabilitiesChanged"><see langword="true"/> to request a capabilities-changed notification when the state actually changed.</param>
	/// <returns><see langword="true"/> when the caller should raise the capabilities-changed event; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// Transitions are ignored once the provider has started disposing, and the disposed state is
	/// terminal: any later transition away from it is ignored.
	/// </remarks>
	internal bool TrySetState(LanguageServerProviderState state, bool notifyCapabilitiesChanged)
	{
		LanguageServerProviderState previousState;

		lock (_syncRoot)
		{
			if (_isDisposedAccessor() && state != _disposedState)
				return false;

			previousState = (LanguageServerProviderState)_providerState;

			if (previousState == _disposedState && state != _disposedState)
				return false;

			Volatile.Write(ref _providerState, (int)state);
		}

		return notifyCapabilitiesChanged && previousState != state;
	}

	/// <summary>
	/// Registers a failed startup attempt.
	/// </summary>
	/// <returns>The new consecutive startup-failure count.</returns>
	internal int RegisterStartupFailure()
	{
		lock (_syncRoot)
		{
			_startupSucceeded = false;
			return ++_consecutiveStartupFailures;
		}
	}

	/// <summary>
	/// Claims the one-time right to report a startup failure of the given persistence.
	/// </summary>
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
	/// Invalidates the startup-succeeded flag after the transport was marked unavailable outside the request path.
	/// </summary>
	internal void InvalidateStartupSucceeded()
	{
		lock (_syncRoot)
			_startupSucceeded = false;
	}

	/// <summary>
	/// Handles a transport-unavailable notification raised by the client (the client-event counterpart of
	/// <see cref="InvalidateStartupSucceeded"/>).
	/// </summary>
	/// <param name="transportGeneration">The transport generation reported as unavailable.</param>
	/// <returns>
	/// <see langword="true"/> when the notification invalidated a startup that had completed on that
	/// generation and the caller should transition the provider state; otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// The unavailable generation is recorded even when the notification does not invalidate a
	/// completed startup, so a startup that completes afterwards on the same generation still fails
	/// its fencing check.
	/// </remarks>
	internal bool OnClientTransportUnavailable(long transportGeneration)
	{
		lock (_syncRoot)
		{
			if (_isDisposedAccessor())
				return false;

			if (transportGeneration > _lastUnavailableTransportGeneration)
				_lastUnavailableTransportGeneration = transportGeneration;

			if (!_startupSucceeded || _readyTransportGeneration != transportGeneration)
				return false;

			_startupSucceeded = false;
			return true;
		}
	}

	/// <summary>
	/// Completes a successful startup on <paramref name="transportGeneration"/> when the client is still ready on that generation.
	/// </summary>
	/// <param name="transportGeneration">The transport generation observed at startup.</param>
	/// <param name="previousState">The provider state before the transition; meaningful when the method returns <see langword="true"/>.</param>
	/// <returns><see langword="true"/> when the startup is accepted; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// The startup fails when the provider is disposing, when the generation is zero, when the generation was
	/// already reported unavailable, when no client is configured, or when the client is no longer ready on the
	/// same generation. A successful startup resets the failure counters and reporting flags.
	/// </remarks>
	internal bool TryCompleteSuccessfulStart(long transportGeneration, out LanguageServerProviderState previousState)
	{
		lock (_syncRoot)
		{
			previousState = (LanguageServerProviderState)_providerState;

			if (_isDisposedAccessor()
				|| transportGeneration == 0
				|| _lastUnavailableTransportGeneration == transportGeneration
				|| _client is null
				|| !_client.IsReady
				|| _client.TransportGeneration != transportGeneration)
			{
				_startupSucceeded = false;
				return false;
			}

			_startupSucceeded = true;
			_hasCompletedStartupOnce = true;
			_readyTransportGeneration = transportGeneration;
			_consecutiveStartupFailures = 0;
			_transientStartupFailureReported = false;
			_persistentStartupFailureReported = false;

			Volatile.Write(ref _providerState, (int)_readyState);
			return true;
		}
	}
}
