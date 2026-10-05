namespace Nickelony.IDEKit.Workspace.FileSystem;

/// <summary>Runs a replace, a move or a delete again after a short delay while it fails with a sharing or lock violation.</summary>
/// <remarks>
/// A sharing or lock violation leaves both paths under their own names, which a process that held the file briefly, such as a scanner
/// or an indexer, causes. The retry is bounded, so an operation runs at most once more than the delays it waits for.
/// </remarks>
internal static class TransientFailureRetry
{
	// The Windows error codes ERROR_SHARING_VIOLATION, ERROR_LOCK_VIOLATION and ERROR_UNABLE_TO_REMOVE_REPLACED as HRESULT values.
	private const int SharingViolationHResult = unchecked((int)0x80070020);
	private const int LockViolationHResult = unchecked((int)0x80070021);
	private const int UnableToRemoveReplacedHResult = unchecked((int)0x80070497);

	// The delays before each further attempt of a replace or a move that failed transiently, so an operation runs
	// at most once more than this array holds delays.
	private static readonly TimeSpan[] Delays =
	[
		TimeSpan.FromMilliseconds(10),
		TimeSpan.FromMilliseconds(20),
		TimeSpan.FromMilliseconds(40),
		TimeSpan.FromMilliseconds(80),
	];

	// Runs a replace, a move or a delete, and runs it again after each delay of Delays while it
	// fails with a sharing or lock violation. The precondition check runs before each retry, so a path that another
	// writer changed during the delay is reported as the conflict it is instead of being overwritten, and the token
	// is observed after it, as before the first attempt. A step that has no precondition passes null. Returns null
	// when the operation ran, or the result of the precondition check that rejected a retry; the failure of the last
	// attempt propagates.
	internal static async Task<TResult?> RunAsync<TResult>(
		Action operation,
		Func<CancellationToken, Task<TResult?>>? checkPreconditionsAsync,
		CancellationToken cancellationToken
	)
		where TResult : class
	{
		for (int attempt = 0; ; attempt++)
		{
			try
			{
				operation();
				return null;
			}
			catch (IOException exception) when (attempt < Delays.Length && IsSharingFailure(exception))
			{
				// The failure left both paths under their own names, so the delay below precedes another attempt.
			}

			await Task.Delay(Delays[attempt], cancellationToken).ConfigureAwait(false);

			if (checkPreconditionsAsync is not null && await checkPreconditionsAsync(cancellationToken).ConfigureAwait(false) is { } rejected)
				return rejected;

			cancellationToken.ThrowIfCancellationRequested();
		}
	}

	// A sharing violation, a lock violation, or a File.Replace that could not remove the replaced file leaves both
	// files under their own names, which a process that held the file briefly, such as a scanner or an indexer,
	// causes. ERROR_UNABLE_TO_MOVE_REPLACEMENT and ERROR_UNABLE_TO_MOVE_REPLACEMENT_2 mean that File.Replace already
	// moved or removed the replaced file, so they are never retried.
	internal static bool IsSharingFailure(IOException exception) =>
		exception.HResult is SharingViolationHResult or LockViolationHResult or UnableToRemoveReplacedHResult;
}
