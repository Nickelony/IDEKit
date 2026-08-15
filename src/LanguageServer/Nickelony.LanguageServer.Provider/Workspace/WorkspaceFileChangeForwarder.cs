namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Forwards workspace file changes when the owner allows it, buffers recoverable delivery failures for replay,
/// and reports unexpected dropped batches with forwarding context.
/// </summary>
/// <remarks>
/// <para>
/// This is an internal provider-composition helper rather than a host API: the provider base and the workspace
/// change coordinator build their forwarding on it, and a host consumes the resulting provider through the
/// Abstractions contract instead of talking to this type directly.
/// </para>
/// <para>
/// Buffered changes are replayed only when the caller invokes <see cref="ReplayDeferredAsync"/>;
/// <see cref="DispatchAsync"/> never drains the deferred set, so a caller that wants guaranteed ordering must replay
/// the deferred changes before forwarding new ones. A batch that fails a dispatch attempt stays ahead of changes
/// that were buffered while the attempt was in flight, so a re-buffered delete cannot cancel against a newer create
/// for the same path. Disposal is immediate and drops undelivered state: an incoming change set is discarded
/// instead of being buffered or forwarded, buffered changes are not replayed, and a host that needs them recovered
/// replaces the forwarder and reconciles missed changes through its workspace snapshot.
/// </para>
/// </remarks>
internal sealed partial class WorkspaceFileChangeForwarder : IDisposable
{
	// Forwarding prerequisites.
	private readonly Func<bool> _canForwardAccessor;
	private readonly Func<bool> _isDisposedAccessor;
	private readonly Func<CancellationToken, Task<bool>> _ensureTransportStartedAsync;
	private readonly Action _tryMarkTransportUnhealthy;
	private readonly Action<WorkspaceFileForwardingFailure>? _logForwardingFailure;
	private readonly bool _bufferChangesWhileForwardingDisabled;

	// Buffered changes are split into two accumulators: _replayedChanges holds batches that failed a dispatch
	// attempt (they keep their original order and stay ahead of changes that arrived while the attempt was in
	// flight), and _pendingChanges holds changes buffered while forwarding was not allowed. Keeping them apart
	// prevents a re-buffered delete from canceling against a newer create for the same path.
	private readonly WorkspaceChangeAccumulator _pendingChanges = new();
	private readonly WorkspaceChangeAccumulator _replayedChanges = new();
	private readonly SemaphoreSlim _forwardingGate = new(1, 1);
	private readonly object _disposeSyncRoot = new();
	private int _activeOperationCount;
	private bool _disposeRequested;
	private bool _disposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceFileChangeForwarder"/> class.
	/// </summary>
	/// <param name="canForwardAccessor">Reports whether forwarding attempts are currently allowed.</param>
	/// <param name="isDisposedAccessor">Reports whether the owner has been disposed.</param>
	/// <param name="ensureTransportStartedAsync">Ensures the transport is running before forwarding.</param>
	/// <param name="tryMarkTransportUnhealthy">Attempts to mark the current transport as unhealthy after recoverable forwarding failures; the callback handles its own failure.</param>
	/// <param name="logForwardingFailure">Logs forwarding failures together with batch context.</param>
	/// <param name="bufferChangesWhileForwardingDisabled">Whether changes should be buffered instead of dropped while forwarding is temporarily disallowed.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="canForwardAccessor"/>, <paramref name="isDisposedAccessor"/>, <paramref name="ensureTransportStartedAsync"/>,
	/// or <paramref name="tryMarkTransportUnhealthy"/> is <see langword="null"/>.
	/// </exception>
	public WorkspaceFileChangeForwarder(
		Func<bool> canForwardAccessor,
		Func<bool> isDisposedAccessor,
		Func<CancellationToken, Task<bool>> ensureTransportStartedAsync,
		Action tryMarkTransportUnhealthy,
		Action<WorkspaceFileForwardingFailure>? logForwardingFailure = null,
		bool bufferChangesWhileForwardingDisabled = true)
	{
		ArgumentNullException.ThrowIfNull(canForwardAccessor);
		ArgumentNullException.ThrowIfNull(isDisposedAccessor);
		ArgumentNullException.ThrowIfNull(ensureTransportStartedAsync);
		ArgumentNullException.ThrowIfNull(tryMarkTransportUnhealthy);

		_canForwardAccessor = canForwardAccessor;
		_isDisposedAccessor = isDisposedAccessor;
		_ensureTransportStartedAsync = ensureTransportStartedAsync;
		_tryMarkTransportUnhealthy = tryMarkTransportUnhealthy;
		_logForwardingFailure = logForwardingFailure;
		_bufferChangesWhileForwardingDisabled = bufferChangesWhileForwardingDisabled;
	}
}
