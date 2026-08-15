namespace Nickelony.IDEKit.Processes;

/// <summary>
/// Represents a started external process and provides operations for waiting, requesting termination, and reading output.
/// </summary>
/// <remarks>
/// <para>
/// The caller owns the handle and must dispose it when it is no longer needed. Disposing does not terminate the
/// process; it releases the process resources and cancels the pending captures. A capture that has not completed
/// then ends by faulting (typically with <see cref="OperationCanceledException"/>,
/// <see cref="ObjectDisposedException"/>, or <see cref="IOException"/>) instead of waiting for the writers, and
/// members throw <see cref="ObjectDisposedException"/> after disposal.
/// </para>
/// <para>
/// Disposal is idempotent and safe to call concurrently with the members: the first caller releases the process
/// resources, and a member that runs concurrently with disposal either completes or throws
/// <see cref="ObjectDisposedException"/> when it reaches the released process, while any other failure
/// the race produces keeps its own type so a genuine fault is not hidden behind the disposal.
/// </para>
/// <para>
/// Implementations drain redirected standard output and error while the process runs, so a process that writes
/// more than the operating-system pipe buffer does not block on its own output.
/// </para>
/// <para>
/// A drain completes when every writer closes the pipe - possibly including descendants that inherited the
/// redirected streams - so the blocking accessors can wait for as long as a writer keeps the stream open; the
/// asynchronous accessors accept a cancellation token to bound that wait.
/// </para>
/// </remarks>
public interface IProcessHandle : IDisposable
{
	/// <summary>
	/// Gets the operating-system process identifier.
	/// </summary>
	/// <remarks>
	/// The identifier is stable for the handle's lifetime and stays available after the process exits.
	/// </remarks>
	/// <exception cref="ObjectDisposedException">The handle is disposed.</exception>
	int ProcessId { get; }

	/// <summary>
	/// Gets the process exit code after the process has exited.
	/// </summary>
	/// <exception cref="InvalidOperationException">The process has not exited.</exception>
	/// <exception cref="ObjectDisposedException">The handle is disposed.</exception>
	int ExitCode { get; }

	/// <summary>
	/// Gets all standard output captured from the redirected output stream.
	/// </summary>
	/// <remarks>
	/// Implementations drain the stream while the process runs; reading the property blocks until the stream
	/// closes. The process must have been started with standard-output redirection enabled; otherwise, accessing
	/// this property throws <see cref="InvalidOperationException"/>. Prefer <see cref="ReadStandardOutputAsync"/>
	/// to await the capture or to bound the wait with a token.
	/// </remarks>
	/// <exception cref="InvalidOperationException">The process was started without standard-output redirection.</exception>
	/// <exception cref="System.Text.DecoderFallbackException">The redirected stream contains a sequence the configured encoding rejects.</exception>
	/// <exception cref="IOException">The redirected stream failed while its capture was read.</exception>
	/// <exception cref="OperationCanceledException">The capture was canceled, typically because the handle was disposed while the read was in progress.</exception>
	/// <exception cref="ObjectDisposedException">The handle is disposed.</exception>
	string StandardOutput { get; }

	/// <summary>
	/// Gets all standard error captured from the redirected error stream.
	/// </summary>
	/// <remarks>
	/// Implementations drain the stream while the process runs; reading the property blocks until the stream
	/// closes. The process must have been started with standard-error redirection enabled; otherwise, accessing
	/// this property throws <see cref="InvalidOperationException"/>. Prefer <see cref="ReadStandardErrorAsync"/>
	/// to await the capture or to bound the wait with a token.
	/// </remarks>
	/// <exception cref="InvalidOperationException">The process was started without standard-error redirection.</exception>
	/// <exception cref="System.Text.DecoderFallbackException">The redirected stream contains a sequence the configured encoding rejects.</exception>
	/// <exception cref="IOException">The redirected stream failed while its capture was read.</exception>
	/// <exception cref="OperationCanceledException">The capture was canceled, typically because the handle was disposed while the read was in progress.</exception>
	/// <exception cref="ObjectDisposedException">The handle is disposed.</exception>
	string StandardError { get; }

	/// <summary>
	/// Gets a value indicating whether the captured standard output reached the capture bound and the
	/// excess was discarded.
	/// </summary>
	/// <remarks>
	/// The flag describes the capture bound only: it is <see langword="false"/> when standard output was
	/// not redirected, and it stays available when the capture itself later fails or is cut short before
	/// the drain completes. The bound is configured through
	/// <see cref="ProcessRunnerOptions.MaxCapturedCharactersPerStream"/>.
	/// </remarks>
	/// <exception cref="ObjectDisposedException">The handle is disposed.</exception>
	bool StandardOutputTruncated { get; }

	/// <summary>
	/// Gets a value indicating whether the captured standard error reached the capture bound and the
	/// excess was discarded.
	/// </summary>
	/// <remarks>
	/// See <see cref="StandardOutputTruncated"/>; the flag describes the error stream the same way.
	/// </remarks>
	/// <exception cref="ObjectDisposedException">The handle is disposed.</exception>
	bool StandardErrorTruncated { get; }

	/// <summary>
	/// Blocks until the process exits.
	/// </summary>
	/// <remarks>This method does not terminate the process.</remarks>
	/// <exception cref="ObjectDisposedException">The handle is disposed.</exception>
	void WaitForExit();

	/// <summary>
	/// Waits up to the specified number of milliseconds and reports whether the process exited in time.
	/// </summary>
	/// <remarks>This method does not terminate the process.</remarks>
	/// <param name="timeoutMilliseconds">The maximum wait in milliseconds, or
	/// <see cref="Timeout.Infinite"/> to wait indefinitely.</param>
	/// <returns><see langword="true"/> when the process exited within the timeout; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentOutOfRangeException">The value is less than <c>-1</c>.</exception>
	/// <exception cref="ObjectDisposedException">The handle is disposed.</exception>
	bool WaitForExit(int timeoutMilliseconds);

	/// <summary>
	/// Waits until the process exits.
	/// </summary>
	/// <remarks>
	/// This method does not terminate the process. When the token is canceled, the wait ends and the process keeps
	/// running.
	/// </remarks>
	/// <param name="cancellationToken">A token that can cancel the wait.</param>
	/// <returns>A task that completes when the process exits.</returns>
	/// <exception cref="OperationCanceledException">The <paramref name="cancellationToken"/> is canceled.</exception>
	/// <exception cref="ObjectDisposedException">The handle is disposed.</exception>
	Task WaitForExitAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Reads all standard output captured from the redirected output stream.
	/// </summary>
	/// <remarks>
	/// The returned task completes when the stream closes. When the token is canceled, the read ends and the
	/// process keeps running; the drain continues in the background. The redirection check throws synchronously
	/// instead of returning a faulted task.
	/// </remarks>
	/// <param name="cancellationToken">A token that can cancel the read.</param>
	/// <returns>A task that produces all captured standard output.</returns>
	/// <exception cref="InvalidOperationException">The process was started without standard-output redirection.</exception>
	/// <exception cref="System.Text.DecoderFallbackException">The redirected stream contains a sequence the configured encoding rejects.</exception>
	/// <exception cref="IOException">The redirected stream failed while its capture was read.</exception>
	/// <exception cref="OperationCanceledException">The <paramref name="cancellationToken"/> is canceled.</exception>
	/// <exception cref="ObjectDisposedException">The handle is disposed.</exception>
	Task<string> ReadStandardOutputAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Reads all standard error captured from the redirected error stream.
	/// </summary>
	/// <remarks>
	/// The returned task completes when the stream closes. When the token is canceled, the read ends and the
	/// process keeps running; the drain continues in the background. The redirection check throws synchronously
	/// instead of returning a faulted task.
	/// </remarks>
	/// <param name="cancellationToken">A token that can cancel the read.</param>
	/// <returns>A task that produces all captured standard error.</returns>
	/// <exception cref="InvalidOperationException">The process was started without standard-error redirection.</exception>
	/// <exception cref="System.Text.DecoderFallbackException">The redirected stream contains a sequence the configured encoding rejects.</exception>
	/// <exception cref="IOException">The redirected stream failed while its capture was read.</exception>
	/// <exception cref="OperationCanceledException">The <paramref name="cancellationToken"/> is canceled.</exception>
	/// <exception cref="ObjectDisposedException">The handle is disposed.</exception>
	Task<string> ReadStandardErrorAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Forces termination of the process; its descendants are not terminated.
	/// </summary>
	/// <remarks>This method does not wait for the process to exit.</remarks>
	/// <exception cref="InvalidOperationException">No process is associated with the handle, or the process is remote.</exception>
	/// <exception cref="System.ComponentModel.Win32Exception">The process could not be terminated.</exception>
	/// <exception cref="ObjectDisposedException">The handle is disposed.</exception>
	void Kill();

	/// <summary>
	/// Forces termination of the process and its descendants.
	/// </summary>
	/// <remarks>
	/// This method does not wait for the process to exit. Terminating the process tree may be unsupported for this
	/// process, descendants the caller cannot inspect are skipped, and the call may complete before every
	/// descendant has exited.
	/// </remarks>
	/// <exception cref="InvalidOperationException">No process is associated with the handle, or the process is remote.</exception>
	/// <exception cref="System.ComponentModel.Win32Exception">The process could not be terminated.</exception>
	/// <exception cref="NotSupportedException">Terminating the process tree is not supported for this process.</exception>
	/// <exception cref="AggregateException">Not all processes in the process tree could be terminated.</exception>
	/// <exception cref="ObjectDisposedException">The handle is disposed.</exception>
	void KillEntireProcessTree();
}
