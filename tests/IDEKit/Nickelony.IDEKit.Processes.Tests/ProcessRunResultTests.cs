namespace Nickelony.IDEKit.Processes.Tests;

/// <summary>
/// Verifies the compact summary of <see cref="ProcessRunResult"/>.
/// </summary>
[TestClass]
public class ProcessRunResultTests
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

		StringAssert.Contains(summary, "Outcome = Exited");
		StringAssert.Contains(summary, "ExitCode = 0");
		StringAssert.Contains(summary, "ProcessId = 4242");
		StringAssert.Contains(summary, "<10000 chars>");
		StringAssert.Contains(summary, "<5000 chars>");
		Assert.IsFalse(summary.Contains(new string('o', 10)), "The summary materialized the captured output.");
	}

	[TestMethod]
	public void ToString_MissingExitAndCaptures_ReportsNull()
	{
		var result = new ProcessRunResult { Outcome = ProcessRunOutcome.TimedOut };

		string summary = result.ToString();

		StringAssert.Contains(summary, "Outcome = TimedOut");
		StringAssert.Contains(summary, "ExitCode = null");
		StringAssert.Contains(summary, "ProcessId = null");
		StringAssert.Contains(summary, "StandardOutput = null");
		StringAssert.Contains(summary, "StandardError = null");
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
