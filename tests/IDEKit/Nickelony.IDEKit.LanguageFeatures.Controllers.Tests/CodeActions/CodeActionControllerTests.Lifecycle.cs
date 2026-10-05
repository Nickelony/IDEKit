using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;
using Nickelony.Testing;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Covers the controller's request lifecycle: how a newer request, a cancellation, an invalidation, and a disposal supersede an outstanding request,
/// the window between the coordinator's checks and the dispatched publication, and the execution of a chosen action.
/// </summary>
public sealed partial class CodeActionControllerTests
{
	[TestMethod]
	public void RequestAsync_SupersededResult_IsDiscarded()
	{
		using var host = new CodeActionTestHost();
		var completion = new TaskCompletionSource<IReadOnlyList<CodeAction>>(TaskCreationOptions.RunContinuationsAsynchronously);

		host.RequestCodeActionsAsync = (_, _) => completion.Task;

		using CodeActionController controller = host.CreateController();

		Task refresh = controller.RequestAsync();
		host.Dispatcher.PumpUntil(() => host.RequestTokens.Count == 1);

		controller.InvalidateRequests();
		completion.SetResult([CodeActionTestHost.CreateItem("Fix it")]);

		host.RunToCompletion(refresh);

		Assert.IsFalse(controller.HasActions);
	}

	[TestMethod]
	public void RequestAsync_SupersededProviderFailure_DoesNotClearTheNewerState()
	{
		using var host = new CodeActionTestHost();
		var firstCompletion = new TaskCompletionSource<IReadOnlyList<CodeAction>>(TaskCreationOptions.RunContinuationsAsynchronously);

		int requestCount = 0;

		host.RequestCodeActionsAsync = (_, _) =>
			++requestCount == 1 ? firstCompletion.Task : Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionTestHost.CreateItem("second")]);

		using CodeActionController controller = host.CreateController();

		Task firstRefresh = controller.RequestAsync();
		host.Dispatcher.PumpUntil(() => host.RequestTokens.Count == 1);

		// The second refresh admits a newer request that publishes its own actions.
		host.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		// The superseded provider failure must not clear the newer state.
		firstCompletion.SetException(new InvalidOperationException("Stale code action failure."));
		host.RunToCompletion(firstRefresh);

		Assert.IsTrue(controller.HasActions);
		Assert.HasCount(1, controller.Actions);
	}

	[TestMethod]
	public void RequestAsync_VetoedRefresh_DiscardsTheOutstandingRequestResult()
	{
		using var host = new CodeActionTestHost();
		var completion = new TaskCompletionSource<IReadOnlyList<CodeAction>>(TaskCreationOptions.RunContinuationsAsynchronously);

		host.RequestCodeActionsAsync = (_, _) => completion.Task;

		using CodeActionController controller = host.CreateController();

		Task refresh = controller.RequestAsync();
		host.Dispatcher.PumpUntil(() => host.RequestTokens.Count == 1);

		// The refresh finds a vetoing context before it can admit a replacement request, so it concludes
		// without superseding the outstanding request through the coordinator's own admission.
		host.BuildRequest = static _ => null;
		host.RunToCompletion(controller.RequestAsync());

		// A cooperative provider is asked to stop at once, because the refresh supersedes the request itself.
		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		// The outstanding request still completes late with usable actions; the refresh must already have
		// superseded it instead of letting its result publish after the cleared state.
		completion.SetResult([CodeActionTestHost.CreateItem("Fix it")]);
		host.RunToCompletion(refresh);

		Assert.IsFalse(controller.HasActions);
		Assert.IsEmpty(controller.Actions);
	}

	[TestMethod]
	public void RequestAsync_ThrowingRequestBuilder_DiscardsTheOutstandingRequestResult()
	{
		using var host = new CodeActionTestHost();
		var completion = new TaskCompletionSource<IReadOnlyList<CodeAction>>(TaskCreationOptions.RunContinuationsAsynchronously);

		host.RequestCodeActionsAsync = (_, _) => completion.Task;

		using CodeActionController controller = host.CreateController();

		Task refresh = controller.RequestAsync();
		host.Dispatcher.PumpUntil(() => host.RequestTokens.Count == 1);

		// The refresh fails while building the request, before it can admit a replacement request.
		host.BuildRequest = static _ => throw new InvalidOperationException("Request building failed.");
		host.RunToCompletion(controller.RequestAsync());

		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		completion.SetResult([CodeActionTestHost.CreateItem("Fix it")]);
		host.RunToCompletion(refresh);

		Assert.IsFalse(controller.HasActions);
		Assert.IsEmpty(controller.Actions);
	}

	[TestMethod]
	public void CancelInFlightRequest_InFlightRequest_CancelsTheTokenAndRejectsTheResult()
	{
		using var host = new CodeActionTestHost();
		var completion = new TaskCompletionSource<IReadOnlyList<CodeAction>>(TaskCreationOptions.RunContinuationsAsynchronously);

		host.RequestCodeActionsAsync = (_, _) => completion.Task;

		using CodeActionController controller = host.CreateController();

		Task refresh = controller.RequestAsync();
		host.Dispatcher.PumpUntil(() => host.RequestTokens.Count == 1);

		controller.CancelInFlightRequest();

		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		completion.SetResult([CodeActionTestHost.CreateItem("Fix it")]);
		host.RunToCompletion(refresh);

		// The canceled request's result is rejected even though the provider ignored the cancellation.
		Assert.IsFalse(controller.HasActions);
	}

	[TestMethod]
	public void PublicOperations_AfterDisposal_ReturnSafeDefaultsAndDoNotThrow()
	{
		using var host = new CodeActionTestHost();
		CodeActionController controller = host.CreateController();

		controller.Dispose();

		controller.CancelScheduledRequest();
		controller.CancelInFlightRequest();
		controller.InvalidateRequests();

		controller.RequestAsync().GetAwaiter().GetResult();
		controller.ExecuteActionAsync(CodeActionTestHost.CreateItem("Fix it")).GetAwaiter().GetResult();

		Assert.IsFalse(controller.HasActions);
		Assert.IsEmpty(controller.Actions);
		Assert.IsEmpty(host.Executed);
	}

	[TestMethod]
	public void PublicOperations_FromAnotherThreadAfterDisposal_AreQuietNoOps()
	{
		using var host = new CodeActionTestHost();
		CodeActionController controller = host.CreateController();

		controller.Dispose();

		// After disposal every member returns before the dispatcher-affinity check, so the calls are safe from any thread.
		Exception? failure = CaptureOffThreadFailure(() =>
		{
			controller.CancelScheduledRequest();
			controller.CancelInFlightRequest();
			controller.InvalidateRequests();

			controller.RequestAsync().GetAwaiter().GetResult();

			controller.ExecuteActionAsync(CodeActionTestHost.CreateItem("Fix it")).GetAwaiter().GetResult();

			controller.Dispose();
		});

		Assert.IsNull(failure);
	}

	[TestMethod]
	public void InteractiveMembers_OffTheDispatcherThread_FailFast()
	{
		using var host = new CodeActionTestHost();

		using CodeActionController controller = host.CreateController();

		Task? request = null;
		Task? execute = null;

		Exception? failure = CaptureOffThreadFailure(() =>
		{
			request = controller.RequestAsync();
			execute = controller.ExecuteActionAsync(CodeActionTestHost.CreateItem("Fix it"));
		});

		// The asynchronous members report the thread violation through the task they return.
		Assert.IsNull(failure);
		Assert.IsNotNull(request);
		Assert.IsNotNull(execute);
		Assert.IsInstanceOfType<InvalidOperationException>(request.Exception?.InnerException);
		Assert.IsInstanceOfType<InvalidOperationException>(execute.Exception?.InnerException);
		Assert.IsEmpty(host.Executed);
	}

	[TestMethod]
	public void RequestAsync_DisposalLandsBeforeTheQueuedPublication_ShowsNothing()
	{
		using var host = new CodeActionTestHost(installSynchronizationContext: false);

		StaleResultScenario scenario = StartStaleResultScenario(host);

		// The disposal lands between the coordinator's checks and the queued step, which must not publish after it.
		scenario.Controller.Dispose();
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted);

		Assert.IsFalse(scenario.Controller.HasActions);
		Assert.IsEmpty(scenario.Controller.Actions);
	}

	[TestMethod]
	public void RequestAsync_CancellationLandsBeforeTheQueuedPublication_ShowsNothing()
	{
		using var host = new CodeActionTestHost(installSynchronizationContext: false);

		StaleResultScenario scenario = StartStaleResultScenario(host);

		scenario.Controller.CancelInFlightRequest();
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted);

		Assert.IsFalse(scenario.Controller.HasActions);

		scenario.Controller.Dispose();
	}

	[TestMethod]
	public void RequestAsync_InvalidationLandsBeforeTheQueuedPublication_ShowsNothing()
	{
		using var host = new CodeActionTestHost(installSynchronizationContext: false);

		StaleResultScenario scenario = StartStaleResultScenario(host);

		scenario.Controller.InvalidateRequests();
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted);

		Assert.IsFalse(scenario.Controller.HasActions);

		scenario.Controller.Dispose();
	}

	[TestMethod]
	public void RequestAsync_ContextChangeLandsBeforeTheQueuedPublication_ShowsNothing()
	{
		using var host = new CodeActionTestHost(installSynchronizationContext: false);

		StaleResultScenario scenario = StartStaleResultScenario(host);

		// A caret move in the same window makes the result stale, even though the coordinator had already accepted it.
		host.Source.MoveCaret(2);
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted);

		Assert.IsFalse(scenario.Controller.HasActions);

		scenario.Controller.Dispose();
	}

	[TestMethod]
	public void RequestAsync_DocumentReplacementLandsBeforeTheQueuedPublication_ShowsNothing()
	{
		using var host = new CodeActionTestHost(installSynchronizationContext: false);

		StaleResultScenario scenario = StartStaleResultScenario(host);

		// The actions were computed for the previous document and must not describe the one that replaced it.
		host.Source.ReplaceDocument("alpha\nbeta");
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted);

		Assert.IsFalse(scenario.Controller.HasActions);
		Assert.IsEmpty(scenario.Controller.Actions);

		scenario.Controller.Dispose();
	}

	[TestMethod]
	public void RequestAsync_NewerRequestStartsBeforeTheQueuedPublication_PublishesOnlyTheNewerResult()
	{
		var secondCompletion = new TaskCompletionSource<IReadOnlyList<CodeAction>>(TaskCreationOptions.RunContinuationsAsynchronously);
		CodeAction second = CodeActionTestHost.CreateItem("second");

		using var host = new CodeActionTestHost(installSynchronizationContext: false);

		StaleResultScenario scenario = StartStaleResultScenario(host, onSecondRequest: () => secondCompletion.Task);

		int changedCount = 0;
		scenario.Controller.Changed += (_, _) => changedCount++;

		// The newer request supersedes the result that waits in the queue; only the newer result may publish.
		Task secondRequest = scenario.Controller.RequestAsync();

		secondCompletion.SetResult([second]);
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted && secondRequest.IsCompleted);

		Assert.HasCount(1, scenario.Controller.Actions);
		Assert.AreSame(second, scenario.Controller.Actions[0]);
		Assert.AreEqual(1, changedCount);

		scenario.Controller.Dispose();
	}

	[TestMethod]
	public void RequestAsync_NewerRequestPublishesBeforeTheQueuedFailureClear_KeepsTheNewerState()
	{
		var logger = new RecordingLogger();

		using var host = new CodeActionTestHost(installSynchronizationContext: false);

		// The failure passed the coordinator's checks and its state clear waits in the queue; a newer request that publishes in the meantime
		// must not be cleared by it.
		StaleResultScenario scenario = StartStaleResultScenario(host, failure: new InvalidOperationException("Provider failed."), logger: logger);

		host.RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem();

		Task newer = scenario.Controller.RequestAsync();
		host.Dispatcher.PumpUntil(() => scenario.Request.IsCompleted && newer.IsCompleted);

		Assert.IsTrue(scenario.Controller.HasActions);
		Assert.AreEqual(1030, logger.Entries[0].EventId.Id);

		scenario.Controller.Dispose();
	}

	[TestMethod]
	public void RequestAsync_CancellationLandsBeforeTheQueuedFailureClear_KeepsThePublishedState()
	{
		var logger = new RecordingLogger();
		var completion = new TaskCompletionSource<IReadOnlyList<CodeAction>>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var host = new CodeActionTestHost(installSynchronizationContext: false)
		{
			RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem(),
		};

		using CodeActionController controller = host.CreateController(logger: logger);

		host.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		host.RequestCodeActionsAsync = (_, _) => completion.Task;

		Task refresh = controller.RequestAsync();
		host.Dispatcher.PumpUntil(() => host.RequestTokens.Count == 2);

		// The provider fails on a pool thread, and the state clear of that failure waits in the dispatcher's queue.
		completion.SetException(new InvalidOperationException("Provider failed."));
		Assert.IsTrue(SpinWait.SpinUntil(() => host.Dispatcher.QueuedCount == 1, TimeSpan.FromSeconds(5.0)));

		// The cancellation lands first, so the failed request does not own the state and its clear changes nothing.
		controller.CancelInFlightRequest();
		host.Dispatcher.PumpUntil(() => refresh.IsCompleted);

		Assert.IsTrue(controller.HasActions);
		Assert.AreEqual(1030, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void RequestAsync_FailureClearQueuedWhileCurrent_ClearsTheStateOnTheDispatcherThread()
	{
		using var host = new CodeActionTestHost(installSynchronizationContext: false);

		int clearThreadId = 0;

		// Publish one set first so the failing request has a state to clear.
		CodeActionController controller = host.CreateController();
		host.RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem();
		host.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		controller.Changed += (_, _) => clearThreadId = Environment.CurrentManagedThreadId;

		var completion = new TaskCompletionSource<IReadOnlyList<CodeAction>>(TaskCreationOptions.RunContinuationsAsynchronously);
		host.RequestCodeActionsAsync = (_, _) => completion.Task;

		Task request = controller.RequestAsync();
		host.Dispatcher.PumpUntil(() => host.RequestTokens.Count == 2);

		completion.SetException(new InvalidOperationException("Provider failed."));
		host.Dispatcher.PumpUntil(() => request.IsCompleted);

		Assert.IsFalse(controller.HasActions);
		Assert.AreEqual(host.Dispatcher.OwnerThreadId, clearThreadId);

		controller.Dispose();
	}

	[TestMethod]
	public void RequestAsync_DispatcherShutsDownBeforeThePublication_CompletesWithoutFaultingOrLogging()
	{
		var logger = new RecordingLogger();

		using var host = new CodeActionTestHost(installSynchronizationContext: false);

		StaleResultScenario scenario = StartStaleResultScenario(host, logger: logger);

		// The dispatcher rejects the queued publication; the controller treats that as the end of the request, not as a failure.
		host.Dispatcher.Dispose();

		Assert.IsTrue(SpinWait.SpinUntil(() => scenario.Request.IsCompleted, TimeSpan.FromSeconds(5.0)));
		scenario.Request.GetAwaiter().GetResult();

		Assert.IsFalse(scenario.Controller.HasActions);
		Assert.IsEmpty(logger.Entries);
	}

	[TestMethod]
	public void RequestAsync_CanceledByTheCaller_ThenTheProviderFails_LogsOnceAndKeepsThePublishedState()
	{
		var logger = new RecordingLogger();
		var completion = new TaskCompletionSource<IReadOnlyList<CodeAction>>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };
		using CodeActionController controller = host.CreateController(logger: logger);

		host.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		using var cancellation = new CancellationTokenSource();

		host.RequestCodeActionsAsync = (_, _) => completion.Task;

		Task refresh = controller.RequestAsync(cancellation.Token);
		host.Dispatcher.PumpUntil(() => host.RequestTokens.Count == 2);

		// The caller gives up while the provider works, and the provider then fails instead of honoring the cancellation. The coordinator does not
		// report a failure of a canceled run, so only the controller's own log entry remains, and the snapshot of the earlier request stays.
		cancellation.Cancel();
		completion.SetException(new InvalidOperationException("Provider failed."));
		host.RunToCompletion(refresh);

		Assert.IsTrue(controller.HasActions);
		Assert.HasCount(1, logger.Entries);
		Assert.AreEqual(1030, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void ExecuteActionAsync_RunsTheHookWithTheActionAndTheLifetimeToken()
	{
		using var host = new CodeActionTestHost();

		object payload = new();
		CodeAction item = CodeActionTestHost.CreateItem("Fix it", payload: payload);

		using CodeActionController controller = host.CreateController();

		Publish(host, controller, item);
		host.RunToCompletion(controller.ExecuteActionAsync(item));

		Assert.HasCount(1, host.Executed);
		Assert.AreSame(item, host.Executed[0]);
		Assert.AreSame(payload, host.Executed[0].Data);
		Assert.IsFalse(host.ExecuteTokens[0].IsCancellationRequested);
	}

	[TestMethod]
	public void ExecuteActionAsync_TokenIsCanceledByDisposal()
	{
		using var host = new CodeActionTestHost();
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		host.ExecuteActionAsync = _ => gate.Task;

		CodeActionController controller = host.CreateController();
		CodeAction item = CodeActionTestHost.CreateItem("Fix it");

		Publish(host, controller, item);

		Task execution = controller.ExecuteActionAsync(item);

		controller.Dispose();

		// The hook received the lifetime token, so a host can stop work that would outlive the controller.
		Assert.IsTrue(host.ExecuteTokens[0].IsCancellationRequested);

		gate.SetResult();
		host.RunToCompletion(execution);
	}

	[TestMethod]
	public void ExecuteActionAsync_ThrowingHook_IsContainedAndLogged()
	{
		var logger = new RecordingLogger();

		using var host = new CodeActionTestHost { ExecuteActionAsync = static _ => throw new InvalidOperationException("Execute failed.") };

		using CodeActionController controller = host.CreateController(logger: logger);
		CodeAction item = CodeActionTestHost.CreateItem("Fix it");

		Publish(host, controller, item);
		host.RunToCompletion(controller.ExecuteActionAsync(item));

		Assert.HasCount(1, host.Executed);
		Assert.HasCount(1, logger.Entries);
		Assert.AreEqual(1031, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void ExecuteActionAsync_NullAction_FaultsTheReturnedTask()
	{
		using var host = new CodeActionTestHost();
		using CodeActionController controller = host.CreateController();

		Task execution = controller.ExecuteActionAsync(null!);

		Assert.IsTrue(execution.IsFaulted);
		Assert.IsInstanceOfType<ArgumentNullException>(execution.Exception?.InnerException);
	}

	// Starts a request whose provider answers on a thread-pool thread and waits until the controller's publication of that answer is queued on the
	// dispatcher, so the test can interrupt the request in the window between the coordinator's checks and the dispatched step. A failure makes the
	// provider fault instead of answering, which queues the state clear.
	private static StaleResultScenario StartStaleResultScenario(
		CodeActionTestHost host,
		Func<Task<IReadOnlyList<CodeAction>>>? onSecondRequest = null,
		Exception? failure = null,
		RecordingLogger? logger = null
	)
	{
		var completion = new TaskCompletionSource<IReadOnlyList<CodeAction>>(TaskCreationOptions.RunContinuationsAsynchronously);

		int requestCount = 0;

		host.RequestCodeActionsAsync = (_, _) => ++requestCount == 1 ? completion.Task : onSecondRequest?.Invoke() ?? completion.Task;

		CodeActionController controller = host.CreateController(logger: logger);
		Task request = controller.RequestAsync();

		// Without a synchronization context the result resumes on a thread-pool thread, whose publication waits in the dispatcher's queue.
		host.Dispatcher.PumpUntil(() => host.RequestTokens.Count == 1);

		if (failure is null)
			completion.SetResult([CodeActionTestHost.CreateItem("Fix it")]);
		else
			completion.SetException(failure);

		Assert.IsTrue(SpinWait.SpinUntil(() => host.Dispatcher.QueuedCount == 1, TimeSpan.FromSeconds(5.0)));

		return new StaleResultScenario(controller, request);
	}

	private readonly record struct StaleResultScenario(CodeActionController Controller, Task Request);
}
