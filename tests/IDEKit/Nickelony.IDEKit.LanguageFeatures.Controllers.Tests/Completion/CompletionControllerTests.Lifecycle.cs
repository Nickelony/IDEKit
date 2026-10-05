using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

public sealed partial class CompletionControllerTests
{
	[TestMethod]
	public void HandleDocumentReplaced_DismissesThePresenterOnceAndStopsRequesting()
	{
		using var host = new CompletionControllerTestHost(text: "other");
		using CompletionController controller = host.CreateController();

		controller.ScheduleRequest();

		Assert.IsTrue(controller.CurrentPresentation.IsRequestScheduled);

		controller.HandleDocumentReplaced();
		controller.HandleDocumentReplaced();

		Assert.AreSequenceEqual([host.Dispatcher.OwnerThreadId], host.Presenter.DismissThreadIds);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);

		// The controller serves the document it was created for, so it requests nothing for the new one either.
		controller.ScheduleRequest();

		Task<bool> request = controller.RequestAsync();

		host.Dispatcher.Advance(CompletionControllerTestHost.s_debounceDelay * 5);

		Assert.IsTrue(request.IsCompleted);
		Assert.IsFalse(request.Result);
		Assert.IsEmpty(host.Provider.Requests);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
	}

	[TestMethod]
	public void HandleDocumentReplaced_WithARequestInFlight_CancelsItAndPresentsNothing()
	{
		using var host = new CompletionControllerTestHost(text: "sample");
		using CompletionController controller = host.CreateController();

		var answer = new CompletionControllerTestHost.GatedAnswer("sample");

		host.Provider.Answer = answer.AnswerAsync;

		Task<bool> request = controller.RequestAsync();

		host.Dispatcher.PumpUntil(() => answer.Started.IsCompleted);
		controller.HandleDocumentReplaced();

		Assert.IsTrue(answer.Token.IsCancellationRequested);

		// A provider that ignores the token answers for the earlier document anyway; its items must not show over the new one.
		answer.Release();

		Assert.IsFalse(host.RunToCompletion(request));
		Assert.IsEmpty(host.Presenter.Applied);
		Assert.IsFalse(controller.CurrentPresentation.IsListVisible);
	}

	[TestMethod]
	public void HandleDocumentReplaced_AfterDisposal_DismissesNothing()
	{
		using var host = new CompletionControllerTestHost();
		CompletionController controller = host.CreateController();

		controller.Dispose();
		controller.HandleDocumentReplaced();

		Assert.IsEmpty(host.Presenter.DismissThreadIds);
	}

	[TestMethod]
	public void Dispose_WhileARequestIsInFlight_CancelsTheTokenAndDiscardsTheDecision()
	{
		using var host = new CompletionControllerTestHost(text: "sample");
		CompletionController controller = host.CreateController();

		var answer = new CompletionControllerTestHost.GatedAnswer("sample");

		host.Provider.Answer = answer.AnswerAsync;

		Task<bool> request = controller.RequestAsync();

		host.Dispatcher.PumpUntil(() => answer.Started.IsCompleted);
		controller.Dispose();

		Assert.IsTrue(answer.Token.IsCancellationRequested);
		Assert.IsTrue(controller.IsDisposed);

		// A provider that ignores the token and returns a decision anyway must not reach the presenter of a disposed controller.
		answer.Release();

		Assert.IsFalse(host.RunToCompletion(request));
		Assert.IsEmpty(host.Presenter.Applied);
	}

	[TestMethod]
	public async Task Members_AfterDisposal_ReturnSafeDefaultsAndDoNotThrow()
	{
		using var host = new CompletionControllerTestHost();
		CompletionController controller = host.CreateController();

		controller.Dispose();
		controller.Dispose();

		controller.ScheduleRequest();
		controller.CancelScheduledRequest();
		controller.InvalidateRequests();
		controller.CancelInFlightRequest();
		controller.HandleCharacterTyped('a', 1);
		controller.HandleDocumentReplaced();

		// A request that starts after disposal is never admitted and reports its documented not-applied result.
		Assert.IsFalse(await controller.RequestAsync());
		Assert.IsEmpty(host.Provider.Requests);
		Assert.IsEmpty(host.Presenter.Applied);
	}

	[TestMethod]
	public void Dispose_WithAScheduledRequest_ClearsTheScheduledFlag()
	{
		using var host = new CompletionControllerTestHost();
		CompletionController controller = host.CreateController();

		controller.ScheduleRequest();

		Assert.IsTrue(controller.CurrentPresentation.IsRequestScheduled);

		controller.Dispose();

		// The scheduled request never runs, so the published state must not claim one.
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);

		host.Dispatcher.Advance(CompletionControllerTestHost.s_debounceDelay * 5);

		Assert.IsEmpty(host.Provider.Requests);
	}

	[TestMethod]
	public void Dispose_LeavesThePresenterAlone()
	{
		using var host = new CompletionControllerTestHost();
		CompletionController controller = host.CreateController();

		host.Presenter.IsListVisible = true;
		controller.Dispose();

		// The caller owns the window and dismisses it before the controller goes, so the controller reports what the presenter still shows.
		Assert.IsEmpty(host.Presenter.DismissThreadIds);
		Assert.IsTrue(controller.CurrentPresentation.IsListVisible);
	}
}
