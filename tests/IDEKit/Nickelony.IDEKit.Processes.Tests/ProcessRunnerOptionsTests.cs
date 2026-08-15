namespace Nickelony.IDEKit.Processes.Tests;

/// <summary>
/// Verifies the defaults and validation of <see cref="ProcessRunnerOptions"/>.
/// </summary>
[TestClass]
public class ProcessRunnerOptionsTests
{
	[TestMethod]
	public void TerminationGracePeriod_Default_IsFiveSeconds()
	{
		Assert.AreEqual(TimeSpan.FromSeconds(5), new ProcessRunnerOptions().TerminationGracePeriod);
	}

	[TestMethod]
	public void OutputDrainGracePeriod_Default_IsFiveSeconds()
	{
		Assert.AreEqual(TimeSpan.FromSeconds(5), new ProcessRunnerOptions().OutputDrainGracePeriod);
	}

	[TestMethod]
	public void TerminationGracePeriod_NegativeValue_IsRejected()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => new ProcessRunnerOptions { TerminationGracePeriod = TimeSpan.FromMilliseconds(-1) });
	}

	[TestMethod]
	public void OutputDrainGracePeriod_NegativeValue_IsRejected()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => new ProcessRunnerOptions { OutputDrainGracePeriod = TimeSpan.FromMilliseconds(-1) });
	}

	[TestMethod]
	public void TerminationGracePeriod_Int32MillisecondsMaximum_IsAccepted()
	{
		var options = new ProcessRunnerOptions { TerminationGracePeriod = TimeSpan.FromMilliseconds(int.MaxValue) };

		Assert.AreEqual(TimeSpan.FromMilliseconds(int.MaxValue), options.TerminationGracePeriod);
	}

	[TestMethod]
	public void OutputDrainGracePeriod_Int32MillisecondsMaximum_IsAccepted()
	{
		var options = new ProcessRunnerOptions { OutputDrainGracePeriod = TimeSpan.FromMilliseconds(int.MaxValue) };

		Assert.AreEqual(TimeSpan.FromMilliseconds(int.MaxValue), options.OutputDrainGracePeriod);
	}

	[TestMethod]
	public void TerminationGracePeriod_ExceedingInt32Milliseconds_IsRejected()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => new ProcessRunnerOptions { TerminationGracePeriod = TimeSpan.FromDays(30) });
	}

	[TestMethod]
	public void OutputDrainGracePeriod_ExceedingInt32Milliseconds_IsRejected()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => new ProcessRunnerOptions { OutputDrainGracePeriod = TimeSpan.FromDays(30) });
	}

	[TestMethod]
	public void MaxCapturedCharactersPerStream_Defaults_AreUnbounded()
	{
		Assert.IsNull(new ProcessRunnerOptions().MaxCapturedCharactersPerStream);
		Assert.IsNull(ProcessRunnerOptions.Default.MaxCapturedCharactersPerStream);
	}

	[TestMethod]
	public void MaxCapturedCharactersPerStream_NegativeValue_IsRejected()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => new ProcessRunnerOptions { MaxCapturedCharactersPerStream = -1 });
	}

	[TestMethod]
	public void MaxCapturedCharactersPerStream_Zero_IsAccepted()
	{
		var options = new ProcessRunnerOptions { MaxCapturedCharactersPerStream = 0 };

		Assert.AreEqual(0, options.MaxCapturedCharactersPerStream);
	}

	[TestMethod]
	public void GracePeriods_Zero_AreAccepted()
	{
		var options = new ProcessRunnerOptions
		{
			TerminationGracePeriod = TimeSpan.Zero,
			OutputDrainGracePeriod = TimeSpan.Zero,
		};

		Assert.AreEqual(TimeSpan.Zero, options.TerminationGracePeriod);
		Assert.AreEqual(TimeSpan.Zero, options.OutputDrainGracePeriod);
	}
}
