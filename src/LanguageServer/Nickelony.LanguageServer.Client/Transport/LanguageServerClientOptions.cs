using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Describes the payload factories, server process configuration, and lifecycle timeouts used to initialize a language-server client.
/// </summary>
/// <remarks>
/// <para>
/// The payload factories may be invoked from background transport threads and should be thread-safe,
/// non-blocking, and cheap to execute.
/// </para>
/// <para>
/// Record equality compares the collection, delegate, and transport members by reference, so two
/// content-equal instances built from equal-but-distinct collections are not equal. Compare the configuration
/// explicitly when a cache needs to react to option changes.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var options = LanguageServerClientOptions.Default with
/// {
///     SettingsProvider = () =&gt; new { maxPreload = 10 },
///     InitializeTimeout = TimeSpan.FromSeconds(20)
/// };
/// </code>
/// </example>
public sealed record LanguageServerClientOptions
{
	// Frozen so the shared default cannot be mutated through an IReadOnlyDictionary cast, which would change the
	// environment variables of every client that uses the default.
	private static readonly IReadOnlyDictionary<string, string> s_emptyEnvironmentVariables = FrozenDictionary<string, string>.Empty;

	/// <summary>
	/// The settings provider used when the host passes none: an empty settings payload.
	/// </summary>
	private static readonly Func<object> s_defaultSettingsProvider = static () => new { };

	/// <summary>
	/// The largest timeout that timer-based cancellation accepts (the <see cref="CancellationTokenSource"/> limit).
	/// </summary>
	private static readonly TimeSpan s_maximumTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

	/// <summary>
	/// Gets the shared default options instance: an empty settings payload, no server arguments or
	/// environment variables, no working directory, the default timeouts, and the stdio transport. A host
	/// overrides only the members it needs with a <c>with</c> expression.
	/// </summary>
	public static LanguageServerClientOptions Default { get; } = new();

	private TimeSpan _initializeTimeout = TimeSpan.FromSeconds(20.0);
	private IReadOnlyList<string> _serverArguments = [];
	private string? _serverWorkingDirectory;
	private IReadOnlyDictionary<string, string> _environmentVariables = s_emptyEnvironmentVariables;
	private StringComparer _environmentVariableNameComparer = StringComparer.Ordinal;
	private TimeSpan _shutdownRequestTimeout = TimeSpan.FromSeconds(3.0);
	private TimeSpan _disposeWaitTimeout = TimeSpan.FromSeconds(5.0);
	private Func<object> _settingsProvider = s_defaultSettingsProvider;
	private Func<IReadOnlyList<string>, object?> _clientCapabilitiesProvider = static _ => new { };
	private Func<IReadOnlyList<string>, object?> _initializationOptionsProvider = static _ => new { };
	private ILanguageServerTransport _transport = StdioLanguageServerTransport.Default;

	/// <summary>
	/// Gets the settings payload factory for <c>workspace/didChangeConfiguration</c>.
	/// Defaults to a factory that returns an empty settings payload.
	/// </summary>
	/// <remarks>
	/// The client invokes this factory lazily and caches the resulting snapshot for <c>workspace/configuration</c>
	/// callbacks. The cache is refreshed from the factory before the initialization push and from the outgoing
	/// payload whenever a <c>workspace/didChangeConfiguration</c> notification is sent through this client. Both
	/// channels are served from the same serialized element, so the same member casing reaches the server on either
	/// channel. The factory must return a non-null payload; returning <see langword="null"/> fails the initialization
	/// handshake.
	/// <para>
	/// The factory runs synchronously on the JSON-RPC read loop while the cached snapshot is built, and the same
	/// serialized element serves the configuration callback. It must therefore be cheap and non-blocking and must
	/// not call back into this client: a slow provider stalls all inbound protocol traffic, and a provider that
	/// synchronously calls the client risks a cross-thread deadlock.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public Func<object> SettingsProvider
	{
		get => _settingsProvider;
		init => _settingsProvider = value ?? throw new ArgumentNullException(nameof(SettingsProvider));
	}

	/// <summary>
	/// Gets or initializes how long startup waits for the server to answer the <c>initialize</c> request.
	/// Defaults to 20 seconds.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The assigned value is not a positive finite duration; infinite timeouts are not supported.
	/// </exception>
	public TimeSpan InitializeTimeout
	{
		get => _initializeTimeout;
		init => _initializeTimeout = ValidateTimeout(value, nameof(InitializeTimeout));
	}

	/// <summary>
	/// Gets or initializes how long graceful shutdown waits for the server to answer the <c>shutdown</c> request.
	/// Defaults to 3 seconds. The same budget bounds the graceful <c>exit</c> notification dispatch.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The assigned value is not a positive finite duration; infinite timeouts are not supported.
	/// </exception>
	public TimeSpan ShutdownRequestTimeout
	{
		get => _shutdownRequestTimeout;
		init => _shutdownRequestTimeout = ValidateTimeout(value, nameof(ShutdownRequestTimeout));
	}

	/// <summary>
	/// Gets or initializes how long disposal waits for background transport work to quiesce before teardown continues.
	/// Defaults to 5 seconds; the budget is shared by all teardown stages of a single disposal.
	/// </summary>
	/// <remarks>
	/// The graceful shutdown request and the exit notification run inside the first teardown stage (the active
	/// session disposal), each bounded by <see cref="ShutdownRequestTimeout"/>, so one disposal attempt takes at
	/// most that stage's shutdown phases followed by the shared disposal budget. The session teardown task keeps
	/// running after the budget abandons the wait - it forces process termination as its last step - and later
	/// teardown stages are skipped, with a warning, once the shared budget is exhausted.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The assigned value is not a positive finite duration; infinite timeouts are not supported.
	/// </exception>
	public TimeSpan DisposeWaitTimeout
	{
		get => _disposeWaitTimeout;
		init => _disposeWaitTimeout = ValidateTimeout(value, nameof(DisposeWaitTimeout));
	}

	/// <summary>
	/// Gets or initializes the client capabilities payload factory for the <c>initialize</c> request.
	/// Defaults to a factory that returns an empty object.
	/// </summary>
	/// <remarks>
	/// The argument holds the normalized workspace root directory paths in caller order; the first entry is the
	/// primary root. This delegate may run on a background transport thread during startup. The client rewrites
	/// <c>dynamicRegistration</c> to <see langword="false"/> on every capability object under <c>workspace</c> and
	/// <c>textDocument</c> because it does not service dynamic registration, forces <c>window.workDoneProgress</c>
	/// to <see langword="false"/> when the payload advertises it because it has no client-side progress sink,
	/// forces <c>workspace.applyEdit</c> to <see langword="false"/> when the payload advertises it because it does
	/// not service <c>workspace/applyEdit</c>, pins <c>general.positionEncodings</c> to UTF-16 (the only encoding
	/// its coordinate math implements), forces <c>multilineTokenSupport</c> and <c>overlappingTokenSupport</c> to
	/// <see langword="false"/> when the payload advertises them, and a factory that returns <see langword="null"/>
	/// is treated as an empty capabilities object.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public Func<IReadOnlyList<string>, object?> ClientCapabilitiesProvider
	{
		get => _clientCapabilitiesProvider;
		init => _clientCapabilitiesProvider = value ?? throw new ArgumentNullException(nameof(ClientCapabilitiesProvider));
	}

	/// <summary>
	/// Gets or initializes the language-specific initialization options factory for the <c>initialize</c> request.
	/// Defaults to a factory that returns an empty object.
	/// </summary>
	/// <remarks>
	/// The argument holds the normalized workspace root directory paths in caller order; the first entry is the
	/// primary root. This delegate may run on a background transport thread during startup.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public Func<IReadOnlyList<string>, object?> InitializationOptionsProvider
	{
		get => _initializationOptionsProvider;
		init => _initializationOptionsProvider = value ?? throw new ArgumentNullException(nameof(InitializationOptionsProvider));
	}

	/// <summary>
	/// Gets or initializes the transport that opens the connection to the language server.
	/// Defaults to <see cref="StdioLanguageServerTransport.Default"/>, which launches
	/// <see langword="serverExecutablePath"/> as a child process and speaks the protocol over its standard streams.
	/// </summary>
	/// <remarks>
	/// Assign a custom transport to connect over a different channel, for example to a server process the host
	/// already owns, a local socket, or an in-memory channel. The executable path, arguments, working directory, and
	/// environment variables configured here are reported to every transport through
	/// <see cref="LanguageServerTransportContext"/>; a transport that does not launch a process is free to ignore
	/// them.
	/// </remarks>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public ILanguageServerTransport Transport
	{
		get => _transport;
		init => _transport = value ?? throw new ArgumentNullException(nameof(Transport));
	}

	/// <summary>
	/// Gets or initializes whether startup fails when the server does not advertise full or incremental text synchronization.
	/// Defaults to <see langword="true"/>.
	/// </summary>
	/// <remarks>
	/// A host that mirrors documents through the tracked-document store must not silently start against a server that
	/// will not accept its synchronization notifications; set this option to <see langword="false"/> to accept servers
	/// that do not support text synchronization at all.
	/// </remarks>
	public bool RequireTextDocumentSynchronization { get; init; } = true;

	/// <summary>
	/// Gets or initializes the factory that supplies the child-process lifetime guard the transport uses to bind a
	/// spawned server process. Defaults to <see langword="null"/>, which selects the platform default (a shared
	/// kill-on-close job object on Windows, a documented no-op elsewhere).
	/// </summary>
	/// <remarks>
	/// Assign a factory to replace the platform default, for example to bind the child to a container or job the host
	/// already owns, or to install a guard with host-specific teardown semantics. The factory is invoked once per
	/// startup attempt so it can return a fresh guard for each session.
	/// </remarks>
	public Func<IChildProcessLifetimeGuard>? ChildProcessLifetimeGuardFactory { get; init; }

	/// <summary>
	/// Gets or initializes the command-line arguments passed to the language-server process.
	/// The list is copied on assignment; <see langword="null"/> means no arguments.
	/// </summary>
	/// <exception cref="ArgumentException">The assigned list contains a <see langword="null"/> entry.</exception>
	public IReadOnlyList<string> ServerArguments
	{
		get => _serverArguments;
		init => _serverArguments = value is null ? [] : ValidateServerArguments(value, nameof(ServerArguments));
	}

	/// <summary>
	/// Copies and validates one server-argument list.
	/// </summary>
	/// <param name="value">The argument list to validate.</param>
	/// <param name="propertyName">The property name reported in the validation failure.</param>
	/// <returns>The validated read-only argument list.</returns>
	/// <exception cref="ArgumentException">The list contains a <see langword="null"/> entry.</exception>
	private static ReadOnlyCollection<string> ValidateServerArguments(IReadOnlyList<string> value, string propertyName)
	{
		string[] arguments = [.. value];

		for (int i = 0; i < arguments.Length; i++)
		{
			if (arguments[i] is null)
				throw new ArgumentException("Server arguments must not contain null entries.", propertyName);
		}

		return Array.AsReadOnly(arguments);
	}

	/// <summary>
	/// Gets or initializes the working directory for the language-server process, or <see langword="null"/> to use
	/// the directory containing the server executable.
	/// </summary>
	/// <remarks>
	/// Servers that resolve configuration or support files relative to the workspace root need
	/// <see cref="ServerWorkingDirectory"/> set to that root, because the process default is the executable's own
	/// directory.
	/// </remarks>
	/// <exception cref="ArgumentException">The assigned value is empty or whitespace-only.</exception>
	public string? ServerWorkingDirectory
	{
		get => _serverWorkingDirectory;
		init => _serverWorkingDirectory = value is null ? null : ValidateServerWorkingDirectory(value, nameof(ServerWorkingDirectory));
	}

	/// <summary>
	/// Validates one server working directory value.
	/// </summary>
	/// <param name="value">The directory path to validate.</param>
	/// <param name="propertyName">The property name used in the thrown exception.</param>
	/// <returns>The validated directory path.</returns>
	/// <exception cref="ArgumentException">The value is empty or whitespace-only.</exception>
	private static string ValidateServerWorkingDirectory(string value, string propertyName)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new ArgumentException("The server working directory must not be empty or whitespace-only.", propertyName);

		return value;
	}

	/// <summary>
	/// Gets or initializes the comparer used for the environment-variable names when
	/// <see cref="EnvironmentVariables"/> is copied and validated.
	/// </summary>
	/// <remarks>
	/// The default is <see cref="StringComparer.Ordinal"/>, so the option stays host-neutral: two entries that
	/// differ only in casing are treated as distinct sibling variables. A host whose process environment is
	/// case-insensitive (for example Windows) assigns <see cref="StringComparer.OrdinalIgnoreCase"/> so such
	/// siblings are rejected instead of silently overwriting each other in the child process environment.
	/// The two properties are order-independent: assigning this comparer after <see cref="EnvironmentVariables"/>
	/// re-validates the already-copied entries, so an object initializer cannot change the duplicate-detection
	/// outcome by ordering the two assignments.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <see cref="EnvironmentVariables"/> was already assigned and contains two entries that compare equal under
	/// the new comparer.
	/// </exception>
	public StringComparer EnvironmentVariableNameComparer
	{
		get => _environmentVariableNameComparer;
		init
		{
			ArgumentNullException.ThrowIfNull(value);

			_environmentVariableNameComparer = value;

			// Re-validate any already-assigned variables under the new comparer so the property order in an object
			// initializer cannot change the duplicate-detection outcome.
			if (_environmentVariables.Count > 0)
				_environmentVariables = CopyEnvironmentVariables(_environmentVariables);
		}
	}

	/// <summary>
	/// Gets or initializes additional environment variables applied to the language-server process.
	/// The variables are added on top of the environment the process would otherwise inherit;
	/// the dictionary is copied on assignment and <see langword="null"/> means none.
	/// </summary>
	/// <remarks>
	/// The copy uses <see cref="EnvironmentVariableNameComparer"/> for its key semantics, so with the default
	/// ordinal comparer the entries are preserved exactly as supplied.
	/// </remarks>
	/// <exception cref="ArgumentException">
	/// The assigned dictionary contains two entries that compare equal under
	/// <see cref="EnvironmentVariableNameComparer"/>.
	/// </exception>
	public IReadOnlyDictionary<string, string> EnvironmentVariables
	{
		get => _environmentVariables;
		init => _environmentVariables = value is null ? s_emptyEnvironmentVariables : CopyEnvironmentVariables(value);
	}

	/// <summary>
	/// Copies a caller-supplied environment dictionary with the configured comparer, rejecting sibling entries
	/// that compare equal under it.
	/// </summary>
	/// <param name="value">The environment dictionary to copy.</param>
	/// <returns>A frozen copy whose key semantics follow <see cref="EnvironmentVariableNameComparer"/>.</returns>
	/// <exception cref="ArgumentException">
	/// <paramref name="value"/> contains two entries that compare equal under the configured comparer.
	/// </exception>
	private FrozenDictionary<string, string> CopyEnvironmentVariables(IReadOnlyDictionary<string, string> value)
	{
		// The Dictionary copy rejects sibling entries that compare equal under the configured comparer; freezing
		// the validated copy makes the IReadOnlyDictionary guarantee real, so a host cannot mutate the option
		// through a downcast.
		StringComparer comparer = _environmentVariableNameComparer;

		return new Dictionary<string, string>(value, comparer).ToFrozenDictionary(comparer);
	}

	private static TimeSpan ValidateTimeout(TimeSpan value, string propertyName)
	{
		if (value == Timeout.InfiniteTimeSpan)
			throw new ArgumentOutOfRangeException(propertyName, value, "Infinite timeouts are not supported for transport lifecycle operations.");

		if (value <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(propertyName, value, "The timeout must be greater than zero.");

		// Timer-based cancellation rejects delays above uint.MaxValue - 1 milliseconds; validating the same bound here
		// keeps an oversized value from failing every startup or teardown at the platform limit instead.
		if (value > s_maximumTimeout)
		{
			throw new ArgumentOutOfRangeException(propertyName, value,
				$"The timeout must not exceed {s_maximumTimeout.TotalDays:F1} days, the platform limit for timer-based cancellation.");
		}

		return value;
	}
}
