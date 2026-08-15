using StreamJsonRpc;
using System.Diagnostics;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Stores all process and transport objects associated with one language-server transport generation.
/// </summary>
internal sealed class TransportSession
{
	private const int MaxRecentStandardErrorLines = 5;
	private const int MaxRecentStandardErrorLineLength = 200;
	private readonly object _recentStandardErrorSyncRoot = new();
	private readonly Queue<string> _recentStandardErrorLines = [];

	/// <summary>
	/// Initializes a new instance of the <see cref="TransportSession"/> class.
	/// </summary>
	/// <param name="generation">The monotonically increasing transport generation.</param>
	/// <param name="process">The owned language-server process, when available.</param>
	/// <param name="readStream">The stream the host reads server output from.</param>
	/// <param name="writeStream">The stream the host writes requests and notifications to (the server's standard input).</param>
	/// <param name="errorStream">The stream carrying the server's standard-error output, when available.</param>
	public TransportSession(long generation, Process? process, Stream readStream, Stream writeStream, Stream? errorStream = null)
	{
		Generation = generation;
		Process = process;
		ReadStream = readStream;
		WriteStream = writeStream;
		ErrorStream = errorStream;
	}

	/// <summary>
	/// Gets the transport generation this session was created for.
	/// </summary>
	public long Generation { get; }

	/// <summary>
	/// Gets the owned server process when the session is backed by a spawned process.
	/// </summary>
	public Process? Process { get; }

	/// <summary>
	/// Gets the stream used to read responses and notifications from the server (the server's standard output).
	/// </summary>
	public Stream ReadStream { get; }

	/// <summary>
	/// Gets the stream used to write requests and notifications to the server (the server's standard input).
	/// </summary>
	public Stream WriteStream { get; }

	/// <summary>
	/// Gets the stream carrying the server's standard-error output, or <see langword="null"/> when the session has
	/// no process.
	/// </summary>
	public Stream? ErrorStream { get; }

	/// <summary>
	/// Gets the transport connection this session was created from, or <see langword="null"/> when the session was
	/// built directly from raw streams.
	/// </summary>
	/// <remarks>
	/// A connection owns its streams and process, so teardown disposes it instead of the individual resources; a
	/// session without a connection owns the resources it was constructed with. The client also treats a session
	/// that has a connection but no process as a live protocol channel for the graceful shutdown sequence.
	/// </remarks>
	public ILanguageServerConnection? Connection { get; init; }

	/// <summary>
	/// Gets or sets the message handler bound to the transport streams.
	/// </summary>
	public HeaderDelimitedMessageHandler? MessageHandler { get; set; }

	/// <summary>
	/// Gets or sets the JSON-RPC instance driving the session.
	/// </summary>
	public JsonRpc? JsonRpc { get; set; }

	/// <summary>
	/// Gets or sets the callback target exposed to the language server.
	/// </summary>
	public ClientRpcTarget? RpcTarget { get; set; }

	/// <summary>
	/// Gets or sets the cached process-exited event handler for later detachment.
	/// </summary>
	public EventHandler? ProcessExitedHandler { get; set; }

	/// <summary>
	/// Gets or sets the JSON-RPC completion task for orderly shutdown.
	/// </summary>
	public Task? RpcCompletionTask { get; set; }

	/// <summary>
	/// Gets or sets the standard-error read loop task for orderly shutdown.
	/// </summary>
	public Task? StderrLoopTask { get; set; }

	/// <summary>
	/// Records one recent non-empty stderr line for failure diagnostics.
	/// </summary>
	/// <param name="line">The stderr line.</param>
	public void RecordStandardErrorLine(string line)
	{
		if (string.IsNullOrWhiteSpace(line))
			return;

		string trimmedLine = line.Trim();

		if (trimmedLine.Length > MaxRecentStandardErrorLineLength)
			trimmedLine = trimmedLine[..MaxRecentStandardErrorLineLength] + "...";

		lock (_recentStandardErrorSyncRoot)
		{
			_recentStandardErrorLines.Enqueue(trimmedLine);

			while (_recentStandardErrorLines.Count > MaxRecentStandardErrorLines)
				_recentStandardErrorLines.Dequeue();
		}
	}

	/// <summary>
	/// Gets a compact summary of recent stderr lines for failure logging.
	/// </summary>
	/// <returns>The recent stderr summary, or <see langword="null"/> when none was recorded.</returns>
	public string? GetRecentStandardErrorSummary()
	{
		lock (_recentStandardErrorSyncRoot)
		{
			if (_recentStandardErrorLines.Count == 0)
				return null;

			return string.Join(" | ", _recentStandardErrorLines);
		}
	}
}
