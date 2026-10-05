using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>Covers the store lifetime directly: the sources it links, its cancellation and its disposal.</summary>
[TestClass]
public sealed class StoreLifetimeTests
{
	[TestMethod]
	public void CreateLinkedCancellation_IsCanceledByTheCallersToken()
	{
		using var lifetime = new StoreLifetime();
		using var caller = new CancellationTokenSource();

		using CancellationTokenSource linked = lifetime.CreateLinkedCancellation(caller.Token);

		caller.Cancel();

		Assert.IsTrue(linked.IsCancellationRequested);
	}

	[TestMethod]
	public void CreateLinkedCancellation_IsCanceledByTheLifetime()
	{
		using var lifetime = new StoreLifetime();
		using CancellationTokenSource linked = lifetime.CreateLinkedCancellation(CancellationToken.None);

		lifetime.Cancel();

		Assert.IsTrue(linked.IsCancellationRequested);
	}

	[TestMethod]
	public void CreateLinkedCancellation_AfterTheLifetimeWasCanceled_IsBornCanceled()
	{
		using var lifetime = new StoreLifetime();

		lifetime.Cancel();

		using CancellationTokenSource linked = lifetime.CreateLinkedCancellation(CancellationToken.None);

		Assert.IsTrue(linked.IsCancellationRequested);
	}

	[TestMethod]
	public void CreateLinkedCancellation_WhenTheCallersTokenIsCanceled_LeavesTheLifetimeAndItsOtherSourcesAlone()
	{
		using var lifetime = new StoreLifetime();
		using var caller = new CancellationTokenSource();

		using CancellationTokenSource first = lifetime.CreateLinkedCancellation(caller.Token);

		caller.Cancel();

		using CancellationTokenSource second = lifetime.CreateLinkedCancellation(CancellationToken.None);

		Assert.IsTrue(first.IsCancellationRequested);
		Assert.IsFalse(second.IsCancellationRequested);
	}

	[TestMethod]
	public void CreateLinkedCancellation_AfterDispose_Throws()
	{
		var lifetime = new StoreLifetime();

		lifetime.Dispose();

		Assert.ThrowsExactly<ObjectDisposedException>(() => lifetime.CreateLinkedCancellation(CancellationToken.None));
	}

	[TestMethod]
	public void CreateLinkedCancellationOrNull_BeforeDispose_ReturnsASourceLinkedToBoth()
	{
		using var lifetime = new StoreLifetime();
		using var caller = new CancellationTokenSource();

		using CancellationTokenSource? byCaller = lifetime.CreateLinkedCancellationOrNull(caller.Token);
		using CancellationTokenSource? byLifetime = lifetime.CreateLinkedCancellationOrNull(CancellationToken.None);

		Assert.IsNotNull(byCaller);
		Assert.IsNotNull(byLifetime);

		caller.Cancel();

		Assert.IsTrue(byCaller.IsCancellationRequested);
		Assert.IsFalse(byLifetime.IsCancellationRequested);

		lifetime.Cancel();

		Assert.IsTrue(byLifetime.IsCancellationRequested);
	}

	[TestMethod]
	public void CreateLinkedCancellationOrNull_AfterDispose_ReturnsNull()
	{
		var lifetime = new StoreLifetime();

		lifetime.Dispose();

		// A dirty reload runs without an operation registration, so the lifetime can already be disposed when it links;
		// the member reports that state instead of letting ObjectDisposedException escape an operation that returns Canceled.
		Assert.IsNull(lifetime.CreateLinkedCancellationOrNull(CancellationToken.None));
	}

	[TestMethod]
	public void Cancel_CalledTwice_DoesNotThrow()
	{
		using var lifetime = new StoreLifetime();

		lifetime.Cancel();
		lifetime.Cancel();
	}

	[TestMethod]
	public void Cancel_AfterDispose_Throws()
	{
		var lifetime = new StoreLifetime();

		lifetime.Dispose();

		// The owner cancels before it disposes. A cancel that found a disposed lifetime would reach no linked source, so it is reported
		// instead of being skipped.
		Assert.ThrowsExactly<ObjectDisposedException>(lifetime.Cancel);
	}

	[TestMethod]
	public void Cancel_WhenACallbackThrows_StillCancelsAndDoesNotThrow()
	{
		using var lifetime = new StoreLifetime();

		using CancellationTokenSource linked = lifetime.CreateLinkedCancellation(CancellationToken.None);
		using CancellationTokenRegistration registration = linked.Token.Register(() => throw new InvalidOperationException("The callback failed."));

		lifetime.Cancel();

		Assert.IsTrue(linked.IsCancellationRequested);
	}

	[TestMethod]
	public void Cancel_WhenACallbackThrows_StillCancelsEveryOtherLinkedSource()
	{
		using var lifetime = new StoreLifetime();

		using CancellationTokenSource first = lifetime.CreateLinkedCancellation(CancellationToken.None);
		using CancellationTokenSource second = lifetime.CreateLinkedCancellation(CancellationToken.None);
		using CancellationTokenSource third = lifetime.CreateLinkedCancellation(CancellationToken.None);

		using CancellationTokenRegistration firstThrows = first.Token.Register(() =>
			throw new InvalidOperationException("The first callback failed.")
		);

		using CancellationTokenRegistration secondThrows = second.Token.Register(() =>
			throw new InvalidOperationException("The second callback failed.")
		);

		bool thirdCallbackRan = false;

		using CancellationTokenRegistration thirdRuns = third.Token.Register(() => thirdCallbackRan = true);

		lifetime.Cancel();

		// The callbacks run one after another, in an order the framework does not define, so a cancel that stopped at the first throwing
		// callback would leave at least one of the three sources uncanceled.
		Assert.IsTrue(first.IsCancellationRequested);
		Assert.IsTrue(second.IsCancellationRequested);
		Assert.IsTrue(third.IsCancellationRequested);
		Assert.IsTrue(thirdCallbackRan);
	}

	[TestMethod]
	public void Cancel_RunsTheCallbacksOfALinkedTokenOnTheCancelingThread()
	{
		using var lifetime = new StoreLifetime();
		using CancellationTokenSource linked = lifetime.CreateLinkedCancellation(CancellationToken.None);

		int callbackThreadId = -1;

		using CancellationTokenRegistration registration = linked.Token.Register(() => callbackThreadId = Environment.CurrentManagedThreadId);

		lifetime.Cancel();

		Assert.AreEqual(Environment.CurrentManagedThreadId, callbackThreadId);
	}

	[TestMethod]
	public void Dispose_CalledTwice_DoesNotThrow()
	{
		var lifetime = new StoreLifetime();

		lifetime.Dispose();
		lifetime.Dispose();
	}
}
