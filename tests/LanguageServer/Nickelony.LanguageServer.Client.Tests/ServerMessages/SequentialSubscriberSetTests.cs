using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class SequentialSubscriberSetTests
{
	private static readonly string s_testDocumentUri = TestPaths.FileUri("test.ext");

	[TestMethod]
	public async Task DiagnosticsQueue_ConcurrentOlderReplacementCannotOverwriteLatestPayload()
	{
		var documentKey = DiagnosticsDocumentKey.FromUri(s_testDocumentUri);

		var firstInvocationEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstInvocationToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var olderPayloadRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowOlderReplacement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondPayloadObserved = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

		int invocationCount = 0;

		var subscribers = new SequentialDiagnosticsSubscriberSet<Action<PublishDiagnosticsParams>>(
			(handler, parameters) => handler(parameters),
			_ => { },

			testHooks: new LanguageServerClientTestHooks
			{
				BeforePendingPayloadReplacement = parameters =>
				{
					if (parameters.Diagnostics?[0].Message is "Older warning.")
					{
						olderPayloadRead.TrySetResult(true);
						allowOlderReplacement.Task.GetAwaiter().GetResult();
					}
				},
			}
		);

		subscribers.Add(parameters =>
		{
			int currentInvocation = Interlocked.Increment(ref invocationCount);

			if (currentInvocation == 1)
			{
				firstInvocationEntered.TrySetResult(true);
				allowFirstInvocationToFinish.Task.GetAwaiter().GetResult();

				return;
			}

			secondPayloadObserved.TrySetResult(parameters.Diagnostics?[0].Message);
		});

		subscribers.Dispatch(documentKey, ServerMessageTestHelpers.CreateDiagnostics(s_testDocumentUri, "First warning."));
		await firstInvocationEntered.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false);

		// Keep an intermediate payload pending while the first callback is blocked.
		subscribers.Dispatch(documentKey, ServerMessageTestHelpers.CreateDiagnostics(s_testDocumentUri, "Intermediate warning."));

		var olderDispatch = Task.Run(
			() => subscribers.Dispatch(documentKey, ServerMessageTestHelpers.CreateDiagnostics(s_testDocumentUri, "Older warning.")),
			TestContext.CancellationToken
		);

		await olderPayloadRead.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false);

		// The latest payload should replace the paused older update.
		subscribers.Dispatch(documentKey, ServerMessageTestHelpers.CreateDiagnostics(s_testDocumentUri, "Latest warning."));
		allowOlderReplacement.TrySetResult(true);
		await olderDispatch.ConfigureAwait(false);

		allowFirstInvocationToFinish.TrySetResult(true);

		Assert.AreEqual(
			"Latest warning.",
			await secondPayloadObserved.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false)
		);

		Assert.AreEqual(2, Volatile.Read(ref invocationCount));
	}

	[TestMethod]
	public async Task DiagnosticsQueue_AfterComplete_DropsPendingPayload()
	{
		var documentKey = DiagnosticsDocumentKey.FromUri("untitled:Doc");

		var firstInvocationEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstInvocationToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var unexpectedSecondInvocation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int invocationCount = 0;

		var subscribers = new SequentialDiagnosticsSubscriberSet<Action<PublishDiagnosticsParams>>(
			(handler, parameters) => handler(parameters),
			_ => { }
		);

		subscribers.Add(_ =>
		{
			if (Interlocked.Increment(ref invocationCount) > 1)
			{
				unexpectedSecondInvocation.TrySetResult(true);
				return;
			}

			firstInvocationEntered.TrySetResult(true);
			allowFirstInvocationToFinish.Task.GetAwaiter().GetResult();
		});

		subscribers.Dispatch(documentKey, ServerMessageTestHelpers.CreateDiagnostics(s_testDocumentUri, "First warning."));
		await firstInvocationEntered.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.CancellationToken).ConfigureAwait(false);

		// The second payload stays pending while the first handler is still running.
		subscribers.Dispatch(documentKey, ServerMessageTestHelpers.CreateDiagnostics(s_testDocumentUri, "Queued warning."));

		subscribers.Complete();
		subscribers.Add(_ => unexpectedSecondInvocation.TrySetResult(true));
		subscribers.Dispatch(documentKey, ServerMessageTestHelpers.CreateDiagnostics(s_testDocumentUri, "Late warning."));
		allowFirstInvocationToFinish.TrySetResult(true);

		// A completed set stops reporting its subscriptions, so the busy handler's drain is observed through an absence window.
		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsFalse(unexpectedSecondInvocation.Task.IsCompleted);
		Assert.AreEqual(1, Volatile.Read(ref invocationCount));
	}

	[TestMethod]
	public async Task SignalQueue_AfterComplete_DoesNotInvokeHandler()
	{
		var invocationObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int invocationCount = 0;

		var subscribers = new SequentialSignalSubscriberSet<Action>(handler => handler(), _ => { });

		subscribers.Add(() =>
		{
			Interlocked.Increment(ref invocationCount);
			invocationObserved.TrySetResult(true);
		});

		subscribers.Complete();

		subscribers.Add(() =>
		{
			Interlocked.Increment(ref invocationCount);
			invocationObserved.TrySetResult(true);
		});

		subscribers.Dispatch();

		await TestPolling.UntilAsync(() => subscribers.IsIdle, TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsFalse(invocationObserved.Task.IsCompleted);
		Assert.AreEqual(0, Volatile.Read(ref invocationCount));
	}

	[TestMethod]
	public async Task DiagnosticsQueue_DeliversOneCallbackAtATimeInEnqueueOrder()
	{
		var firstInvocationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstInvocationToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var received = new ConcurrentQueue<string?>();

		int activeInvocations = 0;
		int overlappingInvocations = 0;

		var subscribers = new SequentialDiagnosticsSubscriberSet<Action<PublishDiagnosticsParams>>(
			(handler, parameters) => handler(parameters),
			_ => { }
		);

		subscribers.Add(parameters =>
		{
			if (Interlocked.Increment(ref activeInvocations) > 1)
				Interlocked.Increment(ref overlappingInvocations);

			received.Enqueue(ServerMessageTestHelpers.FirstMessage(parameters));

			if (received.Count == 1)
			{
				firstInvocationEntered.TrySetResult();
				allowFirstInvocationToFinish.Task.GetAwaiter().GetResult();
			}

			Interlocked.Decrement(ref activeInvocations);
		});

		subscribers.Dispatch(
			DiagnosticsDocumentKey.FromUri("untitled:Blocking"),
			ServerMessageTestHelpers.CreateDiagnostics(s_testDocumentUri, "Blocking")
		);

		try
		{
			await firstInvocationEntered.Task.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

			// The queued payloads wait behind the running handler and then drain in the order they were queued, which is the
			// reverse of the order of their keys, so neither the dictionary order nor a key order can produce it.
			foreach (string name in new[] { "F", "E", "D", "C", "B", "A" })
			{
				subscribers.Dispatch(
					DiagnosticsDocumentKey.FromUri("untitled:" + name),
					ServerMessageTestHelpers.CreateDiagnostics(s_testDocumentUri, name)
				);
			}
		}
		finally
		{
			allowFirstInvocationToFinish.TrySetResult();
		}

		await TestPolling.UntilAsync(() => subscribers.IsIdle && received.Count == 7, TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.AreEqual("Blocking|F|E|D|C|B|A", string.Join("|", received));
		Assert.AreEqual(0, Volatile.Read(ref overlappingInvocations));
	}

	[TestMethod]
	public async Task SignalQueue_CollapsesTheSignalsRaisedWhileTheHandlerRuns()
	{
		var firstInvocationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstInvocationToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		int invocationCount = 0;

		var subscribers = new SequentialSignalSubscriberSet<Action>(handler => handler(), _ => { });

		subscribers.Add(() =>
		{
			if (Interlocked.Increment(ref invocationCount) != 1)
				return;

			firstInvocationEntered.TrySetResult();
			allowFirstInvocationToFinish.Task.GetAwaiter().GetResult();
		});

		subscribers.Dispatch();

		try
		{
			await firstInvocationEntered.Task.WaitAsync(TestPolling.DefaultTimeout).ConfigureAwait(false);

			// Three signals arrive while the handler runs; they cause exactly one further invocation.
			subscribers.Dispatch();
			subscribers.Dispatch();
			subscribers.Dispatch();
		}
		finally
		{
			allowFirstInvocationToFinish.TrySetResult();
		}

		await TestPolling.UntilAsync(() => subscribers.IsIdle, TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.AreEqual(2, Volatile.Read(ref invocationCount));
	}

	[TestMethod]
	public void RemoveAndComplete_ReleaseTheSubscribersOfBothSets()
	{
		var signals = new SequentialSignalSubscriberSet<Action>(handler => handler(), _ => { });

		var diagnostics = new SequentialDiagnosticsSubscriberSet<Action<PublishDiagnosticsParams>>(
			(handler, parameters) => handler(parameters),
			_ => { }
		);

		WeakReference removedSignalSubscriber = SubscribeSignal(signals, remove: true);
		WeakReference removedDiagnosticsSubscriber = SubscribeDiagnostics(diagnostics, remove: true);
		WeakReference completedSignalSubscriber = SubscribeSignal(signals, remove: false);
		WeakReference completedDiagnosticsSubscriber = SubscribeDiagnostics(diagnostics, remove: false);

		CollectGarbage();

		// A set that kept a removed subscriber would hold its handler and the object the handler captured.
		Assert.IsFalse(removedSignalSubscriber.IsAlive, "The signal set kept a removed subscriber.");
		Assert.IsFalse(removedDiagnosticsSubscriber.IsAlive, "The diagnostics set kept a removed subscriber.");

		// A subscriber that is still subscribed stays reachable, so the weak references above are not collected for another reason.
		Assert.IsTrue(completedSignalSubscriber.IsAlive, "The signal set dropped a subscriber that was still subscribed.");
		Assert.IsTrue(completedDiagnosticsSubscriber.IsAlive, "The diagnostics set dropped a subscriber that was still subscribed.");

		signals.Complete();
		diagnostics.Complete();

		CollectGarbage();

		Assert.IsFalse(completedSignalSubscriber.IsAlive, "The signal set kept a subscriber after it completed.");
		Assert.IsFalse(completedDiagnosticsSubscriber.IsAlive, "The diagnostics set kept a subscriber after it completed.");
	}

	private static void CollectGarbage()
	{
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
	}

	// The handlers are created in their own frames, so no local of the test keeps a subscriber alive.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference SubscribeSignal(SequentialSignalSubscriberSet<Action> subscribers, bool remove)
	{
		object subscriber = new();
		void handler() => GC.KeepAlive(subscriber);

		subscribers.Add(handler);

		if (remove)
			subscribers.Remove(handler);

		return new WeakReference(subscriber);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference SubscribeDiagnostics(SequentialDiagnosticsSubscriberSet<Action<PublishDiagnosticsParams>> subscribers, bool remove)
	{
		object subscriber = new();
		void handler(PublishDiagnosticsParams _) => GC.KeepAlive(subscriber);

		subscribers.Add(handler);

		if (remove)
			subscribers.Remove(handler);

		return new WeakReference(subscriber);
	}

	public TestContext? TestContext { get; set; }
}
