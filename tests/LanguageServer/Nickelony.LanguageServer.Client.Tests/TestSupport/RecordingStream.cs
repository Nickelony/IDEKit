using System.Text;

namespace Nickelony.LanguageServer.Client.Tests;

internal sealed class RecordingStream : Stream
{
	private readonly object _syncRoot = new();
	private readonly MemoryStream _innerStream = new();

	public override bool CanRead => false;
	public override bool CanSeek => false;
	public override bool CanWrite => true;
	public override long Length => _innerStream.Length;

	public override long Position
	{
		get
		{
			lock (_syncRoot)
				return _innerStream.Position;
		}
		set
		{
			lock (_syncRoot)
				_innerStream.Position = value;
		}
	}

	public byte[] GetWrittenBytes()
	{
		lock (_syncRoot)
			return _innerStream.ToArray();
	}

	public string GetWrittenText() => Encoding.UTF8.GetString(GetWrittenBytes());

	public override void Flush()
	{
		lock (_syncRoot)
			_innerStream.Flush();
	}

	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	public override void SetLength(long value)
	{
		lock (_syncRoot)
			_innerStream.SetLength(value);
	}

	public override void Write(byte[] buffer, int offset, int count)
	{
		lock (_syncRoot)
			_innerStream.Write(buffer, offset, count);
	}

	public override void Write(ReadOnlySpan<byte> buffer)
	{
		lock (_syncRoot)
			_innerStream.Write(buffer);
	}

	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
	{
		lock (_syncRoot)
		{
			_innerStream.Write(buffer.Span);
			return ValueTask.CompletedTask;
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			lock (_syncRoot)
				_innerStream.Flush();
		}

		base.Dispose(disposing);
	}
}
