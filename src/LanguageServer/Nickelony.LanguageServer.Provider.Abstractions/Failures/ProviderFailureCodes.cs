namespace Nickelony.LanguageServer.Provider.Abstractions;

/// <summary>Lists the stable failure codes that a language-server provider reports through its failure events.</summary>
/// <remarks>
/// <see cref="StartupFailure.Code"/> and <see cref="WorkspaceFileWatchFailure.Code"/> carry one of these values. A host branches on the code,
/// not on the wording of the message, which a language package may change.
/// </remarks>
public static class ProviderFailureCodes
{
	/// <summary>A start attempt failed, whether the process did not start or the handshake did not complete.</summary>
	/// <remarks>
	/// <see cref="StartupFailure.IsPersistent"/> tells whether the provider stopped retrying.
	/// </remarks>
	public const string StartFailed = "StartFailed";

	/// <summary>The provider has no language-server client, so it can never start the server.</summary>
	public const string ClientUnavailable = "ClientUnavailable";

	/// <summary>The workspace file watcher could not be started.</summary>
	public const string FileWatchStartFailed = "FileWatchStartFailed";

	/// <summary>The workspace file watcher failed and the automatic restart did not recover it.</summary>
	public const string FileWatchRecoveryFailed = "FileWatchRecoveryFailed";
}
