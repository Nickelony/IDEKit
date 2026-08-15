namespace Nickelony.IDEKit.Processes;

/// <summary>
/// Launches and drives external processes for compiler, formatter, linter, and generator integration.
/// </summary>
public interface IProcessRunner
{
	/// <summary>
	/// Waits for the process described by the request to exit, for its timeout to elapse, or for cancellation,
	/// then returns the outcome.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This method blocks the calling thread; prefer <see cref="RunAsync"/> when the caller can await. The process
	/// handle is disposed before this method returns.
	/// </para>
	/// <para>
	/// Cancellation is reported as an outcome instead of an exception: a token that is already canceled when the
	/// call starts is reported as <see cref="ProcessRunOutcome.CanceledBeforeStart"/> without launching anything,
	/// and a token canceled while the runner waits is reported as <see cref="ProcessRunOutcome.Canceled"/> after
	/// the runner attempted to terminate the process and its descendants, falling back to the process alone if
	/// necessary.
	/// </para>
	/// <para>
	/// Redirected standard output and error are drained while the process runs, so a child that writes more than
	/// the operating-system pipe buffer cannot deadlock the wait.
	/// </para>
	/// <para>
	/// After the exit is observed, the capture waits at most one output-drain grace period for the pipes to
	/// close; when a descendant keeps a pipe open or the capture fails, the corresponding output is reported as
	/// unavailable. Termination confirmation and output capture can therefore add the configured grace periods
	/// after the timeout or cancellation ended the wait; see <see cref="ProcessRunnerOptions"/> for the
	/// defaults.
	/// </para>
	/// </remarks>
	/// <param name="request">The process run request.</param>
	/// <param name="cancellationToken">A token that can cancel the wait.</param>
	/// <returns>The run outcome.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The request combines settings the launch mechanism cannot honor, such
	/// as shell execution with redirection or environment overrides, both argument shapes set, or an output
	/// encoding without the matching redirection. The request is rejected before anything is launched.</exception>
	/// <exception cref="System.ComponentModel.Win32Exception">The process could not be started, such as when the executable does not exist.</exception>
	/// <exception cref="InvalidOperationException">The operating system reported a failure while starting or waiting for the process.</exception>
	ProcessRunResult Run(ProcessRunRequest request, CancellationToken cancellationToken = default);

	/// <summary>
	/// Asynchronously waits for the process described by the request to exit, for its timeout to elapse, or for
	/// cancellation, then returns the outcome.
	/// </summary>
	/// <remarks>
	/// The wait, termination, output-capture, and cancellation behavior matches <see cref="Run"/>. The configured
	/// timeout is enforced with a deadline so the waited time does not drift, and the process handle is disposed
	/// before the returned task completes.
	/// </remarks>
	/// <param name="request">The process run request.</param>
	/// <param name="cancellationToken">A token that can cancel the wait.</param>
	/// <returns>A task that produces the run outcome.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The request combines settings the launch mechanism cannot honor, such
	/// as shell execution with redirection or environment overrides, both argument shapes set, or an output
	/// encoding without the matching redirection. The request is rejected before anything is launched.</exception>
	/// <exception cref="System.ComponentModel.Win32Exception">The process could not be started, such as when the executable does not exist.</exception>
	/// <exception cref="InvalidOperationException">The operating system reported a failure while starting or waiting for the process.</exception>
	Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default);

	/// <summary>
	/// Starts the process described by the request and returns a handle the caller drives.
	/// </summary>
	/// <remarks>
	/// The caller is responsible for disposing the handle. <see cref="ProcessRunRequest.Timeout"/> does not apply
	/// to a handle; the caller bounds its own waits. The request must produce a process handle, so
	/// <see cref="ProcessRunRequest.UseShellExecute"/> is rejected because a shell launch resolves the target
	/// through the operating system without returning one; use <see cref="Run"/> or <see cref="RunAsync"/> for a
	/// shell launch, which report it as <see cref="ProcessRunOutcome.NoProcessHandle"/>.
	/// </remarks>
	/// <param name="request">The process run request.</param>
	/// <returns>The started process handle.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The request combines settings the launch mechanism cannot honor, such
	/// as shell execution or environment overrides with redirection, both argument shapes set, or an output
	/// encoding without the matching redirection. The request is rejected before anything is launched.</exception>
	/// <exception cref="System.ComponentModel.Win32Exception">The process could not be started, such as when the executable does not exist.</exception>
	/// <exception cref="InvalidOperationException">The launch mechanism did not produce a process.</exception>
	IProcessHandle Start(ProcessRunRequest request);
}
