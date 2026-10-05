using System.Buffers;
using System.IO.Pipelines;

namespace Nickelony.LanguageServer.Client;

/// <summary>Copies the messages that a connection's message handler writes to an outbound pipe on to the connection's write stream.</summary>
/// <remarks>
/// It owns the pipe and the copy and leaves the write stream open, because the connection owns
/// the stream and its disposal releases it. A completed write only puts a message into the pipe, so teardown waits for the copy before it releases
/// the connection.
/// </remarks>
internal sealed class OutboundStreamForwarder
{
	private readonly Pipe _pipe = new();
	private readonly Stream _writeStream;
	private readonly long _connectionGeneration;
	private readonly ILogger _logger;

	/// <param name="writeStream">The stream the client writes messages to.</param>
	/// <param name="connectionGeneration">The connection generation that the failure log names.</param>
	/// <param name="logger">The logger that reports a failed write.</param>
	internal OutboundStreamForwarder(Stream writeStream, long connectionGeneration, ILogger logger)
	{
		_writeStream = writeStream;
		_connectionGeneration = connectionGeneration;
		_logger = logger;
	}

	/// <summary>The writing end of the outbound pipe, which the message handler writes and completes.</summary>
	internal PipeWriter Writer => _pipe.Writer;

	/// <summary>Starts copying the pipe to the write stream.</summary>
	/// <returns>The copy, which completes once the writer completes and every message reached the stream, or at the first failed write.</returns>
	/// <remarks>
	/// A failed write completes the pipe with its exception, so the next flush of the writer throws it instead of filling a pipe that nothing copies.
	/// </remarks>
	internal Task Start() => CopyAsync(_pipe.Reader);

	private async Task CopyAsync(PipeReader reader)
	{
		try
		{
			while (true)
			{
				ReadResult result = await reader.ReadAsync().ConfigureAwait(false);
				ReadOnlySequence<byte> buffer = result.Buffer;

				foreach (ReadOnlyMemory<byte> segment in buffer)
					await _writeStream.WriteAsync(segment).ConfigureAwait(false);

				if (!buffer.IsEmpty)
					await _writeStream.FlushAsync().ConfigureAwait(false);

				reader.AdvanceTo(buffer.End);

				if (result.IsCompleted)
					break;
			}

			await reader.CompleteAsync().ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			// The pipe completes before the log, so a logger that throws cannot leave the writer waiting on a dead copy.
			await reader.CompleteAsync(exception).ConfigureAwait(false);

			_logger.LogDebug(
				exception,
				"Writing to the input stream of language server connection generation {Generation} failed; later messages on this connection fail.",
				_connectionGeneration
			);
		}
	}
}
