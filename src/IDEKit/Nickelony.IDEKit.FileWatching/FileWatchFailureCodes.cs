namespace Nickelony.IDEKit.FileWatching;

/// <summary>Lists the stable failure codes that a watch supervisor reports.</summary>
/// <remarks>
/// <see cref="FileWatchFailure.Code"/> carries one of these values. An owner branches on the code, not on a message, and maps it to the failure
/// vocabulary of its own package.
/// </remarks>
public static class FileWatchFailureCodes
{
	/// <summary>The watcher of a root could not start.</summary>
	public const string StartFailed = "StartFailed";

	/// <summary>A failed watcher could not be replaced.</summary>
	public const string RecoveryFailed = "RecoveryFailed";
}
