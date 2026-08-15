namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Defines the tunable provider framework defaults used by <see cref="LanguageServerIntelliSenseProviderBase"/>.
/// </summary>
/// <remarks>
/// The defaults mirror the values the framework was designed around: a ten-second request timeout with a
/// two-timeout restart threshold, a three-attempt hard startup-failure threshold, and a sixteen-document cap for
/// idle tracked documents. Override individual values on <see cref="Default"/> with a <c>with</c> expression and
/// pass the instance to the provider base constructor, which validates them and throws
/// <see cref="ArgumentOutOfRangeException"/> for a value outside its supported range.
/// </remarks>
public sealed record LanguageServerProviderOptions
{
	/// <summary>
	/// Gets the shared default options instance.
	/// </summary>
	public static LanguageServerProviderOptions Default { get; } = new();

	/// <summary>
	/// Gets the per-request timeout applied to language-server requests. Must be greater than zero and at most
	/// <see cref="int.MaxValue"/> milliseconds.
	/// </summary>
	/// <remarks>
	/// When a request times out, the provider returns the request's documented fallback value. Consecutive timeouts
	/// on one transport generation are counted and mark that generation unhealthy once the
	/// <see cref="RequestTimeoutRestartThreshold"/> is reached, so the next request restarts the transport.
	/// </remarks>
	public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);

	/// <summary>
	/// Gets the number of consecutive request timeouts on one transport generation before that transport is
	/// marked unhealthy and the next request triggers a restart.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Must not be negative; zero disables timeout-driven restarts, so a request that times out always returns its
	/// fallback value without restarting the transport.
	/// </para>
	/// <para>
	/// Restarting on repeated timeouts is a deliberate fail-safe for an unresponsive server, and it deviates from
	/// mainstream language-server clients, which let requests hang or rely on the server process terminating.
	/// Disable it (or raise <see cref="RequestTimeout"/>) for servers that legitimately exceed the timeout while
	/// they index a workspace, so a slow-but-healthy server is not restarted after it discards its warm-up work.
	/// </para>
	/// </remarks>
	public int RequestTimeoutRestartThreshold { get; init; } = 2;

	/// <summary>
	/// Gets the number of consecutive startup failures after which the provider enters the failed state and stops
	/// attempting to start until it is recreated.
	/// </summary>
	/// <remarks>
	/// A failed restart replay (a start that succeeds but cannot reopen its tracked documents) counts as a failed
	/// startup attempt as well. Must be at least one.
	/// </remarks>
	public int HardStartupFailureThreshold { get; init; } = 3;

	/// <summary>
	/// Gets the maximum number of idle tracked documents the provider keeps alive before it trims the least
	/// recently used idle documents and closes them on the server.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Must not be negative; zero disables idle document retention, so an idle document is closed as soon as its
	/// last reference is released.
	/// </para>
	/// <para>
	/// Idle means the document has no open reference and no temporary request reference. Documents held by an open
	/// reference are never trimmed, and a document whose close was deferred because a request reference was still
	/// active is closed when that reference is released regardless of this cap.
	/// </para>
	/// </remarks>
	public int MaxTrackedIdleDocuments { get; init; } = 16;

	/// <summary>
	/// Gets the factory that creates the workspace file watcher for each workspace root, or <see langword="null"/> to
	/// use the framework default, which watches through <see cref="WorkspaceFileWatcher"/> over the specifications
	/// passed to the provider constructor.
	/// </summary>
	/// <remarks>
	/// Assign a factory to substitute the watching engine for a host environment, for example with a host file API or
	/// a container-aware watcher. This is equivalent to overriding the provider base's
	/// <c>CreateWorkspaceFileWatcher</c> hook, but does not require subclassing the provider. The factory is invoked
	/// once per workspace root and must return an unstarted watcher; the framework starts it and reports a startup
	/// failure through the provider's <see cref="LanguageServerIntelliSenseProviderBase.WorkspaceWatcherFailed"/> event
	/// instead of letting the failure escape request APIs.
	/// </remarks>
	public WorkspaceFileWatcherFactory? WorkspaceFileWatcherFactory { get; init; }

	/// <summary>
	/// Validates the option values against the ranges the provider framework supports.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">One of the option values is outside its supported range.</exception>
	internal void Validate()
	{
		if (RequestTimeout <= TimeSpan.Zero || RequestTimeout.TotalMilliseconds > int.MaxValue)
		{
			throw new ArgumentOutOfRangeException(nameof(RequestTimeout), RequestTimeout,
				"The request timeout must be greater than zero and at most Int32.MaxValue milliseconds.");
		}

		if (RequestTimeoutRestartThreshold < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(RequestTimeoutRestartThreshold), RequestTimeoutRestartThreshold,
				"The request-timeout restart threshold must not be negative; zero disables timeout-driven restarts.");
		}

		if (HardStartupFailureThreshold < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(HardStartupFailureThreshold), HardStartupFailureThreshold,
				"The hard startup-failure threshold must be at least one.");
		}

		if (MaxTrackedIdleDocuments < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(MaxTrackedIdleDocuments), MaxTrackedIdleDocuments,
				"The tracked idle document limit must not be negative.");
		}
	}
}
