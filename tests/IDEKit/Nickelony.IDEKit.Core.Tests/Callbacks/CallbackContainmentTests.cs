namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the callback containment: a throwing callback is reported and reported as not applied, the helper contains every exception
/// including a cancellation, and a fallback value replaces the result of a callback that threw.
/// </summary>
[TestClass]
public sealed class CallbackContainmentTests
{
	[TestMethod]
	public void TryRun_CompletingCallback_ReturnsTrueAndReportsNothing()
	{
		bool invoked = false;
		bool reported = false;

		bool applied = CallbackContainment.TryRun(() => invoked = true, _ => reported = true);

		Assert.IsTrue(applied);
		Assert.IsTrue(invoked);
		Assert.IsFalse(reported);
	}

	[TestMethod]
	public void TryRun_ThrowingCallback_ReportsTheFaultAndReturnsFalse()
	{
		var failure = new InvalidOperationException("host failure");
		Exception? reported = null;

		bool applied = CallbackContainment.TryRun(() => throw failure, exception => reported = exception);

		Assert.IsFalse(applied);
		Assert.AreSame(failure, reported);
	}

	[TestMethod]
	public void TryRun_ThrowingCallbackWithACancellation_IsContained()
	{
		Exception? reported = null;

		// A host callback is synchronous user code with no cancellation contract, so a cancellation it
		// throws must be contained like any other host failure.
		bool applied = CallbackContainment.TryRun(() => throw new OperationCanceledException(), exception => reported = exception);

		Assert.IsFalse(applied);
		Assert.IsInstanceOfType<OperationCanceledException>(reported);
	}

	[TestMethod]
	public void TryRun_Result_CompletingCallback_ReturnsTheResult()
	{
		bool applied = CallbackContainment.TryRun(() => 42, static _ => { }, out int result);

		Assert.IsTrue(applied);
		Assert.AreEqual(42, result);
	}

	[TestMethod]
	public void TryRun_Result_ThrowingCallback_ReturnsFalseWithTheDefaultResult()
	{
		var failure = new InvalidOperationException("host failure");
		Exception? reported = null;

		bool applied = CallbackContainment.TryRun<int>(() => throw failure, exception => reported = exception, out int result);

		Assert.IsFalse(applied);
		Assert.AreEqual(0, result);
		Assert.AreSame(failure, reported);
	}

	[TestMethod]
	public void Invoke_CompletingCallback_ReturnsItsResultAndReportsNothing()
	{
		bool reported = false;

		int result = CallbackContainment.Invoke(() => 7, fallbackValue: 42, _ => reported = true);

		Assert.AreEqual(7, result);
		Assert.IsFalse(reported);
	}

	[TestMethod]
	public void Invoke_ThrowingCallback_ReportsTheFaultAndReturnsTheFallbackValue()
	{
		var failure = new InvalidOperationException("Simulated hook failure.");
		Exception? reported = null;

		int result = CallbackContainment.Invoke<int>(() => throw failure, fallbackValue: 42, exception => reported = exception);

		Assert.AreEqual(42, result);
		Assert.AreSame(failure, reported);
	}

	[TestMethod]
	public void Invoke_WhenReportingTheFaultThrows_PropagatesTheReportingFailure()
	{
		var reportingFailure = new InvalidOperationException("reporting failure");

		InvalidOperationException thrown = Assert.ThrowsExactly<InvalidOperationException>(() =>
			CallbackContainment.Invoke<int>(() => throw new IOException(), fallbackValue: 0, _ => throw reportingFailure)
		);

		Assert.AreSame(reportingFailure, thrown);
	}

	[TestMethod]
	public void Members_GivenNull_ThrowArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => CallbackContainment.TryRun(null!, static _ => { }));
		Assert.ThrowsExactly<ArgumentNullException>(() => CallbackContainment.TryRun(static () => { }, null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => CallbackContainment.TryRun<int>(null!, static _ => { }, out _));
		Assert.ThrowsExactly<ArgumentNullException>(() => CallbackContainment.TryRun(static () => 1, null!, out int _));
		Assert.ThrowsExactly<ArgumentNullException>(() => CallbackContainment.Invoke<int>(null!, fallbackValue: 0, static _ => { }));
		Assert.ThrowsExactly<ArgumentNullException>(() => CallbackContainment.Invoke(static () => 1, fallbackValue: 0, null!));
	}
}
