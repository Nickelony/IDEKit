using System.Diagnostics;
using System.Text;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Connects to a language server by launching it as a child process and speaking the protocol over its standard
/// input and output streams.
/// </summary>
/// <remarks>
/// <para>
/// This is the transport a <see cref="LanguageServerClient"/> uses by default
/// (<see cref="LanguageServerClientOptions.Transport"/>). On Windows the child is additionally bound to the host
/// process through a job object as a best-effort guarantee that it cannot outlive a crashed host.
/// </para>
/// <para>
/// The instance is stateless and thread-safe, so the shared <see cref="Default"/> instance can serve any number of
/// clients.
/// </para>
/// </remarks>
public sealed class StdioLanguageServerTransport : ILanguageServerTransport
{
	/// <summary>
	/// Gets the shared instance used as the default transport.
	/// </summary>
	public static StdioLanguageServerTransport Default { get; } = new();

	/// <summary>
	/// Initializes a new instance of the <see cref="StdioLanguageServerTransport"/> class.
	/// </summary>
	public StdioLanguageServerTransport()
	{ }

	/// <inheritdoc/>
	public Task<ILanguageServerConnection> ConnectAsync(LanguageServerTransportContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		cancellationToken.ThrowIfCancellationRequested();

		var startInfo = new ProcessStartInfo
		{
			FileName = context.ServerExecutablePath,
			WorkingDirectory = context.ServerWorkingDirectory,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardErrorEncoding = Encoding.UTF8
		};

		for (int i = 0; i < context.ServerArguments.Count; i++)
			startInfo.ArgumentList.Add(context.ServerArguments[i]);

		foreach ((string variableName, string variableValue) in context.EnvironmentVariables)
			startInfo.Environment[variableName] = variableValue;

		var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

		try
		{
			if (!process.Start())
			{
				throw new InvalidOperationException(
					$"The language server process '{context.ServerExecutablePath}' could not be started.");
			}
		}
		catch
		{
			process.Dispose();
			throw;
		}

		// Platform-specific best-effort child lifetime binding: on Windows the child joins a shared kill-on-close
		// job object, while every other platform has no comparable crash guard and the binding is a documented
		// no-op that leaves the child to the graceful shutdown/exit handshake. A host can replace the guard through
		// LanguageServerClientOptions.ChildProcessLifetimeGuardFactory. The binding happens right after
		// Process.Start, so a host crash inside this window can still strand the child. The guard logs its own
		// failures, so a failure here is not reported twice.
		(context.ChildProcessLifetimeGuard ?? ChildProcessLifetime.DefaultGuard).Bind(process, context.Logger);

		return Task.FromResult<ILanguageServerConnection>(new StdioLanguageServerConnection(process));
	}

	/// <summary>
	/// Owns the spawned server process and the standard streams of one stdio transport connection.
	/// </summary>
	private sealed class StdioLanguageServerConnection : ILanguageServerConnection
	{
		private readonly Process _process;

		/// <summary>
		/// Initializes a new instance of the <see cref="StdioLanguageServerConnection"/> class.
		/// </summary>
		/// <param name="process">The started language-server process.</param>
		public StdioLanguageServerConnection(Process process)
		{
			_process = process;
			ReadStream = process.StandardOutput.BaseStream;
			WriteStream = process.StandardInput.BaseStream;
			ErrorStream = process.StandardError.BaseStream;
		}

		/// <inheritdoc/>
		public Stream ReadStream { get; }

		/// <inheritdoc/>
		public Stream WriteStream { get; }

		/// <inheritdoc/>
		public Stream? ErrorStream { get; }

		/// <inheritdoc/>
		public Process? Process => _process;

		/// <inheritdoc/>
		public ValueTask DisposeAsync()
		{
			// Each resource is released independently: one failed disposal must not skip the process or the streams
			// the client's background read loops depend on.
			DisposeIgnoringFailure(WriteStream);
			DisposeIgnoringFailure(ReadStream);
			DisposeIgnoringFailure(ErrorStream);
			DisposeIgnoringFailure(_process);

			return ValueTask.CompletedTask;
		}

		/// <summary>
		/// Disposes one connection resource while ignoring disposal failures.
		/// </summary>
		/// <param name="resource">The resource to dispose, or <see langword="null"/> when the connection has none.</param>
		private static void DisposeIgnoringFailure(IDisposable? resource)
		{
			try
			{
				resource?.Dispose();
			}
			catch
			{
				// The connection is being torn down anyway; one failed disposal must not block the others.
			}
		}
	}
}
