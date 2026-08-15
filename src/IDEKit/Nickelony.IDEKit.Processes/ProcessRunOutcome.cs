namespace Nickelony.IDEKit.Processes;

/// <summary>
/// Describes how a process run ended.
/// </summary>
/// <remarks>
/// The values are mutually exclusive; <see cref="ProcessRunResult"/> carries the outcome as one value
/// instead of correlated flags, so an impossible combination cannot be observed or constructed.
/// </remarks>
public enum ProcessRunOutcome
{
	/// <summary>
	/// No process handle was produced, so there was nothing to wait for. Shell execution can launch the
	/// target through the operating system without returning a process; the target may still have started.
	/// </summary>
	NoProcessHandle = 0,

	/// <summary>
	/// Cancellation prevented the run from producing a process handle: the token was already canceled
	/// when the run was requested, or it was canceled before a process handle existed.
	/// </summary>
	CanceledBeforeStart = 1,

	/// <summary>
	/// The process exited and the exit was observed; <see cref="ProcessRunResult.ExitCode"/> reports the
	/// exit code.
	/// </summary>
	Exited = 2,

	/// <summary>
	/// The timeout ended the wait and termination was attempted; <see cref="ProcessRunResult.ExitCode"/>
	/// is <see langword="null"/> when the termination could not be confirmed.
	/// </summary>
	TimedOut = 3,

	/// <summary>
	/// Cancellation ended the wait and termination was attempted; <see cref="ProcessRunResult.ExitCode"/>
	/// is <see langword="null"/> when the termination could not be confirmed.
	/// </summary>
	Canceled = 4,
}
