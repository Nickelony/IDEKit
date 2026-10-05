#if AVALONIAQUICKPICK

namespace Nickelony.QuickPick.Avalonia.Tests;

#else

namespace Nickelony.QuickPick.Wpf.Tests;

#endif

[TestClass]
public sealed class QuickPickSubscriptionTests
{
	[TestMethod]
	public void Dispose_RunsTheUnsubscribeActionOnce()
	{
		int runs = 0;
		var subscription = new QuickPickSubscription(() => runs++);

		subscription.Dispose();
		subscription.Dispose();

		Assert.AreEqual(1, runs);
	}

	[TestMethod]
	public void Dispose_WithoutBeingCalled_RunsNothing()
	{
		int runs = 0;

		_ = new QuickPickSubscription(() => runs++);

		Assert.AreEqual(0, runs);
	}
}
