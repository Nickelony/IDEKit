namespace Nickelony.LanguageServer.Provider.Abstractions;

/// <summary>Provides the data for the <c>ILanguageServerFeatureProvider.WorkspaceFileWatchFailed</c> event.</summary>
public sealed class WorkspaceFileWatchFailedEventArgs : EventArgs
{
	/// <exception cref="ArgumentNullException"><paramref name="failure"/> is <see langword="null"/>.</exception>
	public WorkspaceFileWatchFailedEventArgs(WorkspaceFileWatchFailure failure)
	{
		ArgumentNullException.ThrowIfNull(failure);
		Failure = failure;
	}

	public WorkspaceFileWatchFailure Failure { get; }
}
