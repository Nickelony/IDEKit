using System.Globalization;

namespace Nickelony.IDEKit.Processes.Tests;

/// <summary>
/// Builds small cross-platform shell commands for the tests that drive real processes.
/// </summary>
internal static class TestCommands
{
	// The launched shell stays alive for this long while its background descendant works, so a caller has a
	// stable process tree to terminate.
	private static readonly TimeSpan GrandchildHoldDuration = TimeSpan.FromSeconds(30);

	private static bool IsWindows
		=> OperatingSystem.IsWindows();

	/// <summary>
	/// Gets the shell executable used by the command builders.
	/// </summary>
	public static string ShellFileName
		=> IsWindows ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/bin/sh";

	/// <summary>
	/// Builds a command that echoes the given text to standard output.
	/// </summary>
	public static string Echo(string text)
		=> IsWindows ? $"/c echo {text}" : $"-c \"echo {text}\"";

	/// <summary>
	/// Builds a command that writes the bytes of the given file to standard output without transcoding them.
	/// </summary>
	public static string PrintFile(string filePath)
		=> IsWindows ? $"/c type \"{filePath}\"" : $"-c \"cat {filePath}\"";

	/// <summary>
	/// Builds a command that writes the bytes of the given files to standard output and standard error in turn,
	/// so both redirected streams carry a payload larger than the operating-system pipe buffer.
	/// </summary>
	public static string PrintFilesToBothStreams(string standardOutputFilePath, string standardErrorFilePath)
		=> IsWindows
			? $"/c type \"{standardOutputFilePath}\" & type \"{standardErrorFilePath}\" 1>&2"
			: $"-c \"cat {standardOutputFilePath}; cat {standardErrorFilePath} 1>&2\"";

	/// <summary>
	/// Builds a command that echoes the value of an environment variable.
	/// </summary>
	public static string EchoEnvironmentVariable(string variableName)
		=> IsWindows ? $"/c echo %{variableName}%" : $"-c \"echo ${variableName}\"";

	/// <summary>
	/// Builds a command that prints the current working directory.
	/// </summary>
	public static string PrintWorkingDirectory()
		=> IsWindows ? "/c cd" : "-c pwd";

	/// <summary>
	/// Builds a command that writes the given text to standard error.
	/// </summary>
	public static string WriteToStandardError(string text)
		=> IsWindows ? $"/c echo {text} 1>&2" : $"-c \"echo {text} 1>&2\"";

	/// <summary>
	/// Builds the argument list that echoes one argument passed through the platform argument list.
	/// </summary>
	public static string[] EchoSingleArgument(string value)
		=> IsWindows
			? ["/c", "echo", value]
			: ["-c", "echo \"$1\"", "sh", value];

	/// <summary>
	/// Builds a command that runs quietly for the given duration; it writes nothing to the redirected streams,
	/// so a redirected capture stays pending until it is canceled.
	/// </summary>
	public static string Sleep(TimeSpan duration)
		=> IsWindows
			? $"/c ping -n {PingCount(duration)} 127.0.0.1 > NUL"
			: $"-c \"sleep {Seconds(duration)}\"";

	/// <summary>
	/// Builds a command that starts a background child inheriting the redirected pipes and exits immediately
	/// itself, so the redirected pipes stay open without the launched process being alive.
	/// </summary>
	public static string StartAndDetachBackgroundChild(TimeSpan duration)
		=> IsWindows
			? $"/c start /b ping -n {PingCount(duration)} 127.0.0.1"
			: $"-c \"sleep {Seconds(duration)} &\"";

	/// <summary>
	/// Builds a command that starts a background descendant process and then keeps the launched shell alive.
	/// The descendant records its start at once, waits for the given work duration, and then records its
	/// completion, so a caller that terminates the whole process tree can prove the descendant never completed.
	/// </summary>
	/// <param name="workDuration">The descendant's work delay before it records its completion.</param>
	/// <param name="startedMarkerPath">The file the descendant creates when it starts.</param>
	/// <param name="finishedMarkerPath">The file the descendant creates when its work completes.</param>
	public static string StartAndHoldBackgroundGrandchild(
		TimeSpan workDuration,
		string startedMarkerPath,
		string finishedMarkerPath)
		=> IsWindows
			? $"/c start /b powershell -NoProfile -Command \"Set-Content -LiteralPath '{startedMarkerPath}' -Value 1; Start-Sleep -Seconds {Seconds(workDuration)}; Set-Content -LiteralPath '{finishedMarkerPath}' -Value 1\" & ping -n {PingCount(GrandchildHoldDuration)} 127.0.0.1 > NUL"
			: $"-c \"(echo 1 > '{startedMarkerPath}'; sleep {Seconds(workDuration)}; echo 1 > '{finishedMarkerPath}') & sleep {Seconds(GrandchildHoldDuration)}\"";

	private static string Seconds(TimeSpan duration)
		=> duration.TotalSeconds.ToString(CultureInfo.InvariantCulture);

	// ping with n requests runs for approximately n-1 seconds.
	private static int PingCount(TimeSpan duration)
		=> Math.Max(2, (int)Math.Ceiling(duration.TotalSeconds) + 1);
}
