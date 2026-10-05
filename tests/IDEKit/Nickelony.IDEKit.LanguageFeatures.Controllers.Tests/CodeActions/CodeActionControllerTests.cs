using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;
using Nickelony.Testing;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Verifies the request, publication, and state behavior of the code-action controller; the context-change and lifecycle concerns live
/// in the sibling partials.
/// </summary>
/// <remarks>
/// The tests share <see cref="CodeActionTestHost"/>, which scripts the selection source and records what the controller asked the host.
/// A test that awaits a pending provider pumps the dispatcher instead, because the host installs its synchronization context.
/// </remarks>
[TestClass]
public sealed partial class CodeActionControllerTests
{
	[TestMethod]
	public void RequestAsync_WithSelection_BuildsTheContextAndRequestsTheSelectionRange()
	{
		using var host = new CodeActionTestHost();
		using CodeActionController controller = host.CreateController();

		host.Source.Select(2, 3);

		host.RunToCompletion(controller.RequestAsync());

		CodeActionSelection context = host.Contexts[^1];

		Assert.AreEqual("one\ntwo\nthree", context.GetText());
		Assert.AreEqual(new TextRange(2, 3), context.Range);
		Assert.AreEqual(new CodeActionRequest(CodeActionTestHost.DocumentId, "one\ntwo\nthree", new TextRange(2, 3)), host.Requests[0]);
	}

	[TestMethod]
	public void RequestAsync_WithActions_PublishesTheIndicatorLineAndRaisesChanged()
	{
		using var host = new CodeActionTestHost();

		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<CodeAction>>([CodeActionTestHost.CreateItem("Fix it", isPreferred: true)]);

		using CodeActionController controller = host.CreateController();

		int changedCount = 0;
		controller.Changed += (_, _) => changedCount++;

		host.Source.MoveCaret(4);

		host.RunToCompletion(controller.RequestAsync());

		Assert.IsTrue(controller.HasActions);
		Assert.AreEqual(1, changedCount);
		Assert.AreSequenceEqual([2], [.. controller.GetMarkedLineNumbers()]);
	}

	[TestMethod]
	public void RequestAsync_EmptyResult_AfterPublishedState_ClearsTheIndicatorAndRaisesChangedAgain()
	{
		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };
		using CodeActionController controller = host.CreateController();

		host.Source.MoveCaret(4);
		host.RunToCompletion(controller.RequestAsync());

		int changedCount = 0;
		controller.Changed += (_, _) => changedCount++;

		host.RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([]);
		host.RunToCompletion(controller.RequestAsync());

		Assert.IsFalse(controller.HasActions);
		Assert.IsEmpty(controller.GetMarkedLineNumbers());
		Assert.AreEqual(1, changedCount);
	}

	[TestMethod]
	public void RequestAsync_NoPublishedState_DoesNotRaiseChanged()
	{
		using var host = new CodeActionTestHost();
		using CodeActionController controller = host.CreateController();

		int changedCount = 0;
		controller.Changed += (_, _) => changedCount++;

		// The default provider returns an empty list, so the controller publishes nothing and clears nothing; the indicator was never shown,
		// so no change notification belongs to this refresh.
		host.RunToCompletion(controller.RequestAsync());

		Assert.IsFalse(controller.HasActions);
		Assert.AreEqual(0, changedCount);
	}

	[TestMethod]
	public void RequestAsync_VetoedContext_RequestsNothingAndHidesTheIndicator()
	{
		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };
		using CodeActionController controller = host.CreateController();

		host.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		int changedCount = 0;
		controller.Changed += (_, _) => changedCount++;

		// The host vetoes the next context: the provider is not asked, and the snapshot published for the earlier context goes away.
		host.BuildRequest = static _ => null;
		host.RunToCompletion(controller.RequestAsync());

		Assert.HasCount(2, host.Contexts);
		Assert.HasCount(1, host.Requests);
		Assert.IsFalse(controller.HasActions);
		Assert.AreEqual(1, changedCount);
	}

	[TestMethod]
	public void RequestAsync_EditorWithoutDocument_RequestsNothingAndClearsThePublishedState()
	{
		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };
		using CodeActionController controller = host.CreateController();

		host.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		// The source reports no context for an editor without a document, which the controller treats like a vetoed context. Only the capture
		// changes here, so the document replacement does not clear the state first.
		host.Source.CaptureOverride = static () => null;
		host.RunToCompletion(controller.RequestAsync());

		Assert.IsFalse(controller.HasActions);
		Assert.HasCount(1, host.Contexts);
	}

	[TestMethod]
	public void RequestAsync_HostRemappedRange_IsPassedToTheRequest()
	{
		using var host = new CodeActionTestHost
		{
			BuildRequest = static context => new CodeActionRequest(
				CodeActionTestHost.DocumentId,
				context.GetText(),
				new TextRange(0, context.GetText().Length)
			),
		};

		using CodeActionController controller = host.CreateController();

		host.RunToCompletion(controller.RequestAsync());

		Assert.AreEqual(new CodeActionRequest(CodeActionTestHost.DocumentId, "one\ntwo\nthree", new TextRange(0, 13)), host.Requests[0]);
	}

	[TestMethod]
	public void RequestAsync_ThrowingRequestBuilder_ClearsThePublishedStateAndLogsTheFailure()
	{
		var logger = new RecordingLogger();

		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };
		using CodeActionController controller = host.CreateController(logger: logger);

		host.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		host.BuildRequest = static _ => throw new InvalidOperationException("Request building failed.");
		host.RunToCompletion(controller.RequestAsync());

		Assert.IsFalse(controller.HasActions);
		Assert.HasCount(1, logger.Entries);
		Assert.AreEqual(1031, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void RequestAsync_ThrowingRequest_IsContainedAndLoggedOnce()
	{
		var logger = new RecordingLogger();

		using var host = new CodeActionTestHost
		{
			RequestCodeActionsAsync = static (_, _) => throw new InvalidOperationException("Provider failed."),
		};

		using CodeActionController controller = host.CreateController(logger: logger);

		host.RunToCompletion(controller.RequestAsync());

		Assert.IsFalse(controller.HasActions);
		Assert.HasCount(1, logger.Entries);
		Assert.AreEqual(1030, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void RequestAsync_ThrowingCapture_ClearsThePublishedStateAndLogsTheFailure()
	{
		var logger = new RecordingLogger();

		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };
		using CodeActionController controller = host.CreateController(logger: logger);

		host.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		// An editor in an invalid state makes the context factory reject the caret; the request is not admitted, so only the controller's own
		// guard can clear the snapshot that is still published.
		host.Source.CaptureOverride = static () => throw new ArgumentOutOfRangeException("caretOffset");
		host.RunToCompletion(controller.RequestAsync());

		Assert.IsFalse(controller.HasActions);
		Assert.HasCount(1, logger.Entries);
		Assert.AreEqual(1030, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void Actions_AfterPublish_ReturnsThePublishedSnapshot()
	{
		using var host = new CodeActionTestHost();
		CodeAction item = CodeActionTestHost.CreateItem("Fix it");
		host.RequestCodeActionsAsync = (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([item]);

		using CodeActionController controller = host.CreateController();

		// No actions have been published yet, so the accessor reports an empty snapshot.
		Assert.IsEmpty(controller.Actions);

		host.RunToCompletion(controller.RequestAsync());

		Assert.HasCount(1, controller.Actions);
		Assert.AreSame(item, controller.Actions[0]);
	}

	[TestMethod]
	public void Actions_EachPublish_CreatesANewSnapshotEvenForTheSameContent()
	{
		using var host = new CodeActionTestHost();
		CodeAction item = CodeActionTestHost.CreateItem("Fix it");
		host.RequestCodeActionsAsync = (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([item]);

		using CodeActionController controller = host.CreateController();

		host.RunToCompletion(controller.RequestAsync());
		IReadOnlyList<CodeAction> first = controller.Actions;

		host.RunToCompletion(controller.RequestAsync());

		// A presenter tells a replaced set from the one it shows by reference, so identical content still counts as a new snapshot.
		Assert.AreNotSame(first, controller.Actions);
		Assert.AreSame(item, controller.Actions[0]);
	}

	[TestMethod]
	public void GetMarkedLineNumbers_ReturnsAReadOnlyViewOfTheControllersState()
	{
		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };
		using CodeActionController controller = host.CreateController();

		IReadOnlyList<int> lines = controller.GetMarkedLineNumbers();

		Assert.IsEmpty(lines);
		Assert.IsFalse(lines is IList<int> { IsReadOnly: false });

		host.Source.MoveCaret(4);
		host.RunToCompletion(controller.RequestAsync());

		// The same cached view reflects the publication, as a margin that reads it on every render pass expects.
		Assert.AreSequenceEqual([2], [.. lines]);
		Assert.AreSame(lines, controller.GetMarkedLineNumbers());
	}

	[TestMethod]
	public void RequestAsync_NullResult_ClearsThePublishedStateWithoutLogging()
	{
		var logger = new RecordingLogger();

		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };
		using CodeActionController controller = host.CreateController(logger: logger);

		host.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		host.RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>(null!);
		host.RunToCompletion(controller.RequestAsync());

		// A misbehaving provider result is treated as "no actions", not as a failure, and it does not corrupt the state.
		Assert.IsFalse(controller.HasActions);
		Assert.IsEmpty(controller.Actions);
		Assert.IsEmpty(logger.Entries);
	}

	[TestMethod]
	public void RequestAsync_NullEntriesInResult_AreSkippedIndividually()
	{
		using var host = new CodeActionTestHost();
		CodeAction item = CodeActionTestHost.CreateItem("Fix it");
		host.RequestCodeActionsAsync = (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([null!, item]);

		using CodeActionController controller = host.CreateController();

		host.RunToCompletion(controller.RequestAsync());

		// Null entries are dropped one by one; the usable actions are still published.
		Assert.IsTrue(controller.HasActions);
		Assert.HasCount(1, controller.Actions);
		Assert.AreSame(item, controller.Actions[0]);
	}

	[TestMethod]
	[DataRow(true, DisplayName = "WithTheSynchronizationContext")]
	[DataRow(false, DisplayName = "WithoutTheSynchronizationContext")]
	public void RequestAsync_PoolThreadProviderContinuation_PublishesOnTheDispatcherThread(bool installSynchronizationContext)
	{
		var providerGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		using var host = new CodeActionTestHost(installSynchronizationContext)
		{
			RequestCodeActionsAsync = async (_, _) =>
			{
				providerStarted.SetResult();

				await providerGate.Task.ConfigureAwait(false);
				return [CodeActionTestHost.CreateItem("Fix it")];
			},
		};

		using CodeActionController controller = host.CreateController();

		int publishThreadId = 0;
		controller.Changed += (_, _) => publishThreadId = Environment.CurrentManagedThreadId;

		// The provider continuation resumes on a thread-pool thread, so the publish step must still run on the dispatcher thread.
		Task refresh = controller.RequestAsync();

		// Wait for the provider to reach the gate instead of a fixed window, so the release cannot race the provider's start on a loaded machine.
		host.Dispatcher.PumpUntil(() => providerStarted.Task.IsCompleted);
		providerGate.SetResult();
		host.RunToCompletion(refresh);

		Assert.IsTrue(controller.HasActions);
		Assert.AreEqual(host.Dispatcher.OwnerThreadId, publishThreadId, "The publish must run on the dispatcher thread.");
	}

	[TestMethod]
	public void Constructor_NullSourceProviderOrDispatcher_ThrowsArgumentNullException()
	{
		using var host = new CodeActionTestHost();

		ICodeActionSelectionSource selectionSource = host.Source;
		ICodeActionProvider provider = host;
		CodeActionControllerHooks hooks = host.CreateHooks();
		IUiDispatcher dispatcher = host.Dispatcher;

		Assert.ThrowsExactly<ArgumentNullException>(() => new CodeActionController(selectionSource: null!, provider, hooks, dispatcher));
		Assert.ThrowsExactly<ArgumentNullException>(() => new CodeActionController(selectionSource, provider: null!, hooks, dispatcher));
		Assert.ThrowsExactly<ArgumentNullException>(() => new CodeActionController(selectionSource, provider, hooks, dispatcher: null!));
	}

	[TestMethod]
	public void Constructor_EachNullRequiredHook_ThrowsArgumentNullException()
	{
		using var host = new CodeActionTestHost();

		ICodeActionSelectionSource selectionSource = host.Source;
		ICodeActionProvider provider = host;
		IUiDispatcher dispatcher = host.Dispatcher;

		// The hooks are a value type, so a host that smuggles in a null delegate (or a default value) still gets a guarded failure naming the
		// offending hook.
		ArgumentNullException Reject(CodeActionControllerHooks hooks) =>
			Assert.ThrowsExactly<ArgumentNullException>(() => new CodeActionController(selectionSource, provider, hooks, dispatcher));

		string? requestParameter = Reject(
			new CodeActionControllerHooks(BuildRequest: null!, ExecuteActionAsync: static (_, _) => Task.CompletedTask)
		).ParamName;

		Assert.IsNotNull(requestParameter);
		Assert.Contains("BuildRequest", requestParameter);

		string? executeParameter = Reject(new CodeActionControllerHooks(BuildRequest: static _ => null, ExecuteActionAsync: null!)).ParamName;

		Assert.IsNotNull(executeParameter);
		Assert.Contains("ExecuteActionAsync", executeParameter);

		Assert.IsNotNull(Reject(default).ParamName);
	}

	[TestMethod]
	public void Constructor_RejectedArguments_LeaveTheSourceToTheCaller()
	{
		using var host = new CodeActionTestHost();

		ICodeActionSelectionSource selectionSource = host.Source;
		CodeActionControllerHooks hooks = host.CreateHooks();
		IUiDispatcher dispatcher = host.Dispatcher;

		Assert.ThrowsExactly<ArgumentNullException>(() => new CodeActionController(selectionSource, provider: null!, hooks, dispatcher));

		// The controller owns the source only once its constructor returns, and it subscribes at the very end, so a rejected call leaves both the
		// disposal and the (absent) subscriptions to the caller.
		Assert.AreEqual(0, host.Source.DisposeCount);
		Assert.AreEqual(0, host.Source.ChangedSubscriberCount);
		Assert.AreEqual(0, host.Source.DocumentReplacedSubscriberCount);
	}

	[TestMethod]
	public void Constructor_OffTheDispatcherThread_ThrowsInvalidOperationException()
	{
		using var host = new CodeActionTestHost();

		Exception? failure = CaptureOffThreadFailure(() => _ = host.CreateController());

		Assert.IsInstanceOfType<InvalidOperationException>(failure);
		Assert.AreEqual(0, host.Source.DisposeCount);
		Assert.AreEqual(0, host.Source.ChangedSubscriberCount);
	}

	[TestMethod]
	public void Constructor_AttachesToTheSourceEvents()
	{
		using var host = new CodeActionTestHost();

		using CodeActionController controller = host.CreateController();

		Assert.AreEqual(1, host.Source.ChangedSubscriberCount);
		Assert.AreEqual(1, host.Source.DocumentReplacedSubscriberCount);
	}

	// A dedicated thread runs genuinely off the controller's dispatcher thread, and it carries no synchronization context.
	private static Exception? CaptureOffThreadFailure(Action action)
	{
		Exception? failure = null;

		var thread = new Thread(() =>
		{
			try
			{
				action();
			}
			catch (Exception exception)
			{
				failure = exception;
			}
		});

		thread.Start();
		thread.Join();

		return failure;
	}
}
