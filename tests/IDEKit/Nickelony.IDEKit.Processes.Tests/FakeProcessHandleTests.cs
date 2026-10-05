namespace Nickelony.IDEKit.Processes.Tests;

/// <summary>Verifies that the capture accessors of <see cref="FakeProcessHandle"/> report a pending capture instead of blocking on it.</summary>
[TestClass]
public sealed class FakeProcessHandleTests
{
	// A blocking accessor never returns, so the bound turns a hang into a failure.
	[TestMethod]
	[Timeout(5000)]
	public void CaptureAccessors_WhileTheCaptureIsPending_ThrowInsteadOfBlocking()
	{
		using var handle = new FakeProcessHandle { HoldsCapturedOutput = true };

		Assert.ThrowsExactly<InvalidOperationException>(() => _ = handle.StandardOutput);
		Assert.ThrowsExactly<InvalidOperationException>(() => _ = handle.StandardError);
	}

	[TestMethod]
	public void CaptureAccessors_AfterTheCaptureCompleted_ReturnTheText()
	{
		using var handle = new FakeProcessHandle { StandardOutputText = "out", StandardErrorText = "err" };

		Assert.AreEqual("out", handle.StandardOutput);
		Assert.AreEqual("err", handle.StandardError);
	}
}
