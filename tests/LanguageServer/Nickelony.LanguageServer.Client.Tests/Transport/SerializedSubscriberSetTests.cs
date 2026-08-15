namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class SerializedSubscriberSetTests
{
	[TestMethod]
	public async Task DiagnosticsQueue_ConcurrentOlderReplacementCannotOverwriteLatestPayload()
	{
		var documentKey = DiagnosticsDocumentKey.FromUri("file:///C:/Workspace/test.ext");

		var firstInvocationEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowFirstInvocationToFinish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var olderPayloadRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var allowOlderReplacement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondPayloadObserved = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		int invocationCount = 0;

		var subscribers = new SerializedDiagnosticsSubscriberSet<Action<PublishDiagnosticsParams>>(
			(handler, parameters) => handler(parameters),
			_ => { },
			testHooks: new ClientTestHooks
			{
				BeforePendingPayloadReplacement = parameters =>
				{
					if (parameters.Diagnostics?[0].Message is "Older warning.")
					{
						olderPayloadRead.TrySetResult(true);
						allowOlderReplacement.Task.GetAwaiter().GetResult();
					}
				}
			});

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

		subscribers.Dispatch(documentKey, CreateDiagnostics("First warning."));
		await firstInvocationEntered.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

		// Keep an intermediate payload pending while the first callback is blocked.
		subscribers.Dispatch(documentKey, CreateDiagnostics("Intermediate warning."));

		Task olderDispatch = Task.Run(() => subscribers.Dispatch(documentKey, CreateDiagnostics("Older warning.")));
		await olderPayloadRead.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

		// The latest payload should replace the paused older update.
		subscribers.Dispatch(documentKey, CreateDiagnostics("Latest warning."));
		allowOlderReplacement.TrySetResult(true);
		await olderDispatch.ConfigureAwait(false);

		allowFirstInvocationToFinish.TrySetResult(true);

		Assert.AreEqual("Latest warning.",
			await secondPayloadObserved.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false));

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

		var subscribers = new SerializedDiagnosticsSubscriberSet<Action<PublishDiagnosticsParams>>(
			(handler, parameters) => handler(parameters),
			_ => { });

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

		subscribers.Dispatch(documentKey, CreateDiagnostics("First warning."));
		await firstInvocationEntered.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

		// The second payload stays pending while the first handler is still running.
		subscribers.Dispatch(documentKey, CreateDiagnostics("Queued warning."));

		subscribers.Complete();
		subscribers.Add(_ => unexpectedSecondInvocation.TrySetResult(true));
		subscribers.Dispatch(documentKey, CreateDiagnostics("Late warning."));
		allowFirstInvocationToFinish.TrySetResult(true);

		await TestPolling.UntilAsync(() => subscribers.IsIdle, TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.IsFalse(unexpectedSecondInvocation.Task.IsCompleted);
		Assert.AreEqual(1, Volatile.Read(ref invocationCount));
	}

	[TestMethod]
	public async Task SignalQueue_AfterComplete_DoesNotInvokeHandler()
	{
		var invocationObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		int invocationCount = 0;

		var subscribers = new SerializedSignalSubscriberSet<Action>(handler => handler(), _ => { });

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

	private static PublishDiagnosticsParams CreateDiagnostics(string message) => new(
		"file:///C:/Workspace/test.ext",
		null,
		[new DiagnosticPayload(null, null, message, "test", null)]);
}
