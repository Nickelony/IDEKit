using Nickelony.IDEKit.Processes;

namespace Nickelony.Samples;

/// <summary>Runs the command line of the settings file as an external process and returns its output as text.</summary>
internal sealed class SampleProcessRunner
{
	private readonly IProcessRunner _runner;

	/// <param name="runner">The runner that starts the process.</param>
	internal SampleProcessRunner(IProcessRunner runner) => _runner = runner;

	/// <summary>Runs the command line in the working directory with a timeout and describes how the run ended.</summary>
	internal async Task<string> RunAsync(string commandLine, string workingDirectory, CancellationToken cancellationToken = default)
	{
		string[] parts = commandLine.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		if (parts.Length == 0)
			return "The settings name no command to run.";

		ProcessRunResult result = await _runner.RunAsync(
			new ProcessRunRequest
			{
				FileName = parts[0],
				RawArguments = parts.Length > 1 ? parts[1] : string.Empty,
				WorkingDirectory = workingDirectory,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
				Timeout = TimeSpan.FromSeconds(10),
			},

			cancellationToken
		);

		return result.Outcome switch
		{
			ProcessRunOutcome.Exited => $"{result.StandardOutput}{result.StandardError}{Environment.NewLine}Exit code {result.ExitCode}",
			ProcessRunOutcome.TimedOut => "The command did not finish within 10 seconds.",
			ProcessRunOutcome.Canceled or ProcessRunOutcome.CanceledBeforeStart => "The command was canceled.",
			_ => $"The command produced no process: {result.Outcome}.",
		};
	}
}
