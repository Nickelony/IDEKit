using System.Text;

namespace Nickelony.IDEKit.Processes.Tests;

/// <summary>
/// Verifies the request validation of <see cref="ProcessRunner"/>: <see langword="null"/> requests,
/// conflicting argument shapes, shell-execution combinations, and encodings without the matching
/// redirection are rejected before the handle factory runs. Property-level validation lives in <see cref="ProcessRunRequest"/> and is covered
/// by <see cref="ProcessRunRequestTests"/>.
/// </summary>
[TestClass]
public sealed class ProcessRunnerValidationTests
{
	[TestMethod]
	public void Run_NullRequest_ThrowsArgumentNullException()
	{
		var runner = new ProcessRunner(new FakeProcessHandleFactory(_ => new FakeProcessHandle()));
		Assert.ThrowsExactly<ArgumentNullException>(() => runner.Run(null!, TestContext.CancellationToken));
	}

	[TestMethod]
	public async Task RunAsync_NullRequest_ThrowsArgumentNullException()
	{
		var runner = new ProcessRunner(new FakeProcessHandleFactory(_ => new FakeProcessHandle()));
		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => runner.RunAsync(null!, TestContext.CancellationToken));
	}

	[TestMethod]
	public void Start_NullRequest_ThrowsArgumentNullException()
	{
		var runner = new ProcessRunner(new FakeProcessHandleFactory(_ => new FakeProcessHandle()));
		Assert.ThrowsExactly<ArgumentNullException>(() => runner.Start(null!));
	}

	[TestMethod]
	public void Run_RawArgumentsAndArgumentListBothSet_ThrowsArgumentException()
	{
		ProcessRunRequest request = TestRequests.Default with { RawArguments = "-flag", ArgumentList = ["-flag"] };
		AssertRejected(request);
	}

	[TestMethod]
	public void Run_EmptyRawArgumentsWithArgumentList_IsAccepted()
	{
		var handleFactory = new FakeProcessHandleFactory(_ => new FakeProcessHandle());
		var runner = new ProcessRunner(handleFactory);

		ProcessRunResult result = runner.Run(
			TestRequests.Default with
			{
				RawArguments = string.Empty,
				ArgumentList = ["-flag"],
			},

			TestContext.CancellationToken
		);

		Assert.AreEqual(ProcessRunOutcome.Exited, result.Outcome);
		Assert.AreEqual(1, handleFactory.CreateCalls);
	}

	[TestMethod]
	public void Run_ShellExecutionWithStandardOutputRedirection_ThrowsArgumentException() =>
		AssertRejected(TestRequests.Default with { UseShellExecute = true, RedirectStandardOutput = true });

	[TestMethod]
	public void Run_ShellExecutionWithStandardErrorRedirection_ThrowsArgumentException() =>
		AssertRejected(TestRequests.Default with { UseShellExecute = true, RedirectStandardError = true });

	[TestMethod]
	public void Run_ShellExecutionWithEnvironmentOverrides_ThrowsArgumentException()
	{
		AssertRejected(
			TestRequests.Default with
			{
				UseShellExecute = true,
				EnvironmentVariables = new Dictionary<string, string> { ["KEY"] = "value" },
			}
		);
	}

	[TestMethod]
	public void Run_StandardOutputEncodingWithoutRedirection_ThrowsArgumentException() =>
		AssertRejected(TestRequests.Default with { StandardOutputEncoding = Encoding.UTF8 });

	[TestMethod]
	public void Run_StandardErrorEncodingWithoutRedirection_ThrowsArgumentException() =>
		AssertRejected(TestRequests.Default with { StandardErrorEncoding = Encoding.UTF8 });

	[TestMethod]
	public void Start_InvalidRequest_ThrowsArgumentException()
	{
		var runner = new ProcessRunner(new FakeProcessHandleFactory(_ => new FakeProcessHandle()));

		Assert.ThrowsExactly<ArgumentException>(() =>
			runner.Start(TestRequests.Default with { UseShellExecute = true, RedirectStandardOutput = true })
		);
	}

	[TestMethod]
	public void Start_ShellExecutionRequest_IsRejectedBeforeLaunch()
	{
		var handleFactory = new FakeProcessHandleFactory(_ => new FakeProcessHandle());
		var runner = new ProcessRunner(handleFactory);

		Assert.ThrowsExactly<ArgumentException>(() => runner.Start(TestRequests.Default with { UseShellExecute = true }));
		Assert.AreEqual(0, handleFactory.CreateCalls, "The shell request was rejected only after the process was launched.");
	}

	[TestMethod]
	public void Run_ValidShellExecutionRequest_StartsProcess()
	{
		var handleFactory = new FakeProcessHandleFactory(_ => new FakeProcessHandle());
		var runner = new ProcessRunner(handleFactory);

		ProcessRunResult result = runner.Run(
			TestRequests.Default with
			{
				UseShellExecute = true,
				Timeout = TimeSpan.FromSeconds(1),
			},

			TestContext.CancellationToken
		);

		Assert.AreEqual(ProcessRunOutcome.Exited, result.Outcome);
		Assert.AreEqual(1, handleFactory.CreateCalls);
	}

	private static void AssertRejected(ProcessRunRequest request)
	{
		var handleFactory = new FakeProcessHandleFactory(_ => new FakeProcessHandle());
		var runner = new ProcessRunner(handleFactory);

		Assert.ThrowsExactly<ArgumentException>(() => runner.Run(request));
		Assert.AreEqual(0, handleFactory.CreateCalls, "The request was rejected only after the process was launched.");
	}

	public TestContext? TestContext { get; set; }
}
