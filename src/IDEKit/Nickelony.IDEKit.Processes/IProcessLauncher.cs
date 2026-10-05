namespace Nickelony.IDEKit.Processes;

/// <summary>Starts an external process and hands the caller a handle to drive it.</summary>
/// <remarks>
/// A consumer that only waits for an outcome types against <see cref="IProcessRunner"/> instead, so its fake needs no handle.
/// <see cref="ProcessRunner"/> implements both interfaces.
/// </remarks>
public interface IProcessLauncher
{
	/// <summary>Starts the process described by the request and returns a handle the caller drives.</summary>
	/// <param name="request">The process run request.</param>
	/// <returns>The started process handle.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">
	/// The request sets <see cref="ProcessRunRequest.UseShellExecute"/>, which this method always rejects, or it combines settings the launch
	/// mechanism cannot honor, such as both argument shapes set or an output encoding without the matching redirection. The request is rejected
	/// before anything is launched.
	/// </exception>
	/// <exception cref="System.ComponentModel.Win32Exception">
	/// The process could not be started, such as when the executable does not exist.
	/// </exception>
	/// <exception cref="InvalidOperationException">The launch mechanism did not produce a process.</exception>
	/// <remarks>
	/// The caller disposes the handle. <see cref="ProcessRunRequest.Timeout"/> does not apply to a handle; the caller bounds its own waits.
	/// The request must produce a process handle, so <see cref="ProcessRunRequest.UseShellExecute"/> is rejected because a shell launch resolves
	/// the target through the operating system without returning one; use <see cref="IProcessRunner.Run"/> or <see cref="IProcessRunner.RunAsync"/>
	/// for a shell launch, which report it as <see cref="ProcessRunOutcome.NoProcessHandle"/>.
	/// </remarks>
	IProcessHandle Start(ProcessRunRequest request);
}
