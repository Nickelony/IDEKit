namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Test provider with workspace watching disabled through an empty watch-specification list.
/// </summary>
internal sealed class TestDisabledWatchingProvider : TestLanguageServerProvider
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TestDisabledWatchingProvider"/> class.
	/// </summary>
	/// <param name="workspaceRootDirectoryPaths">The workspace root directories.</param>
	/// <param name="client">The language-server client, or <see langword="null"/> when unavailable.</param>
	/// <param name="options">The provider tunables, or <see langword="null"/> for the defaults.</param>
	/// <param name="workspaceFileWatcherFactory">A workspace file watcher factory used to prove that watching is disabled.</param>
	public TestDisabledWatchingProvider(
		IReadOnlyList<string> workspaceRootDirectoryPaths,
		ILanguageServerClient? client,
		LanguageServerProviderOptions? options = null,
		WorkspaceFileWatcherFactory? workspaceFileWatcherFactory = null)
		: base(workspaceRootDirectoryPaths, client, [], options, workspaceFileWatcherFactory)
	{ }
}
