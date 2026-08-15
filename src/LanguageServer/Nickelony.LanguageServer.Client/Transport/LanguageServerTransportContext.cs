namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Describes the language server that an <see cref="ILanguageServerTransport"/> is asked to connect to.
/// </summary>
/// <remarks>
/// The client builds one context per startup attempt from its constructor arguments and
/// <see cref="LanguageServerClientOptions"/>. The members mirror what the default stdio transport needs; a custom
/// transport may ignore the ones that do not apply to its channel.
/// </remarks>
public sealed record LanguageServerTransportContext
{
	/// <summary>
	/// Gets the language-server executable path supplied to the <see cref="LanguageServerClient"/>.
	/// </summary>
	public required string ServerExecutablePath { get; init; }

	/// <summary>
	/// Gets the command-line arguments configured through <see cref="LanguageServerClientOptions.ServerArguments"/>.
	/// </summary>
	public IReadOnlyList<string> ServerArguments { get; init; } = [];

	/// <summary>
	/// Gets the directory the server should be started in: the configured
	/// <see cref="LanguageServerClientOptions.ServerWorkingDirectory"/>, or the directory containing the server
	/// executable when the option is not set.
	/// </summary>
	public required string ServerWorkingDirectory { get; init; }

	/// <summary>
	/// Gets the additional environment variables configured through
	/// <see cref="LanguageServerClientOptions.EnvironmentVariables"/>, to be added on top of the environment the
	/// server would otherwise inherit.
	/// </summary>
	public required IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; }

	/// <summary>
	/// Gets the normalized workspace root directory paths in caller order; the first entry is the primary root.
	/// </summary>
	public required IReadOnlyList<string> WorkspaceRootDirectoryPaths { get; init; }

	/// <summary>
	/// Gets the child-process lifetime guard the transport binds a spawned server process to, or <see langword="null"/> to
	/// use the platform default.
	/// </summary>
	/// <remarks>
	/// The client resolves this from <see cref="LanguageServerClientOptions.ChildProcessLifetimeGuardFactory"/> once per
	/// startup attempt. A transport that does not launch a process is free to ignore it.
	/// </remarks>
	public IChildProcessLifetimeGuard? ChildProcessLifetimeGuard { get; init; }

	/// <summary>
	/// Gets the logger used for transport-level diagnostics.
	/// </summary>
	public required ILogger Logger { get; init; }
}
