namespace Nickelony.LanguageServer.Client.Tests;

internal sealed class GatedWriteStream : Stream
{
	private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly TaskCompletionSource<bool> _writeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>A task that completes when the first connection write reaches this stream.</summary>
	public Task WriteStarted => _writeStarted.Task;

	public override bool CanRead => false;
	public override bool CanSeek => false;
	public override bool CanWrite => true;
	public override long Length => throw new NotSupportedException();

	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	public void Release() => _release.TrySetResult(true);

	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	public override void SetLength(long value) => throw new NotSupportedException();

	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();

	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
		new(WaitForReleaseAsync(cancellationToken));

	public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
		WaitForReleaseAsync(cancellationToken);

	public override void Flush() { }

	public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	private async Task WaitForReleaseAsync(CancellationToken cancellationToken)
	{
		_writeStarted.TrySetResult(true);

		if (_release.Task.IsCompleted)
			return;

		if (!cancellationToken.CanBeCanceled)
		{
			await _release.Task.ConfigureAwait(false);
			return;
		}

		await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
	}
}
