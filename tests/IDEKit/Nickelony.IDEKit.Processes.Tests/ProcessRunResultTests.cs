namespace Nickelony.IDEKit.Processes.Tests;

/// <summary>Verifies the compact summary of <see cref="ProcessRunResult"/>.</summary>
[TestClass]
public sealed class ProcessRunResultTests
{
	[TestMethod]
	public void ToString_LargeCapturedOutput_SummarizesByLength()
	{
		var result = new ProcessRunResult
		{
			Outcome = ProcessRunOutcome.Exited,
			ExitCode = 0,
			ProcessId = 4242,
			StandardOutput = new string('o', 10_000),
			StandardError = new string('e', 5_000),
		};

		string summary = result.ToString();

		Assert.Contains("Outcome = Exited", summary);
		Assert.Contains("ExitCode = 0", summary);
		Assert.Contains("ProcessId = 4242", summary);
		Assert.Contains("<10000 chars>", summary);
		Assert.Contains("<5000 chars>", summary);
		Assert.DoesNotContain(new string('o', 10), summary, "The summary materialized the captured output.");
	}

	[TestMethod]
	public void ToString_MissingExitAndCaptures_ReportsNull()
	{
		var result = new ProcessRunResult { Outcome = ProcessRunOutcome.TimedOut };

		string summary = result.ToString();

		Assert.Contains("Outcome = TimedOut", summary);
		Assert.Contains("ExitCode = null", summary);
		Assert.Contains("ProcessId = null", summary);
		Assert.Contains("StandardOutput = null", summary);
		Assert.Contains("StandardError = null", summary);
	}

	[TestMethod]
	public void Succeeded_ExitedWithZeroExitCode_IsTrue()
	{
		var result = new ProcessRunResult { Outcome = ProcessRunOutcome.Exited, ExitCode = 0 };
		Assert.IsTrue(result.Succeeded);
	}

	[TestMethod]
	public void Succeeded_AnyOtherExitOrOutcome_IsFalse()
	{
		Assert.IsFalse(new ProcessRunResult { Outcome = ProcessRunOutcome.Exited, ExitCode = 3 }.Succeeded);
		Assert.IsFalse(new ProcessRunResult { Outcome = ProcessRunOutcome.Exited }.Succeeded);
		Assert.IsFalse(new ProcessRunResult { Outcome = ProcessRunOutcome.TimedOut }.Succeeded);
		Assert.IsFalse(new ProcessRunResult { Outcome = ProcessRunOutcome.Canceled }.Succeeded);
		Assert.IsFalse(new ProcessRunResult { Outcome = ProcessRunOutcome.CanceledBeforeStart }.Succeeded);
		Assert.IsFalse(new ProcessRunResult { Outcome = ProcessRunOutcome.NoProcessHandle }.Succeeded);
	}

	[TestMethod]
	public void ProcessId_DefaultsToNull()
	{
		var result = new ProcessRunResult { Outcome = ProcessRunOutcome.NoProcessHandle };
		Assert.IsNull(result.ProcessId);
	}
}
