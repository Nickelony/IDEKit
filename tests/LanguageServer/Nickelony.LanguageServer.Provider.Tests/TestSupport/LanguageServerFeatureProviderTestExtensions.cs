namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Reads provider state that tests drive framework seams with.</summary>
internal static class LanguageServerFeatureProviderTestExtensions
{
	/// <summary>Returns the current tracked snapshot for one document path.</summary>
	/// <param name="provider">The provider under test.</param>
	/// <param name="filePath">The local file path of the document.</param>
	/// <returns>The tracked snapshot, or <see langword="null"/> when the document is not tracked.</returns>
	internal static DocumentSnapshot? GetTrackedSnapshot(this LanguageServerFeatureProvider provider, string filePath) =>
		provider.Composition.Documents.GetDocumentSnapshot(filePath);

	/// <summary>
	/// Drives one explicit startup so the provider creates the watcher of a workspace root, then restores the client state and clears
	/// the sent-message log; it does nothing when the root's watcher already exists.
	/// </summary>
	/// <param name="provider">The provider under test, created over <paramref name="client"/> and <paramref name="watchers"/>.</param>
	/// <param name="client">The client of <paramref name="provider"/>.</param>
	/// <param name="watchers">The factory that records the watchers of <paramref name="provider"/>.</param>
	/// <param name="workspaceRootDirectoryPath">The workspace root whose watcher the startup creates.</param>
	/// <exception cref="InvalidOperationException">The startup created no watcher for the root.</exception>
	internal static async Task StartWorkspaceWatcherAsync(
		this LanguageServerFeatureProvider provider,
		FakeStatefulLanguageServerClient client,
		RecordingFileWatcherFactory watchers,
		string workspaceRootDirectoryPath
	)
	{
		if (watchers.CreationsFor(workspaceRootDirectoryPath).Count > 0)
			return;

		bool originalIsReady = client.IsReady;
		bool originalStartResult = client.StartResult;

		try
		{
			client.IsReady = true;
			client.StartResult = true;

			await provider
				.GetHoverAsync(Path.Combine(workspaceRootDirectoryPath, ".watcher-bootstrap.test"), "local bootstrap = true", new TextPosition(0, 0))
				.ConfigureAwait(false);
		}
		finally
		{
			client.IsReady = originalIsReady;
			client.StartResult = originalStartResult;
		}

		if (watchers.CreationsFor(workspaceRootDirectoryPath).Count == 0)
			throw new InvalidOperationException("The workspace file watcher factory did not run during the startup request.");

		client.ClearSentMessages();
	}
}
