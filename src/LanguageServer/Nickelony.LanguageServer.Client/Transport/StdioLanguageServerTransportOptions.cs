using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace Nickelony.LanguageServer.Client;

/// <summary>Describes how <see cref="StdioLanguageServerTransport"/> launches the language-server process.</summary>
/// <remarks>
/// Record equality compares the collection members by reference and the factory by delegate equality, so two content-equal instances built from
/// equal-but-distinct collections are not equal.
/// </remarks>
/// <example>
/// <code>
/// var launch = StdioLanguageServerTransportOptions.Default with
/// {
///     ServerArguments = ["--stdio"],
///     EnvironmentVariables = new Dictionary&lt;string, string&gt; { ["LOG_LEVEL"] = "debug" }
/// };
/// </code>
/// </example>
public sealed record StdioLanguageServerTransportOptions
{
	// Frozen so the shared default cannot be mutated through an IReadOnlyDictionary cast, which would change the
	// environment variables of every transport that uses the default.
	private static readonly IReadOnlyDictionary<string, string> s_emptyEnvironmentVariables = FrozenDictionary<string, string>.Empty;

	/// <summary>The shared default options instance.</summary>
	/// <remarks>
	/// A host overrides only the members it needs with a <c>with</c> expression.
	/// </remarks>
	public static StdioLanguageServerTransportOptions Default { get; } = new();

	private IReadOnlyList<string> _serverArguments = [];
	private string? _serverWorkingDirectory;
	private IReadOnlyDictionary<string, string> _environmentVariables = s_emptyEnvironmentVariables;
	private StringComparer _environmentVariableNameComparer = StringComparer.Ordinal;

	/// <summary>
	/// The command-line arguments passed to the language-server process; the list is copied on assignment, and <see langword="null"/> means
	/// no arguments.
	/// </summary>
	/// <exception cref="ArgumentException">The assigned list contains a <see langword="null"/> entry.</exception>
	public IReadOnlyList<string> ServerArguments
	{
		get => _serverArguments;
		init => _serverArguments = value is null ? [] : ValidateServerArguments(value, nameof(ServerArguments));
	}

	/// <summary>
	/// The working directory for the language-server process, or <see langword="null"/> to use the directory containing the server executable
	/// (the process current directory when the executable path has no directory part).
	/// </summary>
	/// <exception cref="ArgumentException">The assigned value is empty or whitespace-only.</exception>
	/// <remarks>
	/// Servers that resolve configuration or support files relative to the workspace root need <see cref="ServerWorkingDirectory"/> set to that root,
	/// because the process default is the executable's own directory. The transport resolves the default on each connect.
	/// </remarks>
	public string? ServerWorkingDirectory
	{
		get => _serverWorkingDirectory;
		init => _serverWorkingDirectory = value is null ? null : ValidateServerWorkingDirectory(value, nameof(ServerWorkingDirectory));
	}

	/// <summary>The comparer for the environment-variable names when <see cref="EnvironmentVariables"/> is copied and validated.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// <see cref="EnvironmentVariables"/> was already assigned and contains two entries that compare equal under the new comparer.
	/// </exception>
	/// <remarks>
	/// The default is <see cref="StringComparer.Ordinal"/>, so the option stays host-neutral:
	/// two entries that differ only in casing are treated as distinct sibling variables. A host whose process environment is case-insensitive
	/// (for example Windows) assigns <see cref="StringComparer.OrdinalIgnoreCase"/> so such siblings are rejected instead of silently
	/// overwriting each other in the child process environment. This property and <see cref="EnvironmentVariables"/> are order-independent:
	/// assigning this comparer after <see cref="EnvironmentVariables"/> re-validates the already-copied entries, so an object initializer
	/// cannot change the duplicate-detection outcome by ordering the assignments.
	/// </remarks>
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
	/// Additional environment variables applied to the language-server process; the variables are added on top of the environment the process
	/// would otherwise inherit, and the dictionary is copied on assignment with <see langword="null"/> meaning none.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// The assigned dictionary contains two entries that compare equal under <see cref="EnvironmentVariableNameComparer"/>.
	/// </exception>
	/// <remarks>
	/// The copy uses <see cref="EnvironmentVariableNameComparer"/> for its key semantics, so with the default ordinal comparer the entries
	/// are preserved exactly as supplied.
	/// </remarks>
	public IReadOnlyDictionary<string, string> EnvironmentVariables
	{
		get => _environmentVariables;
		init => _environmentVariables = value is null ? s_emptyEnvironmentVariables : CopyEnvironmentVariables(value);
	}

	/// <summary>
	/// The factory that supplies the child-process lifetime guard the transport binds a spawned
	/// server process to; defaults to <see langword="null"/>, which selects the platform default (a shared kill-on-close job object on Windows,
	/// a documented no-op elsewhere).
	/// </summary>
	/// <remarks>
	/// Assign a factory to replace the platform default, for example to bind the child to a container or job the host already owns,
	/// or to install a guard with host-specific teardown semantics. The transport invokes the factory once per connect so it can return a fresh guard
	/// for each session.
	/// </remarks>
	public Func<IChildProcessLifetimeGuard>? ChildProcessLifetimeGuardFactory { get; init; }

	/// <summary>Copies and validates one server-argument list.</summary>
	/// <param name="value">The argument list to validate.</param>
	/// <param name="propertyName">The property name reported in the validation failure.</param>
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

	/// <summary>Validates one server working directory value.</summary>
	/// <param name="value">The directory path to validate.</param>
	/// <param name="propertyName">The property name used in the thrown exception.</param>
	/// <exception cref="ArgumentException">The value is empty or whitespace-only.</exception>
	private static string ValidateServerWorkingDirectory(string value, string propertyName)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new ArgumentException("The server working directory must not be empty or whitespace-only.", propertyName);

		return value;
	}

	/// <summary>
	/// Copies a caller-supplied environment dictionary with the configured comparer, rejecting sibling entries that compare equal under it.
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
}
