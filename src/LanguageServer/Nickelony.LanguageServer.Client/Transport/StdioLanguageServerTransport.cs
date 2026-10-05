using System.Diagnostics;
using System.Text;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Connects to a language server by launching it as a child process and speaking the protocol over its standard input and output streams.
/// </summary>
/// <remarks>
/// <para>
/// On Windows the child is additionally bound to the host process through a job object as a best-effort guarantee that it cannot outlive
/// a crashed host.
/// </para>
/// <para>
/// The instance holds only its launch settings, so one instance can serve any number of clients, and every connect launches a new process.
/// </para>
/// </remarks>
public sealed class StdioLanguageServerTransport : ILanguageServerTransport
{
	private readonly string _serverExecutablePath;
	private readonly StdioLanguageServerTransportOptions _options;

	/// <param name="serverExecutablePath">The language-server executable path.</param>
	/// <param name="options">The launch settings, or <see langword="null"/> for <see cref="StdioLanguageServerTransportOptions.Default"/>.</param>
	/// <exception cref="ArgumentNullException"><paramref name="serverExecutablePath"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="serverExecutablePath"/> is empty or whitespace-only.</exception>
	public StdioLanguageServerTransport(string serverExecutablePath, StdioLanguageServerTransportOptions? options = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(serverExecutablePath);

		_serverExecutablePath = serverExecutablePath;
		_options = options ?? StdioLanguageServerTransportOptions.Default;
	}

	/// <inheritdoc/>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="OperationCanceledException">
	/// <paramref name="cancellationToken"/> is canceled before the server process is launched.
	/// </exception>
	/// <exception cref="InvalidOperationException">The server process could not be started.</exception>
	/// <remarks>
	/// Each call resolves the working directory and invokes the guard factory of the options. An exception thrown while launching the process
	/// propagates after the process object is disposed.
	/// </remarks>
	public Task<ILanguageServerConnection> ConnectAsync(LanguageServerConnectRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		cancellationToken.ThrowIfCancellationRequested();

		var startInfo = new ProcessStartInfo
		{
			FileName = _serverExecutablePath,
			WorkingDirectory = ResolveWorkingDirectory(),
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardErrorEncoding = Encoding.UTF8,
		};

		for (int i = 0; i < _options.ServerArguments.Count; i++)
			startInfo.ArgumentList.Add(_options.ServerArguments[i]);

		foreach ((string variableName, string variableValue) in _options.EnvironmentVariables)
			startInfo.Environment[variableName] = variableValue;

		IChildProcessLifetimeGuard guard = _options.ChildProcessLifetimeGuardFactory?.Invoke() ?? PlatformChildProcessGuard.Instance;

		var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

		try
		{
			if (!process.Start())
				throw new InvalidOperationException($"The language server process '{_serverExecutablePath}' could not be started.");
		}
		catch
		{
			process.Dispose();
			throw;
		}

		// Bind the child to the lifetime guard (the platform default is a shared kill-on-close job object on
		// Windows and a no-op elsewhere). The guard logs its own failures.
		var connection = new StdioLanguageServerConnection(process);

		try
		{
			guard.Bind(process, request.Logger);
		}
		catch
		{
			// The guard contract is that Bind never throws, but a custom guard can violate it. Terminate the freshly
			// spawned child (a raw connection disposal does not) and release its redirected streams instead of
			// stranding an unguarded process, then surface the guard failure.
			try
			{
				if (!process.HasExited)
					process.Kill(entireProcessTree: true);
			}
			catch (Exception)
			{
				// The process may already be gone, and the guard failure rethrown below is the one to report; the connection still has
				// to be released.
			}

			connection.Release();
			throw;
		}

		return Task.FromResult<ILanguageServerConnection>(connection);
	}

	/// <summary>Names the transport and its executable, which the client's startup-failure log reports.</summary>
	/// <returns>The description, for example <c>stdio 'C:\Tools\server.exe'</c>.</returns>
	public override string ToString() => $"stdio '{_serverExecutablePath}'";

	/// <summary>Resolves the working directory of one launch: the configured directory, or the directory of the executable.</summary>
	/// <returns>The working directory; the process current directory when the executable path has no directory part.</returns>
	private string ResolveWorkingDirectory()
	{
		if (_options.ServerWorkingDirectory is { } configured)
			return configured;

		string? executableDirectory = Path.GetDirectoryName(_serverExecutablePath);
		return string.IsNullOrEmpty(executableDirectory) ? Environment.CurrentDirectory : executableDirectory;
	}

	/// <summary>Owns the spawned server process and the standard streams of one stdio transport connection.</summary>
	private sealed class StdioLanguageServerConnection : ILanguageServerConnection
	{
		/// <param name="process">The started language-server process.</param>
		internal StdioLanguageServerConnection(Process process)
		{
			Process = process;
			ReadStream = process.StandardOutput.BaseStream;
			WriteStream = process.StandardInput.BaseStream;
		}

		/// <inheritdoc/>
		public Stream ReadStream { get; }

		/// <inheritdoc/>
		public Stream WriteStream { get; }

		/// <inheritdoc/>
		public Process? Process { get; }

		/// <inheritdoc/>
		public ValueTask DisposeAsync()
		{
			Release();
			return ValueTask.CompletedTask;
		}

		/// <summary>Releases the streams and the process, each independently, so one failed disposal cannot skip the others.</summary>
		/// <remarks>
		/// The client's background read loops depend on the streams and the process being released even when one disposal throws.
		/// </remarks>
		internal void Release()
		{
			DisposeIgnoringFailure(WriteStream);
			DisposeIgnoringFailure(ReadStream);
			DisposeIgnoringFailure(Process);
		}

		private static void DisposeIgnoringFailure(IDisposable? resource)
		{
			try
			{
				resource?.Dispose();
			}
			catch (Exception)
			{
				// A resource that fails to dispose leaves nothing to act on, the connection holds no logger, and the other resources are
				// released regardless.
			}
		}
	}
}
