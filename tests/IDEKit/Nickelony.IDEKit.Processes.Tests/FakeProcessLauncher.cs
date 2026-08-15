namespace Nickelony.IDEKit.Processes.Tests;

/// <summary>
/// A process launcher that records requests and returns a handle produced by a factory.
/// </summary>
internal sealed class FakeProcessLauncher : IProcessLauncher
{
	private readonly Func<ProcessRunRequest, IProcessHandle?> _start;

	public FakeProcessLauncher(Func<ProcessRunRequest, IProcessHandle?> start)
		=> _start = start;

	public ProcessRunRequest? LastRequest { get; private set; }

	public ProcessRunnerOptions? LastOptions { get; private set; }

	public int StartCalls { get; private set; }

	public IProcessHandle? Start(ProcessRunRequest request, ProcessRunnerOptions options)
	{
		StartCalls++;
		LastRequest = request;
		LastOptions = options;
		return _start(request);
	}
}
