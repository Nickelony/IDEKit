namespace Nickelony.IDEKit.Core.Tests;

/// <summary>Covers the keyed gate's supersession, comparer, lease and close rules directly.</summary>
[TestClass]
public sealed class KeyedLatestRequestGateTests
{
	[TestMethod]
	public void Begin_PublishesAFreshLinkedToken()
	{
		var gate = new KeyedLatestRequestGate<string>(StringComparer.Ordinal);
		using var callerSource = new CancellationTokenSource();

		using KeyedRequestLease<string> lease = gate.Begin("a", callerSource.Token);

		Assert.IsFalse(lease.CancellationToken.IsCancellationRequested);

		callerSource.Cancel();

		Assert.IsTrue(lease.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void Begin_ForTheSameKey_CancelsThePreviousRequest()
	{
		var gate = new KeyedLatestRequestGate<string>(StringComparer.Ordinal);

		using KeyedRequestLease<string> first = gate.Begin("a", CancellationToken.None);
		using KeyedRequestLease<string> second = gate.Begin("a", CancellationToken.None);

		Assert.IsTrue(first.CancellationToken.IsCancellationRequested);
		Assert.IsFalse(second.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void Begin_ForAnotherKey_LeavesTheOtherRequestRunning()
	{
		var gate = new KeyedLatestRequestGate<string>(StringComparer.Ordinal);

		using KeyedRequestLease<string> first = gate.Begin("a", CancellationToken.None);
		using KeyedRequestLease<string> second = gate.Begin("b", CancellationToken.None);

		Assert.IsFalse(first.CancellationToken.IsCancellationRequested);
		Assert.IsFalse(second.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void Begin_UnderACaseInsensitiveComparer_CancelsTheRequestOfAKeyThatDiffersOnlyByCase()
	{
		var gate = new KeyedLatestRequestGate<string>(StringComparer.OrdinalIgnoreCase);

		using KeyedRequestLease<string> first = gate.Begin("Case", CancellationToken.None);
		using KeyedRequestLease<string> second = gate.Begin("CASE", CancellationToken.None);

		Assert.IsTrue(first.CancellationToken.IsCancellationRequested);
		Assert.IsFalse(second.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void Begin_UnderACaseSensitiveComparer_LeavesTheRequestOfAKeyThatDiffersOnlyByCaseRunning()
	{
		var gate = new KeyedLatestRequestGate<string>(StringComparer.Ordinal);

		using KeyedRequestLease<string> first = gate.Begin("Case", CancellationToken.None);
		using KeyedRequestLease<string> second = gate.Begin("CASE", CancellationToken.None);

		Assert.IsFalse(first.CancellationToken.IsCancellationRequested);
		Assert.IsFalse(second.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void Dispose_OfASupersededLease_KeepsTheReplacementPublished()
	{
		var gate = new KeyedLatestRequestGate<string>(StringComparer.Ordinal);

		KeyedRequestLease<string> first = gate.Begin("a", CancellationToken.None);
		using KeyedRequestLease<string> second = gate.Begin("a", CancellationToken.None);

		first.Dispose();
		gate.Cancel("a");

		Assert.IsTrue(second.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void Dispose_OfTheCurrentLease_ReleasesTheSlotSoCancelHasNothingToCancel()
	{
		var gate = new KeyedLatestRequestGate<string>(StringComparer.Ordinal);

		KeyedRequestLease<string> lease = gate.Begin("a", CancellationToken.None);

		lease.Dispose();
		gate.Cancel("a");

		Assert.IsFalse(lease.CancellationToken.IsCancellationRequested, "A settled request is not canceled afterwards.");
	}

	[TestMethod]
	public void Cancel_CancelsTheRequestForOneKey()
	{
		var gate = new KeyedLatestRequestGate<string>(StringComparer.Ordinal);

		using KeyedRequestLease<string> lease = gate.Begin("a", CancellationToken.None);

		gate.Cancel("a");

		Assert.IsTrue(lease.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void CancelAll_CancelsEveryRequestAndClosesTheGate()
	{
		var gate = new KeyedLatestRequestGate<string>(StringComparer.Ordinal);

		using KeyedRequestLease<string> first = gate.Begin("a", CancellationToken.None);
		using KeyedRequestLease<string> second = gate.Begin("b", CancellationToken.None);

		gate.CancelAll();

		Assert.IsTrue(first.CancellationToken.IsCancellationRequested);
		Assert.IsTrue(second.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void Begin_AfterCancelAll_ReturnsACanceledLeaseThatHoldsNoSlot()
	{
		var gate = new KeyedLatestRequestGate<string>(StringComparer.Ordinal);

		gate.CancelAll();

		using KeyedRequestLease<string> lease = gate.Begin("a", CancellationToken.None);

		Assert.IsTrue(lease.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void CancelAll_CalledTwice_StaysClosedAndDoesNotThrow()
	{
		var gate = new KeyedLatestRequestGate<string>(StringComparer.Ordinal);

		gate.CancelAll();
		gate.CancelAll();

		using KeyedRequestLease<string> lease = gate.Begin("a", CancellationToken.None);

		Assert.IsTrue(lease.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void CancelAll_FromInsideARequest_Completes()
	{
		var gate = new KeyedLatestRequestGate<string>(StringComparer.Ordinal);

		using KeyedRequestLease<string> lease = gate.Begin("a", CancellationToken.None);

		// The close never waits for a request in flight, so a request that closes its own gate cannot deadlock.
		Task.Run(gate.CancelAll).GetAwaiter().GetResult();

		Assert.IsTrue(lease.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void Dispose_OfTheDefaultLease_DoesNothing()
	{
		KeyedRequestLease<string> lease = default;

		lease.Dispose();

		Assert.IsFalse(lease.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void Begin_CancelAndCancelAll_ContainACancellationCallbackThatThrows()
	{
		var gate = new KeyedLatestRequestGate<string>(StringComparer.Ordinal);

		KeyedRequestLease<string> first = gate.Begin("a", CancellationToken.None);
		first.CancellationToken.Register(static () => throw new InvalidOperationException("callback fault"));

		using KeyedRequestLease<string> second = gate.Begin("a", CancellationToken.None);

		second.CancellationToken.Register(static () => throw new InvalidOperationException("callback fault"));
		gate.Cancel("a");

		using KeyedRequestLease<string> third = gate.Begin("b", CancellationToken.None);

		third.CancellationToken.Register(static () => throw new InvalidOperationException("callback fault"));
		gate.CancelAll();

		Assert.IsTrue(first.CancellationToken.IsCancellationRequested);
		Assert.IsTrue(second.CancellationToken.IsCancellationRequested);
		Assert.IsTrue(third.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public void Constructor_GivenANullComparer_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new KeyedLatestRequestGate<string>(null!));
}
