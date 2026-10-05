using System.Diagnostics;
using StreamJsonRpc;

namespace Nickelony.LanguageServer.Client;

/// <summary>Stores all process and connection objects associated with one language-server connection generation.</summary>
internal sealed class ConnectionSession
{
	private const int MaxRecentStandardErrorLines = 5;
	private const int MaxRecentStandardErrorLineLength = 200;

	private readonly object _recentStandardErrorSyncRoot = new();
	private readonly Queue<string> _recentStandardErrorLines = [];

	/// <param name="connectionGeneration">The monotonically increasing connection generation.</param>
	/// <param name="process">The owned language-server process, when available.</param>
	/// <param name="readStream">The stream the client reads server output from.</param>
	/// <param name="writeStream">The stream the client writes requests and notifications to (the server's standard input).</param>
	internal ConnectionSession(long connectionGeneration, Process? process, Stream readStream, Stream writeStream)
	{
		ConnectionGeneration = connectionGeneration;
		Process = process;
		ReadStream = readStream;
		WriteStream = writeStream;
	}

	/// <summary>The connection generation this session was created for.</summary>
	internal long ConnectionGeneration { get; }

	/// <summary>The owned server process when the session is backed by a spawned process.</summary>
	internal Process? Process { get; }

	/// <summary>Whether the owned server process has exited, which is <see langword="false"/> for a session without a process.</summary>
	internal bool HasProcessExited => Process is { HasExited: true };

	/// <summary>The stream for reading responses and notifications from the server (the server's standard output).</summary>
	internal Stream ReadStream { get; }

	/// <summary>The stream for writing requests and notifications to the server (the server's standard input).</summary>
	internal Stream WriteStream { get; }

	/// <summary>The connection this session was created from.</summary>
	/// <remarks>
	/// A connection owns its streams and process, so teardown disposes it instead of the individual resources. The client treats a session
	/// that has no process as a live protocol channel for the graceful shutdown sequence until the connection is disposed.
	/// </remarks>
	internal required ILanguageServerConnection Connection { get; init; }

	/// <summary>The message handler bound to the connection streams.</summary>
	internal TolerantHeaderDelimitedMessageHandler? MessageHandler { get; set; }

	/// <summary>The JSON-RPC instance driving the session.</summary>
	internal JsonRpc? JsonRpc { get; set; }

	/// <summary>The RPC target exposed to the language server.</summary>
	internal ClientRpcTarget? RpcTarget { get; set; }

	/// <summary>The cached process-exited event handler for later detachment.</summary>
	internal EventHandler? ProcessExitedHandler { get; set; }

	/// <summary>The JSON-RPC completion task for orderly shutdown.</summary>
	internal Task? RpcCompletionTask { get; set; }

	/// <summary>The standard-error read loop task for orderly shutdown.</summary>
	internal Task? StandardErrorLoopTask { get; set; }

	/// <summary>Records one recent non-empty standard-error line for failure diagnostics.</summary>
	/// <param name="line">The standard-error line.</param>
	internal void RecordStandardErrorLine(string line)
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

	/// <summary>Returns a compact summary of recent standard-error lines for failure logging.</summary>
	/// <returns>The recent standard-error summary, or <see langword="null"/> when none was recorded.</returns>
	internal string? GetRecentStandardErrorSummary()
	{
		lock (_recentStandardErrorSyncRoot)
		{
			if (_recentStandardErrorLines.Count == 0)
				return null;

			return string.Join(" | ", _recentStandardErrorLines);
		}
	}
}
