#if AVALONIAEDIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.IntelliSense.CodeActions;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.IntelliSense.CodeActions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class TextCodeActionControllerTests
{
	[TestMethod]
	public void RefreshAsync_WithSelection_BuildsTheContextAndRequestsTheSelectionRange()
	{
		using var host = new CodeActionTestHost("one\ntwo\nthree");
		using var controller = host.CreateController();

		host.Editor.Select(2, 3);

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		TextCodeActionContext context = host.Contexts[^1];

		Assert.AreEqual("one\ntwo\nthree", context.DocumentText);
		Assert.AreEqual(2, context.SelectionStartOffset);
		Assert.AreEqual(5, context.SelectionEndOffset);
		Assert.AreEqual(new TextCodeActionRequest("one\ntwo\nthree", 2, 5), host.Requests[0]);
	}

	[TestMethod]
	public void RefreshAsync_SelectionWithCaretAtTheStart_ReportsTheNormalizedSelectionRange()
	{
		using var host = new CodeActionTestHost("one\ntwo\nthree");
		using var controller = host.CreateController();

		// Reproduces a backward selection: the selection spans 2..5 while the caret sits at its start.
		host.Editor.TextArea.Selection = Selection.Create(host.Editor.TextArea, 2, 5);
		host.Editor.CaretOffset = 2;

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		TextCodeActionContext context = host.Contexts[^1];

		Assert.AreEqual(2, context.CaretOffset);
		Assert.AreEqual(2, context.SelectionStartOffset);
		Assert.AreEqual(5, context.SelectionEndOffset);
	}

	[TestMethod]
	public void RefreshAsync_EmptySelection_CollapsesTheSelectionOffsetsToTheCaret()
	{
		using var host = new CodeActionTestHost("one\ntwo\nthree");
		using var controller = host.CreateController();

		host.Editor.CaretOffset = 5;

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		TextCodeActionContext context = host.Contexts[^1];

		Assert.AreEqual(5, context.CaretOffset);
		Assert.AreEqual(5, context.SelectionStartOffset);
		Assert.AreEqual(5, context.SelectionEndOffset);
	}

	[TestMethod]
	public void RefreshAsync_WithActions_PublishesTheIndicatorLineAndRaisesChanged()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it", isPreferred: true)]);

		using var controller = host.CreateController();

		int changedCount = 0;
		controller.Changed += (_, _) => changedCount++;

		host.Editor.CaretOffset = 4;

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		Assert.IsTrue(controller.HasActions);
		Assert.AreEqual(1, changedCount);
		CollectionAssert.AreEqual(new[] { 2 }, controller.GetIndicatorLineNumbers().ToArray());
	}

	[TestMethod]
	public void RefreshAsync_EmptyResult_AfterPublishedState_ClearsTheIndicatorAndRaisesChangedAgain()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		using var controller = host.CreateController();

		host.Editor.CaretOffset = 4;
		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		int changedCount = 0;
		controller.Changed += (_, _) => changedCount++;

		host.RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>([]);
		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		Assert.IsFalse(controller.HasActions);
		Assert.AreEqual(0, controller.GetIndicatorLineNumbers().Count);
		Assert.AreEqual(1, changedCount);
	}

	[TestMethod]
	public void RefreshAsync_VetoedContext_RequestsNothingAndHidesTheIndicator()
	{
		using var host = new CodeActionTestHost();
		host.BuildRequest = static _ => null;

		using var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		Assert.AreEqual(1, host.Contexts.Count);
		Assert.AreEqual(0, host.Requests.Count);
		Assert.IsFalse(controller.HasActions);
	}

	[TestMethod]
	public void RefreshAsync_HostRemappedRange_IsPassedToTheRequest()
	{
		using var host = new CodeActionTestHost("one\ntwo\nthree");
		host.BuildRequest = static context => new TextCodeActionRequest(context.DocumentText, 0, context.DocumentText.Length);

		using var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		Assert.AreEqual(new TextCodeActionRequest("one\ntwo\nthree", 0, 13), host.Requests[0]);
	}

	[TestMethod]
	public void RefreshAsync_ThrowingStateBuilder_IsContainedAndLogged()
	{
		using var host = new CodeActionTestHost();
		var logger = new CapturingLogger();

		host.BuildRequest = static _ => throw new InvalidOperationException("State building failed.");

		using var controller = host.CreateController(logger: logger);

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		Assert.IsFalse(controller.HasActions);
		Assert.AreEqual(1031, logger.Entries[^1].EventId.Id);
	}

	[TestMethod]
	public void RefreshAsync_ThrowingRequest_IsContainedAndLogged()
	{
		using var host = new CodeActionTestHost();
		var logger = new CapturingLogger();

		host.RequestCodeActionsAsync = static (_, _) => throw new InvalidOperationException("Provider failed.");

		using var controller = host.CreateController(logger: logger);

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		Assert.IsFalse(controller.HasActions);
		Assert.AreEqual(1030, logger.Entries[^1].EventId.Id);
	}

	[TestMethod]
	public void ContextChange_ToDifferentLine_ClearsTheIndicatorBeforeTheDebounceElapses()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		using var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());
		Assert.IsTrue(controller.HasActions);

		host.Editor.CaretOffset = 4;

		Assert.IsFalse(controller.HasActions);
	}

	[TestMethod]
	public void ContextChange_WithinTheSameLine_KeepsTheIndicatorUntilTheRefreshClearsIt()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		using var controller = host.CreateController();

		host.Editor.CaretOffset = 4;
		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());
		Assert.IsTrue(controller.HasActions);

		host.Editor.CaretOffset = 5;

		Assert.IsTrue(controller.HasActions);

		host.RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>([]);
		DispatcherTestUtils.PumpUntil(() => !controller.HasActions);
	}

	[TestMethod]
	public void ContextChange_Settles_RequestsAgainAndMovesTheIndicator()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		using var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());
		Assert.IsTrue(controller.HasActions);

		host.Editor.CaretOffset = 4;

		DispatcherTestUtils.PumpUntil(() =>
		{
			IReadOnlyList<int> lines = controller.GetIndicatorLineNumbers();
			return controller.HasActions && lines.Count == 1 && lines[0] == 2;
		});

		Assert.AreEqual(2, host.Requests.Count);
	}

	[TestMethod]
	public void DocumentEdit_Settles_RequestsAgainForTheEditedDocument()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		using var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());
		Assert.IsTrue(controller.HasActions);
		Assert.AreEqual(1, host.Requests.Count);

		// A document edit is a context change like a caret move, so the controller re-requests through its
		// debounced path even though neither the caret nor the selection moved.
		host.Editor.Document.Insert(host.Editor.Document.TextLength, " four");

		DispatcherTestUtils.PumpUntil(() => host.Requests.Count >= 2 && controller.HasActions);

		// The re-request evaluated the edited document text.
		StringAssert.Contains(host.Requests[^1].DocumentText, "four");
	}

	[TestMethod]
	public void RefreshAsync_SupersededResult_IsDiscarded()
	{
		using var host = new CodeActionTestHost();
		var completion = new TaskCompletionSource<IReadOnlyList<TextCodeActionItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
		host.RequestCodeActionsAsync = (_, _) => completion.Task;

		using var controller = host.CreateController();

		Task refresh = controller.RefreshAsync();
		DispatcherTestUtils.PumpUntil(() => host.RequestTokens.Count == 1);

		controller.InvalidateRequests();
		completion.SetResult([CodeActionTestHost.CreateItem("Fix it")]);

		CodeActionTestHost.RunToCompletion(refresh);

		Assert.IsFalse(controller.HasActions);
	}

	[TestMethod]
	public void RefreshAsync_SupersededProviderFailure_DoesNotClearTheNewerState()
	{
		using var host = new CodeActionTestHost();
		var firstCompletion = new TaskCompletionSource<IReadOnlyList<TextCodeActionItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
		int requestCount = 0;
		host.RequestCodeActionsAsync = (_, _) => ++requestCount == 1
			? firstCompletion.Task
			: Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("second")]);

		using var controller = host.CreateController();

		Task firstRefresh = controller.RefreshAsync();
		DispatcherTestUtils.PumpUntil(() => host.RequestTokens.Count == 1);

		// The second refresh admits a newer request that publishes its own actions.
		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());
		Assert.IsTrue(controller.HasActions);

		// The superseded provider failure must not clear the newer state.
		firstCompletion.SetException(new InvalidOperationException("Stale code action failure."));
		CodeActionTestHost.RunToCompletion(firstRefresh);

		Assert.IsTrue(controller.HasActions);
		Assert.AreEqual(1, controller.Actions.Count);
	}

	[TestMethod]
	public void RefreshAsync_VetoedRefresh_DiscardsTheOutstandingRequestResult()
	{
		using var host = new CodeActionTestHost();
		var completion = new TaskCompletionSource<IReadOnlyList<TextCodeActionItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
		host.RequestCodeActionsAsync = (_, _) => completion.Task;

		using var controller = host.CreateController();

		Task refresh = controller.RefreshAsync();
		DispatcherTestUtils.PumpUntil(() => host.RequestTokens.Count == 1);

		// The refresh finds a vetoing context before it can admit a replacement request, so it concludes
		// without superseding the outstanding request through the coordinator's own admission.
		host.BuildRequest = static _ => null;
		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		// The outstanding request still completes late with usable actions; the refresh must already have
		// superseded it instead of letting its result publish after the cleared state.
		completion.SetResult([CodeActionTestHost.CreateItem("Fix it")]);
		CodeActionTestHost.RunToCompletion(refresh);

		Assert.IsFalse(controller.HasActions);
		Assert.AreEqual(0, controller.Actions.Count);
	}

	[TestMethod]
	public void RefreshAsync_ThrowingStateBuilder_DiscardsTheOutstandingRequestResult()
	{
		using var host = new CodeActionTestHost();
		var completion = new TaskCompletionSource<IReadOnlyList<TextCodeActionItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
		host.RequestCodeActionsAsync = (_, _) => completion.Task;

		using var controller = host.CreateController();

		Task refresh = controller.RefreshAsync();
		DispatcherTestUtils.PumpUntil(() => host.RequestTokens.Count == 1);

		// The refresh fails while building the state, before it can admit a replacement request.
		host.BuildRequest = static _ => throw new InvalidOperationException("State building failed.");
		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		completion.SetResult([CodeActionTestHost.CreateItem("Fix it")]);
		CodeActionTestHost.RunToCompletion(refresh);

		Assert.IsFalse(controller.HasActions);
		Assert.AreEqual(0, controller.Actions.Count);
	}

	[TestMethod]
	public void ContextChange_CancelsTheInFlightRequestToken()
	{
		using var host = new CodeActionTestHost();
		var completion = new TaskCompletionSource<IReadOnlyList<TextCodeActionItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
		host.RequestCodeActionsAsync = (_, _) => completion.Task;

		using var controller = host.CreateController();

		Task refresh = controller.RefreshAsync();
		DispatcherTestUtils.PumpUntil(() => host.RequestTokens.Count == 1);

		host.Editor.CaretOffset = 4;

		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		completion.SetResult([]);
		CodeActionTestHost.RunToCompletion(refresh);
	}

	[TestMethod]
	public void RefreshAsync_RunsImmediately_WithoutASecondScheduledRequest()
	{
		using var host = new CodeActionTestHost();
		using var controller = host.CreateController();

		host.Editor.CaretOffset = 2;
		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(40.0));

		Assert.AreEqual(1, host.Requests.Count);
	}

	[TestMethod]
	public void Dispose_ClearsTheIndicatorAndStopsRequests()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());
		Assert.IsTrue(controller.HasActions);

		int changedCount = 0;
		controller.Changed += (_, _) => changedCount++;

		controller.Dispose();

		Assert.IsFalse(controller.HasActions);
		Assert.AreEqual(1, changedCount);

		host.Editor.CaretOffset = 4;
		DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(40.0));

		Assert.AreEqual(1, host.Requests.Count);
	}

	[TestMethod]
	public void TryOpenActions_WithoutActions_ReturnsFalse()
	{
		using var host = new CodeActionTestHost();
		using var controller = host.CreateController();

		Assert.IsFalse(controller.TryOpenActions());
		Assert.IsFalse(controller.IsActionsOpen);
	}

	[TestMethod]
	public void TryOpenActions_EditorNotConnectedToPresentationSource_ReturnsFalse()
	{
		var editor = TestHost.CreateEditor("one");
		using var controller = CodeActionTestHost.CreateUnhostedController(
			editor,
			static (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]));

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());
		Assert.IsTrue(controller.HasActions);

		Assert.IsFalse(controller.TryOpenActions());
		Assert.IsFalse(controller.IsActionsOpen);
	}

	[TestMethod]
	public void Actions_AfterPublish_ReturnsThePublishedSnapshot()
	{
		using var host = new CodeActionTestHost();
		TextCodeActionItem item = CodeActionTestHost.CreateItem("Fix it");
		host.RequestCodeActionsAsync = (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([item]);

		using var controller = host.CreateController();

		// No actions have been published yet, so the accessor reports an empty snapshot.
		Assert.AreEqual(0, controller.Actions.Count);

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		Assert.AreEqual(1, controller.Actions.Count);
		Assert.AreSame(item, controller.Actions[0]);
	}

	[TestMethod]
	public void TryOpenActionsAtAnchor_IndicatorLine_OpensTheMenuAtTheAnchor()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		using var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		var anchor = new Point(12.0, 34.0);

		Assert.IsTrue(controller.TryOpenActions(1, anchor));
		Assert.IsTrue(controller.IsActionsOpen);

		ContextMenu menu = host.Menus[^1];

		Assert.AreSame(host.Editor.TextArea, menu.PlacementTarget);
#if AVALONIAEDIT

		// Avalonia replaces the WPF relative-point placement with an explicit anchor and gravity: the
		// requested anchor becomes the top-left with a bottom-right gravity.
		Assert.AreEqual(PlacementMode.AnchorAndGravity, menu.Placement);
#else
		Assert.AreEqual(PlacementMode.RelativePoint, menu.Placement);
#endif
		Assert.AreEqual(12.0, menu.HorizontalOffset);
		Assert.AreEqual(34.0, menu.VerticalOffset);
	}

	[TestMethod]
	public void TryOpenActionsAtAnchor_LineWithoutIndicator_ReturnsFalse()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);

		using var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());
		Assert.IsTrue(controller.HasActions);

		// The caret is on line 1, so the indicator belongs to line 1; a host anchor for line 2 is stale.
		Assert.IsFalse(controller.TryOpenActions(2, new Point(0.0, 0.0)));
		Assert.IsFalse(controller.IsActionsOpen);
	}

	[TestMethod]
	public void CancelScheduledRequest_BeforeTheDebounce_SkipsTheRequest()
	{
		using var host = new CodeActionTestHost();
		using var controller = host.CreateController();

		// A caret move schedules the debounced request; canceling before the delay elapses skips it.
		host.Editor.CaretOffset = 2;
		controller.CancelScheduledRequest();

		DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(40.0));

		Assert.AreEqual(0, host.Requests.Count);
	}

	[TestMethod]
	public void CancelInFlightRequest_InFlightRequest_CancelsTheTokenAndRejectsTheResult()
	{
		using var host = new CodeActionTestHost();
		var completion = new TaskCompletionSource<IReadOnlyList<TextCodeActionItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
		host.RequestCodeActionsAsync = (_, _) => completion.Task;

		using var controller = host.CreateController();

		Task refresh = controller.RefreshAsync();
		DispatcherTestUtils.PumpUntil(() => host.RequestTokens.Count == 1);

		controller.CancelInFlightRequest();

		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		completion.SetResult([CodeActionTestHost.CreateItem("Fix it")]);
		CodeActionTestHost.RunToCompletion(refresh);

		// The canceled request's result is rejected even though the provider ignored the cancellation.
		Assert.IsFalse(controller.HasActions);
	}

	[TestMethod]
	public void InvokeAction_ThrowingExecuteHook_IsContainedAndLogged()
	{
		using var host = new CodeActionTestHost();
		var logger = new CapturingLogger();
		host.RequestCodeActionsAsync = static (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([CodeActionTestHost.CreateItem("Fix it")]);
		host.ExecuteActionAsync = static _ => throw new InvalidOperationException("Execute failed.");

		using var controller = host.CreateController(logger: logger);

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());
		Assert.IsTrue(controller.TryOpenActions());

		// Invoke the action through the real menu item so the execute pipeline runs.
		var menuItem = (MenuItem)host.Menus[^1].Items[0]!;

		menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, menuItem));

		DispatcherTestUtils.PumpUntil(() => logger.Entries.Count > 0);

		Assert.AreEqual(1, host.Executed.Count);
		Assert.AreEqual(1031, logger.Entries[^1].EventId.Id);
	}

	[TestMethod]
	public void DocumentSwap_ReattachesToTheNewDocumentAndIgnoresTheOldOne()
	{
		using var host = new CodeActionTestHost("one\ntwo\nthree");
		using var controller = host.CreateController();

		TextDocument firstDocument = host.Editor.Document;
		host.Editor.Document = new TextDocument("alpha\nbeta");

		DispatcherTestUtils.PumpUntil(() => host.Contexts.Count > 0 && host.Contexts[^1].DocumentText == "alpha\nbeta");

		int contextCount = host.Contexts.Count;

		// The old document is no longer observed: changing it must not schedule a request for the new one.
		firstDocument.Insert(0, "ignored ");

		DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(40.0));

		Assert.AreEqual(contextCount, host.Contexts.Count);
	}

	[TestMethod]
	public void PublicOperations_AfterDisposal_ReturnSafeDefaultsAndDoNotThrow()
	{
		using var host = new CodeActionTestHost();
		var controller = host.CreateController();

		controller.Dispose();

		controller.CancelScheduledRequest();
		controller.CancelInFlightRequest();
		controller.InvalidateRequests();
		controller.CloseActions();
		controller.RefreshAsync().GetAwaiter().GetResult();

		Assert.IsFalse(controller.HasActions);
		Assert.IsFalse(controller.IsActionsOpen);
		Assert.IsFalse(controller.TryOpenActions());
		Assert.IsFalse(controller.TryOpenActions(1, new Point(0.0, 0.0)));
	}

	[TestMethod]
	public void RefreshAsync_PoolThreadProviderContinuation_PublishesOnTheDispatcherThread()
	{
		using var host = new CodeActionTestHost();
		var providerGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		int publishThreadId = 0;

		host.RequestCodeActionsAsync = async (_, _) =>
		{
			await providerGate.Task.ConfigureAwait(false);
			return (IReadOnlyList<TextCodeActionItem>)[CodeActionTestHost.CreateItem("Fix it")];
		};

		using var controller = host.CreateController();

		int dispatcherThreadId = Environment.CurrentManagedThreadId;
		controller.Changed += (_, _) => publishThreadId = Environment.CurrentManagedThreadId;

		// The provider continuation resumes on a thread-pool thread, so the publish step must still run on
		// the dispatcher thread the test body runs on.
		Task refresh = controller.RefreshAsync();

		DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(20.0));
		providerGate.SetResult();
		CodeActionTestHost.RunToCompletion(refresh);

		Assert.IsTrue(controller.HasActions);
		Assert.AreEqual(dispatcherThreadId, publishThreadId, "The publish must run on the dispatcher thread.");
	}

	[TestMethod]
	public void RefreshAsync_NullResult_ClearsTheIndicator()
	{
		using var host = new CodeActionTestHost();
		host.RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<TextCodeActionItem>>(null!);

		using var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		// A misbehaving provider result is treated as "no actions" instead of corrupting the state.
		Assert.IsFalse(controller.HasActions);
		Assert.AreEqual(0, controller.Actions.Count);
	}

	[TestMethod]
	public void RefreshAsync_NullEntriesInResult_AreSkippedIndividually()
	{
		using var host = new CodeActionTestHost();
		TextCodeActionItem item = CodeActionTestHost.CreateItem("Fix it");
		host.RequestCodeActionsAsync = (_, _) =>
			Task.FromResult<IReadOnlyList<TextCodeActionItem>>([null!, item]);

		using var controller = host.CreateController();

		CodeActionTestHost.RunToCompletion(controller.RefreshAsync());

		// Null entries are dropped one by one; the usable actions are still published.
		Assert.IsTrue(controller.HasActions);
		Assert.AreEqual(1, controller.Actions.Count);
		Assert.AreSame(item, controller.Actions[0]);
	}
}
