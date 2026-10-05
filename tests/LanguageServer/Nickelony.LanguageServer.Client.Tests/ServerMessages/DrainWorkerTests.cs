namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class DrainWorkerTests
{
	public TestContext TestContext { get; set; } = null!;

	[TestMethod]
	public async Task RunAsync_AfterABurstOfSignals_RunsOneDrainPass()
	{
		int passes = 0;
		var worker = new DrainWorker(() => Interlocked.Increment(ref passes), CancellationToken.None);

		for (int i = 0; i < 5; i++)
			Assert.IsTrue(worker.TrySignal(), "A write that finds the signal full is dropped and not failed.");

		var loop = Task.Run(worker.RunAsync, TestContext.CancellationToken);

		await TestPolling.UntilAsync(() => Volatile.Read(ref passes) == 1, TestPolling.DefaultTimeout).ConfigureAwait(false);
		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(1, Volatile.Read(ref passes));

		worker.Complete();

		await loop.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task TrySignal_AfterAPassFinished_RunsAnotherPass()
	{
		int passes = 0;
		var worker = new DrainWorker(() => Interlocked.Increment(ref passes), CancellationToken.None);

		var loop = Task.Run(worker.RunAsync, TestContext.CancellationToken);

		worker.TrySignal();

		await TestPolling
			.UntilAsync(() => Volatile.Read(ref passes) == 1 && !worker.HasPassInFlight, TestPolling.DefaultTimeout)
			.ConfigureAwait(false);

		worker.TrySignal();

		await TestPolling
			.UntilAsync(() => Volatile.Read(ref passes) == 2 && !worker.HasPassInFlight, TestPolling.DefaultTimeout)
			.ConfigureAwait(false);

		worker.Complete();

		await loop.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task RunAsync_WhenTheLifetimeIsCanceled_EndsWithoutFaulting()
	{
		using var lifetime = new CancellationTokenSource();

		var worker = new DrainWorker(static () => { }, lifetime.Token);
		var loop = Task.Run(worker.RunAsync, TestContext.CancellationToken);

		await lifetime.CancelAsync().ConfigureAwait(false);
		await loop.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsTrue(loop.IsCompletedSuccessfully);
	}

	[TestMethod]
	public async Task HasPassInFlight_WhileTheDrainRuns_IsTrue()
	{
		var drainEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseDrain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var worker = new DrainWorker(
			() =>
			{
				drainEntered.TrySetResult();
				releaseDrain.Task.GetAwaiter().GetResult();
			},

			CancellationToken.None
		);

		Assert.IsFalse(worker.HasPassInFlight);

		worker.TrySignal();

		var loop = Task.Run(worker.RunAsync, TestContext.CancellationToken);

		try
		{
			await drainEntered.Task.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);
			Assert.IsTrue(worker.HasPassInFlight);
		}
		finally
		{
			releaseDrain.TrySetResult();
		}

		await TestPolling.UntilAsync(() => !worker.HasPassInFlight, TestPolling.DefaultTimeout).ConfigureAwait(false);

		worker.Complete();

		await loop.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task RunAsync_WhenTheDrainThrows_FaultsTheLoopAndLeavesNoPassInFlight()
	{
		var worker = new DrainWorker(static () => throw new InvalidOperationException("Simulated drain failure."), CancellationToken.None);

		worker.TrySignal();

		var loop = Task.Run(worker.RunAsync, TestContext.CancellationToken);

		await Assert
			.ThrowsExactlyAsync<InvalidOperationException>(async () =>
				await loop.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false)
			)
			.ConfigureAwait(false);

		Assert.IsFalse(worker.HasPassInFlight);
	}

	[TestMethod]
	public void Complete_MarksTheWorkerCompletedAndRefusesFurtherSignals()
	{
		var worker = new DrainWorker(static () => { }, CancellationToken.None);

		Assert.IsFalse(worker.IsCompleted);
		Assert.IsTrue(worker.TrySignal());

		worker.Complete();

		Assert.IsTrue(worker.IsCompleted);
		Assert.IsFalse(worker.TrySignal());
	}

	[TestMethod]
	public async Task Complete_WhileAPassRuns_LetsThePassFinishAndEndsTheLoop()
	{
		var drainEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseDrain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		bool firstPassFinished = false;

		var worker = new DrainWorker(
			() =>
			{
				drainEntered.TrySetResult();
				releaseDrain.Task.GetAwaiter().GetResult();
				Volatile.Write(ref firstPassFinished, true);
			},

			CancellationToken.None
		);

		worker.TrySignal();

		var loop = Task.Run(worker.RunAsync, TestContext.CancellationToken);

		await drainEntered.Task.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);

		worker.Complete();

		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsFalse(loop.IsCompleted, "The running pass keeps the loop alive until it finishes.");

		releaseDrain.TrySetResult();

		await loop.WaitAsync(TestPolling.DefaultTimeout, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsTrue(Volatile.Read(ref firstPassFinished));
	}
}
