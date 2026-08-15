namespace Nickelony.IDEKit.Processes;

/// <summary>
/// Creates process handles from run requests, abstracted so the orchestration in
/// <see cref="ProcessRunner"/> can be tested without launching real processes.
/// </summary>
internal interface IProcessLauncher
{
	/// <summary>
	/// Starts the executable, file, or shell target described by the request.
	/// </summary>
	/// <param name="request">The process run request.</param>
	/// <param name="options">The run options that configure the started handle.</param>
	/// <returns>The started process handle, or <see langword="null"/> when shell execution launched the target
	/// through the operating system without returning a process handle.</returns>
	IProcessHandle? Start(ProcessRunRequest request, ProcessRunnerOptions options);
}
