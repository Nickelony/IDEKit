using System.Diagnostics;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>Starts and observes the child processes that tests use in place of a language-server process.</summary>
internal static class ProcessTestHelpers
{
	internal static Process StartDisposableProcess()
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
			Arguments = "/c ping 127.0.0.1 -n 10 > nul",
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};

		return Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start the disposable test process.");
	}

	/// <summary>Starts a process that exits as soon as it has read one line from its standard input.</summary>
	internal static Process StartProcessThatExitsAfterOneInputLine()
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
			Arguments = "/c \"set /p line=& exit\"",
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};

		return Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start the one-line test process.");
	}

	/// <summary>Starts a process that writes the given lines to its standard error and exits; an empty string writes a blank line.</summary>
	internal static Process StartProcessThatWritesToStandardError(params string[] lines)
	{
		string commands = string.Join(" & ", lines.Select(line => line.Length == 0 ? "echo. 1>&2" : "echo " + line + " 1>&2"));

		var startInfo = new ProcessStartInfo
		{
			FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
			Arguments = "/c \"" + commands + "\"",
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};

		return Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start the standard-error test process.");
	}

	// Deadline-bounded wait: a fixed iteration count can expire under load even though the process still exits
	// a moment later, while the deadline can only fail red when the process never exits at all.
	internal static Task<bool> WaitForProcessExitAsync(int processId) =>
		TestPolling.ForConditionAsync(() => HasProcessExited(processId), TestPolling.DefaultTimeout);

	private static bool HasProcessExited(int processId)
	{
		try
		{
			using var process = Process.GetProcessById(processId);
			return process.HasExited;
		}
		catch (ArgumentException)
		{
			return true;
		}
	}
}
