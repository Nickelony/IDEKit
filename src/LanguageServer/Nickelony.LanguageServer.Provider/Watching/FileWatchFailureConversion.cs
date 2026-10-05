namespace Nickelony.LanguageServer.Provider;

/// <summary>Converts a failure of the watching engine to the failure payload that a host receives.</summary>
internal static class FileWatchFailureConversion
{
	/// <summary>Converts a watcher failure, adding the provider's host-neutral wording and failure code.</summary>
	/// <param name="failure">The failure that a watch supervisor reported.</param>
	/// <param name="providerDisplayName">The provider display name used in the message.</param>
	/// <returns>The failure payload of the provider.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The code of <paramref name="failure"/> is not a <see cref="FileWatchFailureCodes"/> value.
	/// </exception>
	internal static WorkspaceFileWatchFailure ToWorkspaceFileWatchFailure(FileWatchFailure failure, string providerDisplayName)
	{
		string root = failure.WorkspaceRootDirectoryPath;

		(string code, string firstSentence) = failure.Code switch
		{
			FileWatchFailureCodes.StartFailed => (
				ProviderFailureCodes.FileWatchStartFailed,
				$"The {providerDisplayName} workspace file watcher could not be started for the workspace root '{root}'."
			),

			FileWatchFailureCodes.RecoveryFailed => (
				ProviderFailureCodes.FileWatchRecoveryFailed,
				$"The {providerDisplayName} workspace file watcher encountered an internal error and automatic recovery failed for the workspace root '{root}'."
			),

			_ => throw new ArgumentOutOfRangeException(nameof(failure), failure.Code, "The failure code is not a known code."),
		};

		string message =
			$"{firstSentence}\n\n{providerDisplayName} language features will continue to work for documents synchronized through this provider, but external workspace changes may not be forwarded until the watcher is available again.";

		return new WorkspaceFileWatchFailure(code, message, workspaceRootDirectoryPath: root, exception: failure.Exception);
	}
}
