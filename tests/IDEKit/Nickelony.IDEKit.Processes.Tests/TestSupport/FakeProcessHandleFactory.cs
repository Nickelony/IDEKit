namespace Nickelony.IDEKit.Processes.Tests;

/// <summary>A process-handle factory that records requests and returns the handle that its delegate produces.</summary>
internal sealed class FakeProcessHandleFactory : IProcessHandleFactory
{
	private readonly Func<ProcessRunRequest, IProcessHandle?> _start;

	public FakeProcessHandleFactory(Func<ProcessRunRequest, IProcessHandle?> start) => _start = start;

	public ProcessRunRequest? LastRequest { get; private set; }

	public ProcessRunnerOptions? LastOptions { get; private set; }

	public int CreateCalls { get; private set; }

	public IProcessHandle? Create(ProcessRunRequest request, ProcessRunnerOptions options)
	{
		CreateCalls++;
		LastRequest = request;
		LastOptions = options;
		return _start(request);
	}
}
