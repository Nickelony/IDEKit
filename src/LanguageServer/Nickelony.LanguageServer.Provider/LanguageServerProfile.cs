namespace Nickelony.LanguageServer.Provider;

/// <summary>The data and rules that make <c>LanguageServerFeatureProvider</c> serve one language.</summary>
/// <remarks>
/// <para>
/// A language package ships a factory that returns a profile, and the host hands the profile to the provider with its workspace roots
/// and the server executable path. The names appear in the provider's log and diagnostic text, and the failure messages default to sentences built
/// from them.
/// </para>
/// <para>
/// The profile validates its values as they are assigned and copies its lists. The provider checks each watch specification when it is constructed.
/// </para>
/// </remarks>
public sealed record LanguageServerProfile
{
	private readonly string _languageId = string.Empty;
	private readonly string _languageDisplayName = string.Empty;
	private readonly string _serverDisplayName = string.Empty;

	private readonly IReadOnlyList<FileWatchSpecification> _watchSpecifications = [];
	private readonly StdioLanguageServerTransportOptions _launch = StdioLanguageServerTransportOptions.Default;
	private readonly LanguageServerProfileHooks _hooks = LanguageServerProfileHooks.Default;
	private readonly LanguageServerClientCapabilitiesOptions _clientCapabilities = LanguageServerClientCapabilitiesOptions.Default;

	private readonly string? _transientStartupFailureMessage;
	private readonly string? _persistentStartupFailureMessage;
	private readonly string? _missingClientFailureMessage;

	/// <summary>The language identifier sent as the <c>languageId</c> of opened documents, for example <c>"lua"</c>.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The value is empty or whitespace-only.</exception>
	public required string LanguageId
	{
		get => _languageId;
		init
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(value);
			_languageId = value;
		}
	}

	/// <summary>The name of the language in log and diagnostic text, for example <c>"Lua"</c>, <c>"C#"</c> or <c>"Visual Basic"</c>.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The value is empty or whitespace-only.</exception>
	public required string LanguageDisplayName
	{
		get => _languageDisplayName;
		init
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(value);
			_languageDisplayName = value;
		}
	}

	/// <summary>The name of the language server in log and diagnostic text, for example <c>"Lua language server"</c>.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The value is empty or whitespace-only.</exception>
	public required string ServerDisplayName
	{
		get => _serverDisplayName;
		init
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(value);
			_serverDisplayName = value;
		}
	}

	/// <summary>
	/// The file patterns mirrored to the language server and tracked for recovery snapshots, or an empty list to disable workspace watching.
	/// The accepted pattern grammar is defined by <see cref="FileWatchSpecification"/>.
	/// </summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	public required IReadOnlyList<FileWatchSpecification> WatchSpecifications
	{
		get => _watchSpecifications;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_watchSpecifications = Array.AsReadOnly([.. value]);
		}
	}

	/// <summary>The rules of the language that only code can express; <see cref="LanguageServerProfileHooks.Default"/> when unset.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	public LanguageServerProfileHooks Hooks
	{
		get => _hooks;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_hooks = value;
		}
	}

	/// <summary>
	/// The diagnostic-mapping rules of the language, or <see langword="null"/> to expose no diagnostics: the provider then drops every published
	/// or pulled payload.
	/// </summary>
	/// <remarks>
	/// A provider that needs different rules overrides the individual values of <see cref="DiagnosticMappingPolicy.Default"/>.
	/// A delegate of the policy that throws is contained, and the payload it was read for is dropped.
	/// </remarks>
	public DiagnosticMappingPolicy? DiagnosticMappingPolicy { get; init; }

	/// <summary>The client capabilities that the language server needs beyond the shared baseline, for a client that the provider creates.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	public LanguageServerClientCapabilitiesOptions ClientCapabilities
	{
		get => _clientCapabilities;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_clientCapabilities = value;
		}
	}

	/// <summary>
	/// The launch settings of the language-server process, for a client that the provider creates;
	/// <see cref="StdioLanguageServerTransportOptions.Default"/> by default.
	/// </summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	/// <remarks>
	/// A language package states its default arguments here, and a host overrides one setting with <c>with</c>,
	/// for example <c>profile with { Launch = profile.Launch with { EnvironmentVariables = variables } }</c>, so the package's arguments stay
	/// in place.
	/// </remarks>
	public StdioLanguageServerTransportOptions Launch
	{
		get => _launch;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_launch = value;
		}
	}

	/// <summary>The message of the startup-failure report for a transient failure, which the provider retries.</summary>
	/// <remarks>
	/// A blank value suppresses the report. The default names <see cref="ServerDisplayName"/> and <see cref="LanguageDisplayName"/> and tells
	/// the host that the provider retries when the language features are requested again.
	/// </remarks>
	public string? TransientStartupFailureMessage
	{
		get =>
			_transientStartupFailureMessage
			?? $"The configured {ServerDisplayName} failed to start, so {LanguageDisplayName} language features stay unavailable until a start succeeds. The provider retries automatically when {LanguageDisplayName} language features are requested again.";
		init => _transientStartupFailureMessage = value;
	}

	/// <summary>The message of the startup-failure report for a persistent failure, after which the provider stays failed.</summary>
	/// <remarks>
	/// A blank value suppresses the report. The default names <see cref="ServerDisplayName"/> and <see cref="LanguageDisplayName"/>.
	/// </remarks>
	public string? PersistentStartupFailureMessage
	{
		get =>
			_persistentStartupFailureMessage
			?? $"The configured {ServerDisplayName} failed to start repeatedly, so {LanguageDisplayName} language features are now disabled until the provider is recreated.";
		init => _persistentStartupFailureMessage = value;
	}

	/// <summary>The message of the persistent startup-failure report for a provider that has no language-server client.</summary>
	/// <remarks>
	/// A blank value suppresses the report. The default names <see cref="ServerDisplayName"/> and <see cref="LanguageDisplayName"/>.
	/// </remarks>
	public string? MissingClientFailureMessage
	{
		get =>
			_missingClientFailureMessage
			?? $"The {ServerDisplayName} executable is unavailable, so {LanguageDisplayName} language features are disabled until the provider is recreated with a valid executable path.";
		init => _missingClientFailureMessage = value;
	}
}
