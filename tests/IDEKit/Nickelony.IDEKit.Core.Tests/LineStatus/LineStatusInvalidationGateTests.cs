namespace Nickelony.IDEKit.Core.Tests;

/// <summary>
/// Pins the line-status invalidation gate: the source subscription follows the connection state and the coalescing gate admits one invalidation
/// at a time and always releases.
/// </summary>
[TestClass]
public sealed class LineStatusInvalidationGateTests
{
	private sealed class FakeChangeSource : IChangeNotificationSource
	{
		private EventHandler? _handler;

		public int SubscriptionCount { get; private set; }

		public event EventHandler? Changed
		{
			add
			{
				_handler += value;
				SubscriptionCount++;
			}
			remove
			{
				_handler -= value;
				SubscriptionCount--;
			}
		}

		public void RaiseChanged() => _handler?.Invoke(this, EventArgs.Empty);
	}

	[TestMethod]
	public void SetSource_WhenConnected_SubscribesAndRaisesTheQueuedInvalidation()
	{
		var source = new FakeChangeSource();
		int invalidations = 0;
		var gate = new LineStatusInvalidationGate(() => true, () => invalidations++, static action => action());

		gate.SetSource(source);

		Assert.IsTrue(gate.IsSourceSubscribed);
		Assert.AreEqual(1, source.SubscriptionCount);

		// The post delegate runs the queued action synchronously, so the invalidation lands immediately.
		source.RaiseChanged();

		Assert.AreEqual(1, invalidations);
	}

	[TestMethod]
	public void SetSource_WhenNotConnected_LeavesTheSourceUnsubscribed()
	{
		var source = new FakeChangeSource();
		var gate = new LineStatusInvalidationGate(static () => false, static () => { }, static action => action());

		gate.SetSource(source);

		Assert.IsFalse(gate.IsSourceSubscribed);
		Assert.AreEqual(0, source.SubscriptionCount);
	}

	[TestMethod]
	public void UpdateSubscription_WhenDisconnected_UnsubscribesAndStopsInvalidating()
	{
		var source = new FakeChangeSource();
		int invalidations = 0;
		var gate = new LineStatusInvalidationGate(() => true, () => invalidations++, static action => action());

		gate.SetSource(source);
		gate.UpdateSubscription(false);

		Assert.IsFalse(gate.IsSourceSubscribed);
		Assert.AreEqual(0, source.SubscriptionCount);

		source.RaiseChanged();

		Assert.AreEqual(0, invalidations);
	}

	[TestMethod]
	public void PublicMembers_GivenNull_ThrowArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new LineStatusInvalidationGate(null!, static () => { }, static action => action()));
		Assert.ThrowsExactly<ArgumentNullException>(() => new LineStatusInvalidationGate(static () => true, null!, static action => action()));
		Assert.ThrowsExactly<ArgumentNullException>(() => new LineStatusInvalidationGate(static () => true, static () => { }, null!));

		var gate = new LineStatusInvalidationGate(static () => true, static () => { }, static action => action());

		Assert.ThrowsExactly<ArgumentNullException>(() => gate.SetSource(null!));
	}

	[TestMethod]
	public void SetSource_CalledTwice_ThrowsInvalidOperationException()
	{
		var gate = new LineStatusInvalidationGate(static () => true, static () => { }, static action => action());

		gate.SetSource(new FakeChangeSource());

		Assert.ThrowsExactly<InvalidOperationException>(() => gate.SetSource(new FakeChangeSource()));
	}

	[TestMethod]
	public void QueueInvalidation_WhileQueued_CoalescesToOnePost()
	{
		var pending = new List<Action>();
		int invalidations = 0;

		var gate = new LineStatusInvalidationGate(() => true, () => invalidations++, pending.Add);

		gate.QueueInvalidation();
		gate.QueueInvalidation();

		Assert.IsTrue(gate.IsInvalidationQueued);
		Assert.HasCount(1, pending);

		pending[0]();

		Assert.IsFalse(gate.IsInvalidationQueued);
		Assert.AreEqual(1, invalidations);

		// Once the queued action ran, the gate admits the next invalidation again.
		gate.QueueInvalidation();

		Assert.HasCount(2, pending);
	}

	[TestMethod]
	public void QueueInvalidation_WhenPostThrows_ReleasesTheGate()
	{
		var gate = new LineStatusInvalidationGate(
			static () => true,
			static () => { },
			static _ => throw new InvalidOperationException("dispatcher shutting down")
		);

		gate.QueueInvalidation();

		Assert.IsFalse(gate.IsInvalidationQueued, "A post that threw must release the gate so a later invalidation is not suppressed.");
	}

	[TestMethod]
	public void QueueInvalidation_WhenCanPostIsFalse_SkipsThePost()
	{
		int posts = 0;

		var gate = new LineStatusInvalidationGate(
			isConnected: static () => true,
			invalidate: static () => { },
			post: _ => posts++,
			canPost: static () => false
		);

		gate.QueueInvalidation();

		Assert.IsFalse(gate.IsInvalidationQueued);
		Assert.AreEqual(0, posts);
	}

	[TestMethod]
	public void QueueInvalidation_AfterTheConnectionVersionBumped_DiscardsTheQueuedInvalidation()
	{
		var pending = new List<Action>();
		int invalidations = 0;
		var gate = new LineStatusInvalidationGate(() => true, () => invalidations++, pending.Add);

		gate.QueueInvalidation();
		gate.BumpConnectionVersion();

		pending[0]();

		Assert.AreEqual(0, invalidations, "An invalidation queued for a previous connection must be discarded.");
	}

	[TestMethod]
	public void QueueInvalidation_WhenTheGateIsNoLongerConnected_DiscardsTheQueuedInvalidation()
	{
		var pending = new List<Action>();

		bool connected = true;
		int invalidations = 0;

		var gate = new LineStatusInvalidationGate(() => connected, () => invalidations++, pending.Add);

		gate.QueueInvalidation();

		connected = false;

		pending[0]();

		Assert.AreEqual(0, invalidations);
	}
}
