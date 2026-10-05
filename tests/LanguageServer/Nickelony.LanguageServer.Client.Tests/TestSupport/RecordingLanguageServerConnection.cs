using System.Diagnostics;

namespace Nickelony.LanguageServer.Client.Tests;

internal sealed class RecordingLanguageServerConnection : ILanguageServerConnection
{
	private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

	private int _disposeCount;

	public RecordingLanguageServerConnection(Stream readStream, Stream writeStream, Process? process = null)
	{
		ReadStream = readStream;
		WriteStream = writeStream;
		Process = process;
	}

	public Stream ReadStream { get; }

	public Stream WriteStream { get; }

	public Process? Process { get; }

	public int DisposeCount => Volatile.Read(ref _disposeCount);

	/// <summary>A task that completes when the connection is disposed for the first time.</summary>
	public Task Disposed => _disposed.Task;

	public ValueTask DisposeAsync()
	{
		// The test streams stay owned by the test that created them, so only the disposal itself is recorded.
		Interlocked.Increment(ref _disposeCount);
		_disposed.TrySetResult();
		return ValueTask.CompletedTask;
	}
}
