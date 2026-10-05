namespace Nickelony.IDEKit.Processes.Tests;

/// <summary>
/// Pins the split between waiting for an outcome (<see cref="IProcessRunner"/>) and driving a handle (<see cref="IProcessLauncher"/>).
/// </summary>
[TestClass]
public sealed class ProcessRunnerContractTests
{
	[TestMethod]
	public void ProcessRunner_ImplementsBothInterfaces()
	{
		var runner = new ProcessRunner();

		Assert.IsInstanceOfType<IProcessRunner>(runner);
		Assert.IsInstanceOfType<IProcessLauncher>(runner);
	}

	[TestMethod]
	public async Task AFakeRunner_NeedsNoHandle()
	{
		IProcessRunner runner = new OutcomeOnlyRunner();

		ProcessRunResult result = await runner.RunAsync(new ProcessRunRequest { FileName = "tool" });

		Assert.AreEqual(ProcessRunOutcome.Exited, result.Outcome);
	}

	private sealed class OutcomeOnlyRunner : IProcessRunner
	{
		public ProcessRunResult Run(ProcessRunRequest request, CancellationToken cancellationToken = default) =>
			new() { Outcome = ProcessRunOutcome.Exited, ExitCode = 0 };

		public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult(Run(request, cancellationToken));
	}
}
