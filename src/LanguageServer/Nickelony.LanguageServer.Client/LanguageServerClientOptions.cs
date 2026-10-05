using System.Text.Json.Nodes;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Describes the payload factories and lifecycle timeouts for a language-server client.
/// </summary>
/// <remarks>
/// <para>
/// The payload factories may be invoked from background connection threads and should be thread-safe,
/// non-blocking, and cheap to execute. What the client connects to belongs to its <see cref="ILanguageServerTransport"/>.
/// </para>
/// <para>
/// Record equality compares the delegate members by delegate equality, so two instances built from equal-but-distinct lambdas are not equal.
/// Compare the configuration explicitly when a cache needs to react to option changes.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var options = LanguageServerClientOptions.Default with
/// {
///     SettingsProvider = () =&gt; new JsonObject { ["maxPreload"] = 10 },
///     InitializeRequestTimeout = TimeSpan.FromSeconds(20)
/// };
/// </code>
/// </example>
public sealed record LanguageServerClientOptions
{
	/// <summary>The settings provider used when the host passes none: an empty settings object.</summary>
	private static readonly Func<JsonNode> s_defaultSettingsProvider = static () => new JsonObject();

	/// <summary>The largest timeout that timer-based cancellation accepts (the <see cref="CancellationTokenSource"/> limit).</summary>
	private static readonly TimeSpan s_maximumTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

	/// <summary>The shared default options instance.</summary>
	/// <remarks>
	/// A host overrides only the members it needs with a <c>with</c> expression.
	/// </remarks>
	public static LanguageServerClientOptions Default { get; } = new();

	private TimeSpan _initializeRequestTimeout = TimeSpan.FromSeconds(20.0);

	private TimeSpan _shutdownRequestTimeout = TimeSpan.FromSeconds(3.0);
	private TimeSpan _disposeWaitTimeout = TimeSpan.FromSeconds(5.0);

	private Func<JsonNode> _settingsProvider = s_defaultSettingsProvider;

	private Func<IReadOnlyList<string>, object?> _clientCapabilitiesProvider = static _ => new { };
	private Func<IReadOnlyList<string>, object?> _initializationOptionsProvider = static _ => new { };

	private Func<IReadOnlyList<string>, IReadOnlyList<SessionStartNotification>> _sessionStartNotificationsProvider = static _ => [];

	/// <summary>
	/// The settings document factory for <c>workspace/didChangeConfiguration</c>; defaults to a factory that returns an empty settings object.
	/// </summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	/// <remarks>
	/// <para>
	/// The client invokes this factory during the initialize handshake
	/// and caches the result; a configuration request that arrives before the handshake invokes it too. The cache is refreshed from the factory
	/// before the initialization push and before each <see cref="ILanguageServerClient.PushSettingsAsync"/> push, and from the outgoing payload
	/// whenever a <c>workspace/didChangeConfiguration</c> notification is sent through <see cref="ILanguageServerClient.SendNotificationAsync"/>.
	/// The same serialized element serves the configuration answers and the push, so the server
	/// receives the same settings on either channel. The client applies no naming policy to the document, so the factory spells every member
	/// exactly as the server expects it. The factory must return a non-<see langword="null"/> document; returning <see langword="null"/> fails
	/// the initialization handshake.
	/// </para>
	/// <para>
	/// It must be cheap and non-blocking. Other inbound requests and notifications wait until it returns, so a slow
	/// provider delays them. Responses to requests the client sent are not delayed.
	/// </para>
	/// </remarks>
	public Func<JsonNode> SettingsProvider
	{
		get => _settingsProvider;
		init => _settingsProvider = value ?? throw new ArgumentNullException(nameof(SettingsProvider));
	}

	/// <summary>The time startup waits for the server to answer the <c>initialize</c> request; defaults to 20 seconds.</summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The assigned value is zero, negative, infinite, or longer than the timer-based cancellation limit.
	/// </exception>
	public TimeSpan InitializeRequestTimeout
	{
		get => _initializeRequestTimeout;
		init => _initializeRequestTimeout = ValidateTimeout(value, nameof(InitializeRequestTimeout));
	}

	/// <summary>The time graceful shutdown waits for the server to answer the <c>shutdown</c> request; defaults to 3 seconds.</summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The assigned value is zero, negative, infinite, or longer than the timer-based cancellation limit.
	/// </exception>
	/// <remarks>
	/// A second budget of the same length starts when the graceful <c>exit</c> notification is dispatched, and it bounds that dispatch and the drain
	/// of the queued messages to the server's input before the connection is released.
	/// </remarks>
	public TimeSpan ShutdownRequestTimeout
	{
		get => _shutdownRequestTimeout;
		init => _shutdownRequestTimeout = ValidateTimeout(value, nameof(ShutdownRequestTimeout));
	}

	/// <summary>The time disposal waits for background connection work to quiesce before teardown continues; defaults to 5 seconds.</summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The assigned value is zero, negative, infinite, or longer than the timer-based cancellation limit.
	/// </exception>
	/// <remarks>
	/// The graceful shutdown request, and the exit notification with the drain of the queued messages, run inside the first
	/// teardown stage (the active session disposal), each bounded by <see cref="ShutdownRequestTimeout"/>. The budget bounds the whole disposal wait:
	/// a stage that outlives it is abandoned, and its teardown keeps running and still terminates a server that has not exited.
	/// Later stages that are still running are skipped with a warning once the budget is spent.
	/// </remarks>
	public TimeSpan DisposeWaitTimeout
	{
		get => _disposeWaitTimeout;
		init => _disposeWaitTimeout = ValidateTimeout(value, nameof(DisposeWaitTimeout));
	}

	/// <summary>
	/// The client capabilities payload factory for the <c>initialize</c> request; defaults to a factory that returns an empty object.
	/// </summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	/// <remarks>
	/// <para>
	/// The argument holds the normalized workspace root directory paths in caller order; the first entry is the primary root. This delegate may run
	/// on a background connection thread during startup. The client normalizes the payload to the capabilities it services:
	/// </para>
	/// <list type="bullet">
	/// <item>
	/// <description>
	/// rewrites <c>dynamicRegistration</c> to <see langword="false"/> on every capability object under <c>workspace</c> and <c>textDocument</c>,
	/// because it does not service dynamic registration;
	/// </description>
	/// </item>
	/// <item>
	/// <description>
	/// sets <c>window.workDoneProgress</c> to <see langword="true"/> when the client has a <see cref="LanguageServerClientHooks.Progress"/> hook,
	/// and forces it to <see langword="false"/> when it has none and the payload advertises it, because the client surfaces progress
	/// only through that hook;
	/// </description>
	/// </item>
	/// <item>
	/// <description>
	/// sets <c>workspace.applyEdit</c> to <see langword="true"/>
	/// when the client has an <see cref="LanguageServerClientHooks.ApplyWorkspaceEdit"/> hook, and forces it to <see langword="false"/>
	/// when it has none and the payload advertises it, because the client services <c>workspace/applyEdit</c> only through that hook;
	/// </description>
	/// </item>
	/// <item><description>pins <c>general.positionEncodings</c> to UTF-16, the only encoding its coordinate math implements;</description></item>
	/// <item>
	/// <description>
	/// forces <c>multilineTokenSupport</c> and <c>overlappingTokenSupport</c> to <see langword="false"/> when the payload advertises them.
	/// </description>
	/// </item>
	/// </list>
	/// <para>
	/// A factory that returns <see langword="null"/> is treated as an empty capabilities object, and a payload that does not serialize to a JSON
	/// object is replaced by an empty one with a logged warning.
	/// </para>
	/// </remarks>
	public Func<IReadOnlyList<string>, object?> ClientCapabilitiesProvider
	{
		get => _clientCapabilitiesProvider;
		init => _clientCapabilitiesProvider = value ?? throw new ArgumentNullException(nameof(ClientCapabilitiesProvider));
	}

	/// <summary>
	/// The language-specific initialization options factory for the <c>initialize</c> request; defaults to a factory that returns an empty object.
	/// </summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	/// <remarks>
	/// The argument holds the normalized workspace root directory paths in caller order; the first entry is the primary root. This delegate may run
	/// on a background connection thread during startup.
	/// </remarks>
	public Func<IReadOnlyList<string>, object?> InitializationOptionsProvider
	{
		get => _initializationOptionsProvider;
		init => _initializationOptionsProvider = value ?? throw new ArgumentNullException(nameof(InitializationOptionsProvider));
	}

	/// <summary>
	/// The factory of the notifications the client sends right after <c>initialized</c> on every session start, a restart included;
	/// defaults to a factory that returns none.
	/// </summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	/// <remarks>
	/// <para>
	/// The argument holds the normalized workspace root directory paths in caller order. The client sends the notifications
	/// in list order, before the settings push and before the session reports ready, so they reach the server ahead of every document notification.
	/// A server that loads its projects only when asked, such as the Roslyn language server with <c>project/open</c>, needs them.
	/// </para>
	/// <para>
	/// When the factory throws, or a notification's parameters do not serialize, the client logs a warning and sends none of the notifications
	/// of that start, and the start still completes. A <see langword="null"/> list or entry sends nothing for it. The factory runs on a background
	/// connection thread during startup.
	/// </para>
	/// </remarks>
	public Func<IReadOnlyList<string>, IReadOnlyList<SessionStartNotification>> SessionStartNotificationsProvider
	{
		get => _sessionStartNotificationsProvider;
		init => _sessionStartNotificationsProvider = value ?? throw new ArgumentNullException(nameof(SessionStartNotificationsProvider));
	}

	/// <summary>
	/// Whether startup fails when the server does not advertise full or incremental text synchronization; defaults to <see langword="true"/>.
	/// </summary>
	/// <remarks>
	/// A host that mirrors document contents to the server must not silently start against a server that will not accept
	/// its synchronization notifications; set this option to <see langword="false"/> to accept servers that do not support text synchronization
	/// at all.
	/// </remarks>
	public bool RequireTextDocumentSynchronization { get; init; } = true;

	private static TimeSpan ValidateTimeout(TimeSpan value, string propertyName)
	{
		if (value == Timeout.InfiniteTimeSpan)
			throw new ArgumentOutOfRangeException(propertyName, value, "Infinite timeouts are not supported for connection lifecycle operations.");

		if (value <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(propertyName, value, "The timeout must be greater than zero.");

		// Timer-based cancellation rejects delays above uint.MaxValue - 1 milliseconds; validating the same bound here
		// keeps an oversized value from failing every startup or teardown at the platform limit instead.
		if (value > s_maximumTimeout)
		{
			throw new ArgumentOutOfRangeException(
				propertyName,
				value,
				$"The timeout must not exceed {s_maximumTimeout.TotalDays:F1} days, the platform limit for timer-based cancellation."
			);
		}

		return value;
	}
}
