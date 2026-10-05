using System.Diagnostics;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Connection whose disposal signals when it starts and then waits for a release, so a cleanup can be held open while another asks to run.
/// </summary>
internal sealed class GatedLanguageServerConnection : ILanguageServerConnection
{
	private readonly Task _release;
	private readonly TaskCompletionSource<bool> _disposeStarted;

	private int _disposeCount;

	public GatedLanguageServerConnection(Task release, TaskCompletionSource<bool> disposeStarted)
	{
		_release = release;
		_disposeStarted = disposeStarted;
	}

	public Stream ReadStream => Stream.Null;

	public Stream WriteStream => Stream.Null;

	public Process? Process => null;

	public int DisposeCount => Volatile.Read(ref _disposeCount);

	public async ValueTask DisposeAsync()
	{
		_disposeStarted.TrySetResult(true);
		await _release.ConfigureAwait(false);
		Interlocked.Increment(ref _disposeCount);
	}
}
