namespace Nickelony.IDEKit.Core.Tests;

/// <summary>Pins the single-shot contract of the unsubscribe handle: the action runs on the first dispose and never again.</summary>
[TestClass]
public sealed class ActionSubscriptionTests
{
	[TestMethod]
	public void Constructor_NullAction_ThrowsArgumentNullException() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new ActionSubscription(null!));

	[TestMethod]
	public void Dispose_RunsTheUnsubscribeActionOnce()
	{
		int calls = 0;
		var subscription = new ActionSubscription(() => calls++);

		Assert.AreEqual(0, calls);

		subscription.Dispose();

		Assert.AreEqual(1, calls);
	}

	[TestMethod]
	public void Dispose_Repeatedly_RunsTheActionOnlyOnce()
	{
		int calls = 0;

		var subscription = new ActionSubscription(() => calls++);

		subscription.Dispose();
		subscription.Dispose();
		subscription.Dispose();

		Assert.AreEqual(1, calls);
	}

	[TestMethod]
	public void Using_DetachesWhenTheScopeEnds()
	{
		bool attached = true;

		using (new ActionSubscription(() => attached = false))
			Assert.IsTrue(attached);

		Assert.IsFalse(attached);
	}
}
