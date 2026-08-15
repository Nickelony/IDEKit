using System.Collections.Concurrent;

namespace Nickelony.IDEKit.Core.Tests;

public sealed partial class LatestRequestCoordinatorTests
{
	[TestMethod]
	[Timeout(30_000)]
	public void RunAsync_PublishesOnTheCapturedSynchronizationContext()
	{
		// The continuation, including canApply and apply, must run on the caller's captured
		// synchronization context (the only ConfigureAwait(true) in Core), so a host that starts a
		// request on a UI thread also publishes its result on that thread. The scenario runs on a
		// single-threaded pump so the assertion cannot be satisfied by an inline continuation.
		using var pump = new SingleThreadSynchronizationContext();
		using var completed = new ManualResetEventSlim();
		RequestOutcome outcome = default;
		int? canApplyThreadId = null;
		int? applyThreadId = null;
		Exception? failure = null;

		pump.Post(
			async _ =>
			{
				try
				{
					var coordinator = new LatestRequestCoordinator();
					var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

					Task<RequestOutcome> request = coordinator.RunAsync(
						start,
						static (state, token) => state.Task,
						(state, result) =>
						{
							canApplyThreadId = Environment.CurrentManagedThreadId;
							return true;
						},
						_ => applyThreadId = Environment.CurrentManagedThreadId);

					// The queued completion forces the continuation to marshal back to the context
					// instead of running inline on the completing thread.
					start.SetResult(42);

					outcome = await request;
				}
				catch (Exception exception)
				{
					failure = exception;
				}
				finally
				{
					completed.Set();
				}
			},
			null);

		Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(30.0)), "The request did not complete in time.");
		Assert.IsNull(failure, failure?.ToString());
		Assert.AreEqual(RequestOutcome.Completed, outcome);
		Assert.AreEqual(pump.ThreadId, canApplyThreadId);
		Assert.AreEqual(pump.ThreadId, applyThreadId);
	}

	[TestMethod]
	[Timeout(30_000)]
	public void RunAsync_ContinueOnCapturedContextFalse_ResumesOffTheCapturedContext()
	{
		// The documented thread-pool escape hatch: with continueOnCapturedContext set to false the
		// continuation - including canApply and apply - must not resume on the captured context.
		using var pump = new SingleThreadSynchronizationContext();
		using var completed = new ManualResetEventSlim();
		RequestOutcome outcome = default;
		int? canApplyThreadId = null;
		int? applyThreadId = null;
		Exception? failure = null;

		pump.Post(
			async _ =>
			{
				try
				{
					var coordinator = new LatestRequestCoordinator();
					var start = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

					Task<RequestOutcome> request = coordinator.RunAsync(
						start,
						static (state, token) => state.Task,
						(state, result) =>
						{
							canApplyThreadId = Environment.CurrentManagedThreadId;
							return true;
						},
						_ => applyThreadId = Environment.CurrentManagedThreadId,
						continueOnCapturedContext: false);

					start.SetResult(42);

					outcome = await request;
				}
				catch (Exception exception)
				{
					failure = exception;
				}
				finally
				{
					completed.Set();
				}
			},
			null);

		Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(30.0)), "The request did not complete in time.");
		Assert.IsNull(failure, failure?.ToString());
		Assert.AreEqual(RequestOutcome.Completed, outcome);
		Assert.AreNotEqual(pump.ThreadId, canApplyThreadId);
		Assert.AreNotEqual(pump.ThreadId, applyThreadId);
	}

	// A single-threaded synchronization context that installs itself on its pump thread and runs
	// every posted callback there, so a captured-context continuation can only resume on that thread.
	private sealed class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable
	{
		private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
		private readonly Thread _thread;

		public SingleThreadSynchronizationContext()
		{
			_thread = new Thread(Pump) { IsBackground = true, Name = "Synchronization context pump" };
			_thread.Start();
		}

		public int ThreadId => _thread.ManagedThreadId;

		public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

		public override void Send(SendOrPostCallback d, object? state)
			=> throw new NotSupportedException("The pump context only supports asynchronous posts.");

		public void Dispose()
		{
			_queue.CompleteAdding();

			if (Environment.CurrentManagedThreadId != _thread.ManagedThreadId)
				_thread.Join();
		}

		private void Pump()
		{
			// The context must be current on the pump thread for a continuation to capture it.
			SetSynchronizationContext(this);

			foreach ((SendOrPostCallback callback, object? state) in _queue.GetConsumingEnumerable())
				callback(state);
		}
	}
}
