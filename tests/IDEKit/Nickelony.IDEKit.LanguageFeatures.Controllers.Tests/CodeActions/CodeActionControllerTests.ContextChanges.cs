using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;
using Nickelony.Testing;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Covers how the controller reacts to the context its selection source reports: the debounced refresh, the stale indicator, the document edit,
/// and the document replacement.
/// </summary>
public sealed partial class CodeActionControllerTests
{
	[TestMethod]
	public void ContextChange_ToDifferentLine_ClearsTheIndicatorBeforeTheDebounceElapses()
	{
		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };
		using CodeActionController controller = host.CreateController();

		host.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		host.Source.MoveCaret(4);

		Assert.IsFalse(controller.HasActions);
	}

	[TestMethod]
	public void ContextChange_WithinTheSameLine_KeepsTheIndicatorUntilTheRefreshClearsIt()
	{
		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };
		using CodeActionController controller = host.CreateController();

		host.Source.MoveCaret(4);
		host.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		host.Source.MoveCaret(5);

		Assert.IsTrue(controller.HasActions);

		host.RequestCodeActionsAsync = static (_, _) => Task.FromResult<IReadOnlyList<CodeAction>>([]);
		host.SettleDebounce();

		Assert.IsFalse(controller.HasActions);
	}

	[TestMethod]
	public void ContextChange_Settles_RequestsAgainAndMovesTheIndicator()
	{
		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };
		using CodeActionController controller = host.CreateController();

		host.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		host.Source.MoveCaret(4);
		host.SettleDebounce();

		Assert.IsTrue(controller.HasActions);
		Assert.AreSequenceEqual([2], [.. controller.GetMarkedLineNumbers()]);
		Assert.HasCount(2, host.Requests);
	}

	[TestMethod]
	public void ContextChange_BeforeTheDebounceElapses_RequestsNothingYet()
	{
		using var host = new CodeActionTestHost();
		using CodeActionController controller = host.CreateController();

		host.Source.MoveCaret(2);

		// The delay restarts with every change, so a caret that keeps moving asks the host nothing until it settles.
		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(0.5));

		Assert.IsEmpty(host.Requests);

		host.Source.MoveCaret(3);
		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(0.5));

		Assert.IsEmpty(host.Requests);

		host.SettleDebounce();

		Assert.HasCount(1, host.Requests);
	}

	[TestMethod]
	public void DocumentEdit_Settles_RequestsAgainForTheEditedDocument()
	{
		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };
		using CodeActionController controller = host.CreateController();

		host.RunToCompletion(controller.RequestAsync());

		Assert.IsTrue(controller.HasActions);
		Assert.HasCount(1, host.Requests);

		// A document edit is a context change like a caret move, so the controller re-requests through its debounced path even though neither
		// the caret nor the selection moved.
		host.Source.EditDocument("one\ntwo\nthree four");
		host.SettleDebounce();

		Assert.HasCount(2, host.Requests);
		Assert.IsTrue(controller.HasActions);

		// The re-request evaluated the edited document text.
		Assert.Contains("four", host.Requests[^1].DocumentText);
	}

	[TestMethod]
	public void ContextChange_CancelsTheInFlightRequestToken()
	{
		using var host = new CodeActionTestHost();
		var completion = new TaskCompletionSource<IReadOnlyList<CodeAction>>(TaskCreationOptions.RunContinuationsAsynchronously);

		host.RequestCodeActionsAsync = (_, _) => completion.Task;

		using CodeActionController controller = host.CreateController();

		Task refresh = controller.RequestAsync();
		host.Dispatcher.PumpUntil(() => host.RequestTokens.Count == 1);

		host.Source.MoveCaret(4);

		Assert.IsTrue(host.RequestTokens[0].IsCancellationRequested);

		completion.SetResult([]);
		host.RunToCompletion(refresh);
	}

	[TestMethod]
	public void RequestAsync_RunsImmediately_WithoutASecondScheduledRequest()
	{
		using var host = new CodeActionTestHost();
		using CodeActionController controller = host.CreateController();

		host.Source.MoveCaret(2);
		host.RunToCompletion(controller.RequestAsync());

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(40.0));

		// The immediate request supersedes the debounced one the caret move scheduled.
		Assert.HasCount(1, host.Requests);
	}

	[TestMethod]
	public void CancelScheduledRequest_BeforeTheDebounce_SkipsTheRequest()
	{
		using var host = new CodeActionTestHost();
		using CodeActionController controller = host.CreateController();

		// A caret move schedules the debounced request; canceling before the delay elapses skips it.
		host.Source.MoveCaret(2);
		controller.CancelScheduledRequest();

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(40.0));

		Assert.IsEmpty(host.Requests);
	}

	[TestMethod]
	public void DocumentReplacement_ClearsThePublishedStateAndRequestsForTheNewDocument()
	{
		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };
		using CodeActionController controller = host.CreateController();

		host.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		int changedCount = 0;
		controller.Changed += (_, _) => changedCount++;

		host.Source.ReplaceDocument("alpha\nbeta");

		// The actions belong to the previous document, so they disappear at once, before the refresh for the new document runs.
		Assert.IsFalse(controller.HasActions);
		Assert.AreEqual(1, changedCount);

		host.SettleDebounce();

		Assert.AreEqual("alpha\nbeta", host.Contexts[^1].GetText());
		Assert.IsTrue(controller.HasActions);
	}

	[TestMethod]
	public void DocumentReplacement_ToNoDocument_RequestsNothingMore()
	{
		using var host = new CodeActionTestHost();
		using CodeActionController controller = host.CreateController();

		host.Source.ReplaceDocument(null);
		host.SettleDebounce();

		// The scheduled refresh finds no context, which clears and requests nothing.
		Assert.IsEmpty(host.Requests);
		Assert.IsFalse(controller.HasActions);
	}

	[TestMethod]
	public void ContextChange_RaisedOffTheDispatcherThread_FailsTheRaiseAndSchedulesNothing()
	{
		using var host = new CodeActionTestHost();
		using CodeActionController controller = host.CreateController();

		// The controller answers a notification on its dispatcher's thread only, so a source that raises elsewhere is rejected loudly.
		Exception? failure = CaptureOffThreadFailure(() => host.Source.MoveCaret(2));

		host.SettleDebounce();

		Assert.IsInstanceOfType<InvalidOperationException>(failure);
		Assert.IsEmpty(host.Requests);
	}

	[TestMethod]
	public void DocumentReplacement_RaisedOffTheDispatcherThread_FailsTheRaiseAndKeepsThePublishedState()
	{
		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };
		using CodeActionController controller = host.CreateController();

		host.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		Exception? failure = CaptureOffThreadFailure(() => host.Source.ReplaceDocument("alpha\nbeta"));

		Assert.IsInstanceOfType<InvalidOperationException>(failure);
		Assert.IsTrue(controller.HasActions);
	}

	[TestMethod]
	public void Dispose_ClearsTheIndicatorAndDropsTheScheduledRequest()
	{
		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };

		CodeActionController controller = host.CreateController();

		host.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		// A caret move inside the indicator line keeps the indicator and schedules a refresh.
		host.Source.MoveCaret(1);
		Assert.IsTrue(controller.HasActions);

		int changedCount = 0;
		controller.Changed += (_, _) => changedCount++;

		controller.Dispose();

		Assert.IsFalse(controller.HasActions);
		Assert.AreEqual(1, changedCount);

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(40.0));

		Assert.HasCount(1, host.Requests);
	}

	[TestMethod]
	public void Dispose_SourceDisposalThrows_StillClearsTheStateAndRaisesChanged()
	{
		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };

		CodeActionController controller = host.CreateController();

		host.RunToCompletion(controller.RequestAsync());

		int changedCount = 0;
		controller.Changed += (_, _) => changedCount++;

		host.Source.DisposeOverride = static () => throw new InvalidOperationException("Source disposal failed.");

		// A host source that faults on disposal surfaces its failure, but only after the controller is torn down.
		Assert.ThrowsExactly<InvalidOperationException>(controller.Dispose);

		Assert.IsFalse(controller.HasActions);
		Assert.AreEqual(1, changedCount);
		Assert.AreEqual(1, host.Source.DisposeCount);
	}

	[TestMethod]
	public void Dispose_ChangedHandlerThrows_StillDisposesTheSource()
	{
		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };

		CodeActionController controller = host.CreateController();

		host.RunToCompletion(controller.RequestAsync());

		controller.Changed += static (_, _) => throw new InvalidOperationException("Handler failed.");

		Assert.ThrowsExactly<InvalidOperationException>(controller.Dispose);

		Assert.AreEqual(1, host.Source.DisposeCount);
		Assert.IsFalse(controller.HasActions);
	}

	[TestMethod]
	public void Dispose_DetachesFromAndDisposesTheSelectionSource()
	{
		using var host = new CodeActionTestHost();

		CodeActionController controller = host.CreateController();

		controller.Dispose();
		controller.Dispose();

		// The controller owns the source: it unsubscribes and disposes it once, and a second disposal does nothing more.
		Assert.AreEqual(0, host.Source.ChangedSubscriberCount);
		Assert.AreEqual(0, host.Source.DocumentReplacedSubscriberCount);
		Assert.AreEqual(1, host.Source.DisposeCount);
	}

	[TestMethod]
	public void RequestAsync_ContextChangeDuringTheCapture_ThenTheCaptureFails_KeepsThePublishedState()
	{
		var logger = new RecordingLogger();

		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };
		using CodeActionController controller = host.CreateController(logger: logger);

		host.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		// A caret move inside the indicator line lands while the request reads the editor, and then the read fails. The attempt is stale, so its
		// failure must not clear the snapshot; the refresh that the move scheduled decides.
		host.Source.CaptureOverride = () =>
		{
			host.Source.MoveCaret(1);
			throw new ArgumentOutOfRangeException("caretOffset");
		};

		host.RunToCompletion(controller.RequestAsync());

		Assert.IsTrue(controller.HasActions);
		Assert.HasCount(1, logger.Entries);
		Assert.AreEqual(1030, logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void RequestAsync_ContextChangeDuringTheCapture_ThenNoDocumentRemains_KeepsThePublishedState()
	{
		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };
		using CodeActionController controller = host.CreateController();

		host.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		// The caret move inside the indicator line makes the attempt stale before it learns that the editor has no document.
		host.Source.CaptureOverride = () =>
		{
			host.Source.MoveCaret(1);
			return null;
		};

		host.RunToCompletion(controller.RequestAsync());

		Assert.IsTrue(controller.HasActions);
	}

	[TestMethod]
	public void RequestAsync_ContextChangeDuringTheRequestBuilder_ThenTheBuilderVetoes_KeepsThePublishedState()
	{
		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };
		using CodeActionController controller = host.CreateController();

		host.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		host.BuildRequest = _ =>
		{
			host.Source.MoveCaret(1);
			return null;
		};

		host.RunToCompletion(controller.RequestAsync());

		// The attempt is stale, so its veto leaves the snapshot to the refresh that the caret move scheduled.
		Assert.IsTrue(controller.HasActions);
	}

	[TestMethod]
	public void RequestAsync_ContextChangeDuringTheRequestBuilder_ThenTheBuilderThrows_KeepsThePublishedState()
	{
		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };
		using CodeActionController controller = host.CreateController();

		host.RunToCompletion(controller.RequestAsync());
		Assert.IsTrue(controller.HasActions);

		host.BuildRequest = _ =>
		{
			host.Source.MoveCaret(1);
			throw new InvalidOperationException("Request building failed.");
		};

		host.RunToCompletion(controller.RequestAsync());

		Assert.IsTrue(controller.HasActions);
	}

	[TestMethod]
	public void RequestAsync_ContextChangeDuringTheRequestBuilder_DoesNotAskTheProviderAndRefreshesAfterwards()
	{
		using var host = new CodeActionTestHost { RequestCodeActionsAsync = static (_, _) => CodeActionTestHost.OneItem() };
		using CodeActionController controller = host.CreateController();

		bool moved = false;

		host.BuildRequest = context =>
		{
			if (!moved)
			{
				moved = true;
				host.Source.MoveCaret(2);
			}

			return new CodeActionRequest(CodeActionTestHost.DocumentId, context.GetText(), context.Range);
		};

		host.RunToCompletion(controller.RequestAsync());

		// The caret move made the attempt stale before it could admit a request, so the provider heard nothing from it.
		Assert.IsEmpty(host.Requests);
		Assert.IsFalse(controller.HasActions);

		host.SettleDebounce();

		// The refresh that the caret move scheduled asks for the context the move produced.
		Assert.HasCount(1, host.Requests);
		Assert.IsTrue(controller.HasActions);
	}
}
