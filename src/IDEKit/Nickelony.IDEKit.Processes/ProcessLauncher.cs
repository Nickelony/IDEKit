using System.Diagnostics;

namespace Nickelony.IDEKit.Processes;

/// <summary>
/// Builds a <see cref="ProcessStartInfo"/> from a <see cref="ProcessRunRequest"/> and starts the process.
/// </summary>
internal sealed class ProcessLauncher : IProcessLauncher
{
	public IProcessHandle? Start(ProcessRunRequest request, ProcessRunnerOptions options)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = request.FileName,
			Arguments = request.RawArguments,
			WorkingDirectory = request.WorkingDirectory,
			UseShellExecute = request.UseShellExecute,
			CreateNoWindow = request.CreateNoWindow,
			RedirectStandardOutput = request.RedirectStandardOutput,
			RedirectStandardError = request.RedirectStandardError,
			StandardOutputEncoding = request.StandardOutputEncoding,
			StandardErrorEncoding = request.StandardErrorEncoding
		};

		foreach ((string name, string value) in request.EnvironmentVariables)
			startInfo.Environment[name] = value;

		foreach (string argument in request.ArgumentList)
			startInfo.ArgumentList.Add(argument);

		Process? process = Process.Start(startInfo);

		if (process is null)
			return null;

		// A handle that cannot be constructed must not leave the started process running: the process is
		// released before the construction failure reaches the caller.
		try
		{
			return new ProcessHandle(
				process,
				request.RedirectStandardOutput,
				request.RedirectStandardError,
				options.MaxCapturedCharactersPerStream);
		}
		catch
		{
			process.Dispose();
			throw;
		}
	}
}
