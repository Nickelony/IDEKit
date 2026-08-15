namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Provides the data for the <see cref="ILanguageServerIntelliSenseProvider.WorkspaceWatcherFailed"/> event.
/// </summary>
public sealed class WorkspaceWatcherFailedEventArgs : EventArgs
{
	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceWatcherFailedEventArgs"/> class.
	/// </summary>
	/// <param name="failure">The reported workspace watcher failure.</param>
	/// <exception cref="ArgumentNullException"><paramref name="failure"/> is <see langword="null"/>.</exception>
	public WorkspaceWatcherFailedEventArgs(WorkspaceWatcherFailure failure)
	{
		ArgumentNullException.ThrowIfNull(failure);

		Failure = failure;
	}

	/// <summary>
	/// Gets the reported workspace watcher failure.
	/// </summary>
	public WorkspaceWatcherFailure Failure { get; }
}
