using System.Globalization;

namespace Nickelony.IDEKit.Processes;

/// <summary>
/// Immutable outcome of a process run.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Outcome"/> reports how the run ended as one value. <see cref="ExitCode"/> is set when the process
/// exited and the exit was observed; the captured output is set when the corresponding stream was redirected and
/// its capture completed within the output-drain grace period.
/// </para>
/// <para>
/// Exit observation is bounded: when a termination attempt cannot be confirmed, <see cref="ExitCode"/> and the
/// captured output are <see langword="null"/> and the process may still be running. <see cref="ProcessId"/> is
/// reported whenever a process handle was produced, so a host can still act on a process whose exit was not
/// observed.
/// </para>
/// </remarks>
public sealed record ProcessRunResult
{
	/// <summary>
	/// Gets how the run ended.
	/// </summary>
	public required ProcessRunOutcome Outcome { get; init; }

	/// <summary>
	/// Gets the process exit code when the process exited and the exit was observed, or <see langword="null"/>
	/// when the exit could not be observed after termination was attempted. A confirmed termination after a
	/// timeout or cancellation reports the platform-specific termination code, not the process's own result.
	/// </summary>
	public int? ExitCode { get; init; }

	/// <summary>
	/// Gets the operating-system process identifier of the launched process, or <see langword="null"/> when no
	/// process handle was produced.
	/// </summary>
	/// <remarks>
	/// The identifier is reported even when the exit could not be observed, so a host can log or act on a process
	/// that may still be running.
	/// </remarks>
	public int? ProcessId { get; init; }

	/// <summary>
	/// Gets a value indicating whether the process ran to completion and reported a zero exit code.
	/// </summary>
	/// <remarks>
	/// The convenience is equivalent to
	/// <c>Outcome == ProcessRunOutcome.Exited &amp;&amp; ExitCode == 0</c>; it is <see langword="false"/> for every
	/// other outcome, including an exit whose code could not be observed.
	/// </remarks>
	public bool Succeeded => Outcome == ProcessRunOutcome.Exited && ExitCode == 0;

	/// <summary>
	/// Gets all captured standard output, or <see langword="null"/> when standard output was not
	/// redirected, when the exit could not be observed, when a descendant kept the redirected pipe open
	/// beyond the output-drain grace period, or when its capture failed (for example, the pipe failed
	/// while it was read or the configured encoding rejected a captured byte sequence).
	/// </summary>
	/// <remarks>
	/// The text holds at most
	/// <see cref="ProcessRunnerOptions.MaxCapturedCharactersPerStream"/> characters when a bound is
	/// configured; <see cref="StandardOutputTruncated"/> reports whether the bound was reached.
	/// </remarks>
	public string? StandardOutput { get; init; }

	/// <summary>
	/// Gets a value indicating whether the captured standard output was truncated at the configured
	/// capture bound.
	/// </summary>
	/// <remarks>
	/// The flag is <see langword="true"/> only when <see cref="StandardOutput"/> is reported and the
	/// configured <see cref="ProcessRunnerOptions.MaxCapturedCharactersPerStream"/> bound was reached;
	/// the reported text then holds at most the bound, so output that was exactly the bound long is also
	/// reported as truncated.
	/// </remarks>
	public bool StandardOutputTruncated { get; init; }

	/// <summary>
	/// Gets all captured standard error, or <see langword="null"/> when standard error was not
	/// redirected, when the exit could not be observed, when a descendant kept the redirected pipe open
	/// beyond the output-drain grace period, or when its capture failed (for example, the pipe failed
	/// while it was read or the configured encoding rejected a captured byte sequence).
	/// </summary>
	/// <remarks>
	/// The text holds at most
	/// <see cref="ProcessRunnerOptions.MaxCapturedCharactersPerStream"/> characters when a bound is
	/// configured; <see cref="StandardErrorTruncated"/> reports whether the bound was reached.
	/// </remarks>
	public string? StandardError { get; init; }

	/// <summary>
	/// Gets a value indicating whether the captured standard error was truncated at the configured
	/// capture bound.
	/// </summary>
	/// <remarks>
	/// See <see cref="StandardOutputTruncated"/>; the flag describes the error stream the same way.
	/// </remarks>
	public bool StandardErrorTruncated { get; init; }

	/// <summary>
	/// Returns a compact summary of the outcome without materializing the captured output.
	/// </summary>
	/// <remarks>
	/// The captured streams are described by their length instead of their content, so a result that holds a large
	/// output can be logged or inspected without copying the text.
	/// </remarks>
	/// <returns>The summary text.</returns>
	public override string ToString()
		=> $"ProcessRunResult {{ Outcome = {Outcome}, ExitCode = {DescribeNullableInt(ExitCode)}, ProcessId = {DescribeNullableInt(ProcessId)}, StandardOutput = {DescribeCapture(StandardOutput, StandardOutputTruncated)}, StandardError = {DescribeCapture(StandardError, StandardErrorTruncated)} }}";

	private static string DescribeNullableInt(int? value)
		=> value is { } present ? present.ToString(CultureInfo.InvariantCulture) : "null";

	private static string DescribeCapture(string? capture, bool truncated)
		=> capture is null
			? "null"
			: $"<{capture.Length.ToString(CultureInfo.InvariantCulture)} chars{(truncated ? ", truncated" : string.Empty)}>";
}
