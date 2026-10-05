namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the disposal handshake: the one teardown claim, the signal that collaborators observe, and the release of later callers.</summary>
[TestClass]
public sealed class ProviderLifetimeTests
{
	[TestMethod]
	public void TryBegin_ClaimsTheTeardownExactlyOnce()
	{
		var disposal = new ProviderLifetime();

		Assert.IsTrue(disposal.TryBegin());
		Assert.IsFalse(disposal.TryBegin());
	}

	[TestMethod]
	public void TryBegin_AloneDoesNotMarkTheProviderDisposed()
	{
		var disposal = new ProviderLifetime();

		// Claiming the teardown and signaling it are two steps, and the collaborators observe the signal.
		Assert.IsTrue(disposal.TryBegin());
		Assert.IsFalse(disposal.IsDisposed);
		Assert.IsFalse(disposal.Token.IsCancellationRequested);
	}

	[TestMethod]
	public void Signal_CancelsTheTokenAndLeavesItLinkable()
	{
		var disposal = new ProviderLifetime();

		Assert.IsFalse(disposal.IsDisposed);
		Assert.IsFalse(disposal.Token.IsCancellationRequested);

		disposal.Signal();
		disposal.Signal();

		Assert.IsTrue(disposal.IsDisposed);
		Assert.IsTrue(disposal.Token.IsCancellationRequested);

		// The source is never disposed, so linking against its token after the cancellation returns a canceled source instead of throwing.
		using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(disposal.Token);

		Assert.IsTrue(linkedSource.IsCancellationRequested);
	}

	[TestMethod]
	public void Complete_ReleasesLaterCallers()
	{
		var disposal = new ProviderLifetime();

		Assert.IsFalse(disposal.Completed.IsCompleted);

		disposal.Complete();
		disposal.Complete();

		Assert.IsTrue(disposal.Completed.IsCompletedSuccessfully);
	}

	[TestMethod]
	public async Task Complete_ReleasesAWaiterThatStartedBeforeIt()
	{
		var disposal = new ProviderLifetime();

		var waiter = Task.Run(async () => await disposal.Completed.ConfigureAwait(false));

		Assert.IsFalse(waiter.IsCompleted);

		disposal.Complete();

		await waiter.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsTrue(waiter.IsCompletedSuccessfully);
	}
}
