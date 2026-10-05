namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class DocumentLeaseCountsTests
{
	[TestMethod]
	public void AcquireAndRelease_OpenAndRequestLeases_RemainAccurateUnderConcurrency()
	{
		const int operationCount = 2000;

		var tracker = new DocumentLeaseCounts();

		Parallel.For(0, operationCount, _ => tracker.AcquireOpen());
		Parallel.For(0, operationCount, _ => tracker.AcquireRequest());

		Assert.AreEqual(operationCount, tracker.OpenLeaseCount);
		Assert.AreEqual(operationCount, tracker.RequestLeaseCount);
		Assert.IsTrue(tracker.HasOpenLeases);
		Assert.IsFalse(tracker.IsIdle);

		Parallel.For(0, operationCount, _ => tracker.ReleaseOpen());
		Parallel.For(0, operationCount, _ => tracker.ReleaseRequest());

		Assert.AreEqual(0, tracker.OpenLeaseCount);
		Assert.AreEqual(0, tracker.RequestLeaseCount);
		Assert.IsFalse(tracker.HasOpenLeases);
		Assert.IsTrue(tracker.IsIdle);
	}

	[TestMethod]
	public void ReleaseMethods_DoNotDriveCountsNegativeUnderConcurrency()
	{
		var tracker = new DocumentLeaseCounts();

		Parallel.For(
			0,
			2000,

			_ =>
			{
				tracker.ReleaseOpen();
				tracker.ReleaseRequest();
			}
		);

		Assert.AreEqual(0, tracker.OpenLeaseCount);
		Assert.AreEqual(0, tracker.RequestLeaseCount);
		Assert.IsTrue(tracker.IsIdle);
	}
}
