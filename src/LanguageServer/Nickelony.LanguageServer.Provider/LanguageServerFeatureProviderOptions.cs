namespace Nickelony.LanguageServer.Provider;

/// <summary>Defines the tunable provider framework defaults used by <c>LanguageServerFeatureProvider</c>.</summary>
/// <remarks>
/// Override individual values on <see cref="Default"/> with a <c>with</c> expression or an object initializer, then pass the instance
/// to the provider constructor. Each property validates its value when it is assigned and throws <see cref="ArgumentOutOfRangeException"/>
/// for one outside its supported range.
/// </remarks>
public sealed record LanguageServerFeatureProviderOptions
{
	private TimeSpan _requestTimeout = TimeSpan.FromSeconds(10);

	private int _requestTimeoutRestartThreshold = 2;
	private int _hardStartupFailureThreshold = 3;
	private int _maxTrackedIdleDocuments = 16;

	public static LanguageServerFeatureProviderOptions Default { get; } = new();

	/// <summary>The per-request timeout applied to language-server requests; defaults to 10 seconds.</summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The value is not greater than zero, or exceeds <see cref="int.MaxValue"/> milliseconds.
	/// </exception>
	/// <remarks>
	/// A request that times out returns its documented fallback value, and consecutive timeouts on one connection generation mark it unhealthy
	/// once <see cref="RequestTimeoutRestartThreshold"/> is reached, so the next request restarts the connection.
	/// </remarks>
	public TimeSpan RequestTimeout
	{
		get => _requestTimeout;
		init
		{
			if (value <= TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue)
			{
				throw new ArgumentOutOfRangeException(
					nameof(value),
					value,
					"The request timeout must be greater than zero and at most Int32.MaxValue milliseconds."
				);
			}

			_requestTimeout = value;
		}
	}

	/// <summary>
	/// The number of consecutive request timeouts on one connection generation before that connection is marked unhealthy and the next request
	/// triggers a restart; defaults to 2.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	/// <remarks>
	/// <para>
	/// Zero disables timeout-driven restarts, so a request that times out always returns its fallback value without restarting the connection.
	/// </para>
	/// <para>
	/// Restarting on repeated timeouts is a deliberate fail-safe for an unresponsive server. Disable it (or raise <see cref="RequestTimeout"/>)
	/// for servers that legitimately exceed the timeout while they index a workspace, so a slow-but-healthy server is not restarted after it discards
	/// its warm-up work.
	/// </para>
	/// </remarks>
	public int RequestTimeoutRestartThreshold
	{
		get => _requestTimeoutRestartThreshold;
		init
		{
			if (value < 0)
			{
				throw new ArgumentOutOfRangeException(
					nameof(value),
					value,
					"The request-timeout restart threshold must not be negative; zero disables timeout-driven restarts."
				);
			}

			_requestTimeoutRestartThreshold = value;
		}
	}

	/// <summary>
	/// The number of consecutive startup failures after which the provider enters the failed state and stops attempting to start
	/// until it is recreated; defaults to 3.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is less than one.</exception>
	/// <remarks>
	/// A failed restart replay (a start that succeeds but cannot reopen its tracked documents) also counts as a failed startup attempt.
	/// </remarks>
	public int HardStartupFailureThreshold
	{
		get => _hardStartupFailureThreshold;
		init
		{
			if (value < 1)
				throw new ArgumentOutOfRangeException(nameof(value), value, "The hard startup-failure threshold must be at least one.");

			_hardStartupFailureThreshold = value;
		}
	}

	/// <summary>
	/// The maximum number of idle tracked documents the provider keeps alive before it trims the least recently used idle documents and closes
	/// them on the server; defaults to 16.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
	/// <remarks>
	/// <para>
	/// Zero disables idle document retention, so an idle document is closed as soon as its last lease is released.
	/// </para>
	/// <para>
	/// Idle means the document has no open lease and no temporary request lease. Documents held by an open lease are never trimmed, and a document
	/// whose close was deferred because a request lease was still active is closed when that lease is released regardless of this cap.
	/// </para>
	/// </remarks>
	public int MaxTrackedIdleDocuments
	{
		get => _maxTrackedIdleDocuments;
		init
		{
			if (value < 0)
				throw new ArgumentOutOfRangeException(nameof(value), value, "The tracked idle document limit must not be negative.");

			_maxTrackedIdleDocuments = value;
		}
	}

	/// <summary>
	/// The comparison that decides whether two local paths identify one document;
	/// defaults to <see cref="LocalPathComparisonPolicy.ForCurrentPlatform"/>.
	/// </summary>
	/// <remarks>
	/// Every collection the provider keys by path - the tracked documents, their scheduling and cancellation slots,
	/// the pending closes, and the workspace change buffers and snapshots - compares paths through this one value, so a host with a case-sensitive
	/// store sets <see cref="LocalPathComparisonPolicy.CaseSensitive"/> and two paths that differ only by case stay two documents.
	/// The language-package conventions that compare file extensions follow the platform instead. The client the provider drives keys its diagnostics
	/// callbacks and checks its workspace roots with the platform default, whatever this value is.
	/// </remarks>
	public LocalPathComparisonPolicy PathComparison { get; init; } = LocalPathComparisonPolicy.ForCurrentPlatform;

	/// <summary>Resolves the path comparison of a collaborator that takes the options as an optional argument.</summary>
	/// <param name="options">The provider options, or <see langword="null"/> for <see cref="Default"/>.</param>
	/// <returns>The path comparison of <paramref name="options"/>, or of <see cref="Default"/> when it is <see langword="null"/>.</returns>
	internal static LocalPathComparisonPolicy PathComparisonOf(LanguageServerFeatureProviderOptions? options) => (options ?? Default).PathComparison;
}
