namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Describes a workspace file watcher failure that could not be started or recovered.
/// </summary>
/// <remarks>
/// The failure is reported to the consumer through
/// <see cref="ILanguageServerIntelliSenseProvider.WorkspaceWatcherFailed"/>. The provider is the sender;
/// <see cref="Message"/> is a human-readable description for display or logging. IntelliSense for open editor
/// files may remain available while external workspace changes are not forwarded.
/// </remarks>
public sealed record WorkspaceWatcherFailure
{
	/// <summary>
	/// Initializes a new instance of the <see cref="WorkspaceWatcherFailure"/> record.
	/// </summary>
	/// <param name="message">The human-readable description of the failure for display or logging.</param>
	/// <param name="workspaceRootDirectoryPath">
	/// The normalized workspace root directory path the failure applies to, or <see langword="null"/> when the
	/// failure is not tied to a specific root.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
	public WorkspaceWatcherFailure(string message, string? workspaceRootDirectoryPath = null)
	{
		ArgumentNullException.ThrowIfNull(message);

		Message = message;
		WorkspaceRootDirectoryPath = workspaceRootDirectoryPath;
	}

	/// <summary>
	/// Gets the human-readable description of the failure for display or logging.
	/// </summary>
	public string Message { get; }

	/// <summary>
	/// Gets the normalized workspace root directory path the failure applies to, or <see langword="null"/> when
	/// the failure is not tied to a specific root.
	/// </summary>
	public string? WorkspaceRootDirectoryPath { get; }
}
