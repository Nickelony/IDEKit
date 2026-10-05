using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Verifies the request pipeline of the completion request controller: what a request carries, how the newest request wins, the contained failures,
/// and the creation rules. The scheduling, late-answer, trigger and lifecycle concerns live in the sibling partials.
/// </summary>
/// <remarks>
/// The tests share <see cref="CompletionControllerTestHost"/>, which owns a manual dispatcher, a scripted editor and a presenter
/// that records the decisions it was given. A test that awaits a pending provider pumps the dispatcher instead, because the host installs
/// its synchronization context.
/// </remarks>
[TestClass]
public sealed partial class CompletionControllerTests
{
	[TestMethod]
	public void RequestAsync_ProviderAnswer_ReachesThePresenterOnTheDispatcherThread()
	{
		using var host = new CompletionControllerTestHost(text: "sample");

		host.Provider = ScriptedCompletionProvider.Answering("sample");

		using CompletionController controller = host.CreateController();

		Task<bool> request = controller.RequestAsync();

		Assert.IsTrue(host.RunToCompletion(request));
		Assert.HasCount(1, host.Presenter.Applied);
		Assert.AreEqual("sample", host.Presenter.Applied[0].Items![0].Label);
		Assert.AreSequenceEqual([host.Dispatcher.OwnerThreadId], host.Presenter.ApplyThreadIds);
		Assert.IsTrue(controller.CurrentPresentation.IsListVisible);
	}

	[TestMethod]
	public void RequestAsync_CarriesTheDocumentTheCaretAndTheContextOfTheCall()
	{
		using var host = new CompletionControllerTestHost(text: "pri", caretOffset: 2);
		using CompletionController controller = host.CreateController();

		Assert.IsFalse(host.RunToCompletion(controller.RequestAsync()));
		Assert.IsFalse(host.RunToCompletion(controller.RequestAsync(CompletionContext.ForCharacter('.'))));

		Assert.HasCount(2, host.Provider.Requests);
		Assert.AreEqual(CompletionControllerTestHost.DocumentId, host.Provider.Requests[0].DocumentId);
		Assert.AreEqual("pri", host.Provider.Requests[0].DocumentText);
		Assert.AreEqual(2, host.Provider.Requests[0].CaretOffset);
		Assert.AreEqual(CompletionContext.Invoked, host.Provider.Requests[0].Context);
		Assert.AreEqual(CompletionContext.ForCharacter('.'), host.Provider.Requests[1].Context);
	}

	[TestMethod]
	public void RequestAsync_ThrowingProvider_IsContainedAndLogged()
	{
		using var host = new CompletionControllerTestHost();

		host.Provider.Answer = (_, _) => throw new InvalidOperationException("Completion provider failed.");

		using CompletionController controller = host.CreateController();

		Task<bool> request = controller.RequestAsync();

		// A provider failure must not fault the caller: it is contained and logged like a failure on the
		// scheduled path, and the request reports its not-applied result.
		Assert.IsFalse(host.RunToCompletion(request));
		Assert.HasCount(1, host.Logger.Entries);
		Assert.AreEqual(1000, host.Logger.Entries[0].EventId.Id);
		Assert.IsEmpty(host.Presenter.Applied);

		// The controller stays usable after the contained failure.
		Assert.IsFalse(controller.CurrentPresentation.IsListVisible);
	}

	[TestMethod]
	public void RequestAsync_ThrowingPresenter_IsContainedAndLoggedOnce()
	{
		using var host = new CompletionControllerTestHost(text: "sample");

		host.Provider = ScriptedCompletionProvider.Answering("sample");
		host.Presenter.OnApply = static _ => throw new InvalidOperationException("The window failed.");

		using CompletionController controller = host.CreateController();

		Assert.IsFalse(host.RunToCompletion(controller.RequestAsync()));
		Assert.HasCount(1, host.Logger.Entries);
		Assert.AreEqual(1000, host.Logger.Entries[0].EventId.Id);
	}

	[TestMethod]
	public void RequestAsync_SupersededRequest_DoesNotApplyTheStaleDecision()
	{
		using var host = new CompletionControllerTestHost(text: "sample");
		using CompletionController controller = host.CreateController();

		var first = new CompletionControllerTestHost.GatedAnswer("stale");

		host.Provider.Answer = first.AnswerAsync;

		Task<bool> firstRequest = controller.RequestAsync();

		host.Dispatcher.PumpUntil(() => first.Started.IsCompleted);

		// The newest request wins: the first answer arrives later with a stale decision that must not be applied.
		host.Provider.Answer = (_, _) => Task.FromResult<IReadOnlyList<CompletionItem>>([new CompletionItem("sample")]);

		Task<bool> secondRequest = controller.RequestAsync();

		Assert.IsTrue(host.RunToCompletion(secondRequest));
		Assert.IsTrue(first.Token.IsCancellationRequested);

		first.Release();
		host.Dispatcher.PumpUntil(() => firstRequest.IsCompleted);

		Assert.IsFalse(firstRequest.GetAwaiter().GetResult());
		Assert.HasCount(1, host.Presenter.Applied);
		Assert.AreEqual("sample", host.Presenter.Applied[0].Items![0].Label);
	}

	[TestMethod]
	public void RequestAsync_ProviderCompletingAfterCancelInFlightRequest_DoesNotApplyTheDecision()
	{
		using var host = new CompletionControllerTestHost(text: "sample");
		using CompletionController controller = host.CreateController();

		// The provider observes the cancellation but answers anyway, like a provider that cannot stop early; the canceled result
		// must not reach the presenter.
		host.Provider.Answer = (_, _) =>
		{
			controller.CancelInFlightRequest();
			return Task.FromResult<IReadOnlyList<CompletionItem>>([new CompletionItem("sample")]);
		};

		Assert.IsFalse(host.RunToCompletion(controller.RequestAsync()));
		Assert.IsEmpty(host.Presenter.Applied);
		Assert.IsFalse(controller.CurrentPresentation.IsListVisible);
	}

	[TestMethod]
	public void InvalidateRequests_DiscardsTheDecisionWithoutCancelingTheToken()
	{
		using var host = new CompletionControllerTestHost(text: "sample");
		using CompletionController controller = host.CreateController();

		var answer = new CompletionControllerTestHost.GatedAnswer("sample");

		host.Provider.Answer = answer.AnswerAsync;

		Task<bool> request = controller.RequestAsync();

		host.Dispatcher.PumpUntil(() => answer.Started.IsCompleted);
		controller.InvalidateRequests();

		// Invalidating only rejects the result; the provider keeps running until it is canceled explicitly.
		Assert.IsFalse(answer.Token.IsCancellationRequested);

		answer.Release();

		Assert.IsFalse(host.RunToCompletion(request));
		Assert.IsEmpty(host.Presenter.Applied);
	}

	[TestMethod]
	public void RequestAsync_ContextVersionChangedInFlight_DiscardsTheDecision()
	{
		int contextVersion = 1;

		using var host = new CompletionControllerTestHost(text: "sample") { ContextVersionProvider = () => contextVersion };
		using CompletionController controller = host.CreateController();

		var answer = new CompletionControllerTestHost.GatedAnswer("sample");

		host.Provider.Answer = answer.AnswerAsync;

		Task<bool> request = controller.RequestAsync();

		host.Dispatcher.PumpUntil(() => answer.Started.IsCompleted);

		// The host state moved on while the provider was working, so the decision belongs to the earlier state.
		contextVersion++;
		answer.Release();

		Assert.IsFalse(host.RunToCompletion(request));
		Assert.IsEmpty(host.Presenter.Applied);
		Assert.IsFalse(answer.Token.IsCancellationRequested, "The version check discards the decision without canceling the provider.");
	}

	[TestMethod]
	public void RequestAsync_ProviderCompletingOnAPoolThreadWithoutSynchronizationContext_AppliesTheDecisionOnTheDispatcherThread()
	{
		using var host = new CompletionControllerTestHost(text: "sample", installSynchronizationContext: false);
		using CompletionController controller = host.CreateController();

		// The dispatcher is no synchronization context of the test thread, so this provider's continuation resumes on a thread-pool thread;
		// the completed decision must still be admitted and applied on the dispatcher's thread.
		var providerGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var providerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		host.Provider.Answer = async (_, _) =>
		{
			providerStarted.SetResult();

			await providerGate.Task.ConfigureAwait(false);

			return [new CompletionItem("sample")];
		};

		Task<bool> request = controller.RequestAsync();

		// Wait for the provider to reach the gate instead of a fixed window, so the release cannot race the provider's start on a loaded machine.
		host.Dispatcher.PumpUntil(() => providerStarted.Task.IsCompleted);
		providerGate.SetResult();

		Assert.IsTrue(host.RunToCompletion(request));
		Assert.AreSequenceEqual([host.Dispatcher.OwnerThreadId], host.Presenter.ApplyThreadIds);
		Assert.IsTrue(controller.CurrentPresentation.IsListVisible);
	}

	[TestMethod]
	public void RequestAsync_EditorWithoutADocument_AsksNobodyAndReportsFalse()
	{
		using var host = new CompletionControllerTestHost { HasDocument = false };
		using CompletionController controller = host.CreateController();

		Assert.IsFalse(host.RunToCompletion(controller.RequestAsync()));
		Assert.IsEmpty(host.Provider.Requests);
		Assert.IsEmpty(host.Presenter.Applied);
	}

	[TestMethod]
	public void RequestAsync_OffTheDispatcherThread_Throws()
	{
		using var host = new CompletionControllerTestHost();
		using CompletionController controller = host.CreateController();

		Assert.IsInstanceOfType<InvalidOperationException>(CaptureOffThreadFailure(() => _ = controller.RequestAsync()));
		Assert.IsEmpty(host.Provider.Requests);
	}

	[TestMethod]
	public void HandleCharacterTyped_OffTheDispatcherThread_ThrowsWithoutAskingTheScopeHook()
	{
		int scopeReads = 0;

		using var host = new CompletionControllerTestHost
		{
			ScopeAt = _ =>
			{
				scopeReads++;
				return CompletionScopes.Code;
			},
		};

		using CompletionController controller = host.CreateController();

		Assert.IsInstanceOfType<InvalidOperationException>(CaptureOffThreadFailure(() => controller.HandleCharacterTyped('a', 1)));

		// The thread check is not part of the contained trigger evaluation, so the failure reaches the caller and is not logged.
		Assert.AreEqual(0, scopeReads);
		Assert.IsEmpty(host.Logger.Entries);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
	}

	[TestMethod]
	public void HandleDocumentReplaced_OffTheDispatcherThread_ThrowsAndDismissesNothing()
	{
		using var host = new CompletionControllerTestHost();
		using CompletionController controller = host.CreateController();

		Assert.IsInstanceOfType<InvalidOperationException>(CaptureOffThreadFailure(controller.HandleDocumentReplaced));
		Assert.IsEmpty(host.Presenter.DismissThreadIds);
	}

	[TestMethod]
	public void CurrentPresentation_ReportsThePresenterStateAndTheScheduledFlag()
	{
		using var host = new CompletionControllerTestHost();
		using CompletionController controller = host.CreateController();

		Assert.AreEqual(CompletionPresentationState.Empty, controller.CurrentPresentation);

		host.Presenter.IsListVisible = true;
		host.Presenter.IsTooltipVisible = true;
		host.Presenter.TooltipContent = new CompletionTooltipContent("detail");
		controller.ScheduleRequest();

		Assert.AreEqual(
			new CompletionPresentationState(
				IsListVisible: true,
				IsRequestScheduled: true,
				IsTooltipVisible: true,
				TooltipContent: new CompletionTooltipContent("detail")
			),

			controller.CurrentPresentation
		);
	}

	[TestMethod]
	public void Constructor_InvalidArguments_Throw()
	{
		using var host = new CompletionControllerTestHost();

		static CompletionEditorContext? capture() => null;

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new CompletionController(
				captureContext: null!,
				documentId: "id",
				provider: host.Provider,
				presenter: host.Presenter,
				dispatcher: host.Dispatcher
			)
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new CompletionController(
				captureContext: capture,
				documentId: null!,
				provider: host.Provider,
				presenter: host.Presenter,
				dispatcher: host.Dispatcher
			)
		);

		Assert.ThrowsExactly<ArgumentException>(() =>
			new CompletionController(
				captureContext: capture,
				documentId: " ",
				provider: host.Provider,
				presenter: host.Presenter,
				dispatcher: host.Dispatcher
			)
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new CompletionController(
				captureContext: capture,
				documentId: "id",
				provider: null!,
				presenter: host.Presenter,
				dispatcher: host.Dispatcher
			)
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new CompletionController(
				captureContext: capture,
				documentId: "id",
				provider: host.Provider,
				presenter: null!,
				dispatcher: host.Dispatcher
			)
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new CompletionController(captureContext: capture, documentId: "id", provider: host.Provider, presenter: host.Presenter, dispatcher: null!)
		);
	}

	[TestMethod]
	public void Constructor_OffTheDispatcherThread_ThrowsInvalidOperationException()
	{
		using var host = new CompletionControllerTestHost();
		Assert.IsInstanceOfType<InvalidOperationException>(CaptureOffThreadFailure(() => host.CreateController().Dispose()));
	}

	// A dedicated thread runs genuinely off the dispatcher's thread.
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
