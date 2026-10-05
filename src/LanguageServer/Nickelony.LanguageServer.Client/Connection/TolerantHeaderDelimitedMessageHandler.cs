using System.IO.Pipelines;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// A header-delimited JSON-RPC message handler that drops a frame the formatter cannot decode instead of faulting the whole connection.
/// </summary>
/// <remarks>
/// Only an <see cref="InvalidOperationException"/> from the base read is dropped; any other exception, including cancellation, propagates.
/// A run of consecutive decode failures is bounded, so a non-decode failure faults the connection instead of spinning. StreamJsonRpc otherwise treats
/// any decode failure as fatal, and the System.Text.Json formatter rejects a frame whose <c>params</c> member is neither a JSON array,
/// a JSON object nor <c>null</c>, which <see cref="TolerantSystemTextJsonFormatter"/> reads as absent.
/// </remarks>
internal sealed class TolerantHeaderDelimitedMessageHandler : HeaderDelimitedMessageHandler
{
	private const int MaxConsecutiveDecodeFailures = 32;

	private readonly ILogger _logger;

	/// <param name="writeStream">
	/// The stream the client writes messages to, which an <see cref="OutboundStreamForwarder"/> writes and leaves open, or <see langword="null"/>.
	/// </param>
	/// <param name="readStream">The stream the client reads messages from, which the handler leaves open, or <see langword="null"/>.</param>
	/// <param name="formatter">The formatter that serializes and deserializes messages.</param>
	/// <param name="connectionGeneration">The connection generation that the failure logs name.</param>
	/// <param name="logger">The logger that reports a dropped frame and a failed write.</param>
	internal TolerantHeaderDelimitedMessageHandler(
		Stream? writeStream,
		Stream? readStream,
		IJsonRpcMessageFormatter formatter,
		long connectionGeneration,
		ILogger logger
	)
		: this(
			outboundForwarder: writeStream is null ? null : new OutboundStreamForwarder(writeStream, connectionGeneration, logger),
			readStream,
			formatter,
			logger
		) { }

	private TolerantHeaderDelimitedMessageHandler(
		OutboundStreamForwarder? outboundForwarder,
		Stream? readStream,
		IJsonRpcMessageFormatter formatter,
		ILogger logger
	)
		: base(
			outboundForwarder?.Writer,
			readStream is null ? null : PipeReader.Create(readStream, new StreamPipeReaderOptions(leaveOpen: true)),
			formatter
		)
	{
		_logger = logger;
		OutboundDrained = outboundForwarder?.Start() ?? Task.CompletedTask;
	}

	/// <summary>The copy of the outbound forwarder, which teardown waits for before it releases the connection.</summary>
	/// <remarks>
	/// The disposal of the handler completes the forwarder's writer, so the copy ends once the queued messages reach the stream.
	/// </remarks>
	internal Task OutboundDrained { get; }

	/// <inheritdoc/>
	protected override async ValueTask<JsonRpcMessage?> ReadCoreAsync(CancellationToken cancellationToken)
	{
		int consecutiveFailures = 0;

		while (true)
		{
			try
			{
				return await base.ReadCoreAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (InvalidOperationException exception)
			{
				consecutiveFailures++;

				// A decode failure consumes the frame's bytes, so the reader makes progress and the counter resets on
				// the next successfully read frame. A run past the bound means the exception is not a per-frame decode
				// failure, so rethrow instead of looping forever.
				if (consecutiveFailures > MaxConsecutiveDecodeFailures)
					throw;

				// The frame could not be decoded (for example a "params" string). Its bytes are
				// already consumed, so skip it and continue with the next frame instead of faulting the connection.
				_logger.LogWarning(exception, "Discarded a malformed language server frame that could not be decoded; the connection stays open.");
			}
		}
	}
}
