namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the <see cref="IChangeNotificationSource"/> contract with a reference implementation:
/// subscribers are invoked once per raise, detaching stops the notifications, and the event may be
/// raised from any thread, so the subscriber runs on the raising thread.
/// </summary>
[TestClass]
public sealed class ChangeNotificationSourceContractTests
{
	[TestMethod]
	public void Changed_Raised_InvokesEverySubscriberOnce()
	{
		var source = new ReferenceChangeNotificationSource();
		int firstCalls = 0;
		int secondCalls = 0;
		source.Changed += (_, _) => firstCalls++;
		source.Changed += (_, _) => secondCalls++;

		source.RaiseChanged();

		Assert.AreEqual(1, firstCalls);
		Assert.AreEqual(1, secondCalls);
	}

	[TestMethod]
	public void Changed_AfterUnsubscribe_StopsNotifyingTheDetachedSubscriber()
	{
		var source = new ReferenceChangeNotificationSource();
		int calls = 0;
		EventHandler handler = (_, _) => calls++;
		source.Changed += handler;
		source.RaiseChanged();

		// A consumer detaches when it is no longer attached, so it must not be notified afterwards.
		source.Changed -= handler;
		source.RaiseChanged();

		Assert.AreEqual(1, calls);
	}

	[TestMethod]
	public void Changed_RaisedFromAnotherThread_InvokesTheSubscriberOnThatThread()
	{
		var source = new ReferenceChangeNotificationSource();
		int raiserThread = Environment.CurrentManagedThreadId;
		int? subscriberThread = null;
		source.Changed += (_, _) => subscriberThread = Environment.CurrentManagedThreadId;

		// The contract permits a raise from any thread, so the subscriber runs on the raising thread;
		// a consumer that touches thread-affine state must marshal the work onto its own thread.
		var thread = new Thread(source.RaiseChanged);
		thread.Start();
		Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "The raising thread did not finish.");

		int observedThread = subscriberThread ?? -1;
		Assert.AreNotEqual(-1, observedThread, "The subscriber did not observe the event.");
		Assert.AreNotEqual(raiserThread, observedThread);
	}

	[TestMethod]
	public void RaiseChanged_NoSubscribers_DoesNotThrow()
	{
		var source = new ReferenceChangeNotificationSource();

		source.RaiseChanged();
	}

	// A reference implementation with a plain event, so a raise from any thread reaches every
	// currently attached subscriber.
	private sealed class ReferenceChangeNotificationSource : IChangeNotificationSource
	{
		public event EventHandler? Changed;

		public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
	}
}
