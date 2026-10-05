using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

public sealed partial class CompletionControllerTests
{
	[TestMethod]
	public void ScheduleRequest_PastDebounce_RunsScheduledRequestAndClearsScheduledState()
	{
		using var host = new CompletionControllerTestHost();
		using CompletionController controller = host.CreateController();

		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);

		controller.ScheduleRequest();

		Assert.IsTrue(controller.CurrentPresentation.IsRequestScheduled);

		host.Dispatcher.Advance(CompletionControllerTestHost.s_debounceDelay);

		// The debounced request ran through the timer path and cleared the scheduled state.
		Assert.HasCount(1, host.Provider.Requests);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
	}

	[TestMethod]
	public void CancelScheduledRequest_AfterScheduleRequest_ClearsScheduledState()
	{
		using var host = new CompletionControllerTestHost();
		using CompletionController controller = host.CreateController();

		controller.ScheduleRequest();

		Assert.IsTrue(controller.CurrentPresentation.IsRequestScheduled);

		controller.CancelScheduledRequest();

		// The scheduled request was canceled before the debounce elapsed: the state is cleared and the callback never runs, even after the delay passes.
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);

		host.Dispatcher.Advance(CompletionControllerTestHost.s_debounceDelay * 5);

		Assert.IsEmpty(host.Provider.Requests);
	}

	[TestMethod]
	public void ScheduleRequest_WaitsForTheDebounceDelayOfTheControllerOptions()
	{
		using var host = new CompletionControllerTestHost
		{
			Options = CompletionControllerOptions.Default with { RequestDebounceDelay = TimeSpan.FromMinutes(10.0) },
		};

		using CompletionController controller = host.CreateController();

		controller.ScheduleRequest();

		// The configured delay is far beyond the default one, so the request has not run after the default delay passed.
		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(400.0));

		Assert.IsEmpty(host.Provider.Requests);
		Assert.IsTrue(controller.CurrentPresentation.IsRequestScheduled);
	}

	[TestMethod]
	public void ScheduleRequest_ArmedRepeatedly_RunsTheScheduledRequestOnce()
	{
		using var host = new CompletionControllerTestHost();
		using CompletionController controller = host.CreateController();

		// Arming again restarts the debounce delay, so the last schedule runs the scheduled request exactly once.
		controller.ScheduleRequest();
		controller.ScheduleRequest();
		controller.ScheduleRequest();

		host.Dispatcher.Advance(CompletionControllerTestHost.s_debounceDelay * 5);

		Assert.HasCount(1, host.Provider.Requests);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
	}

	[TestMethod]
	public void RequestAsync_AfterScheduleRequest_CancelsTheScheduledRequest()
	{
		using var host = new CompletionControllerTestHost();
		using CompletionController controller = host.CreateController();

		controller.ScheduleRequest();

		Task<bool> request = controller.RequestAsync();

		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);

		host.RunToCompletion(request);
		host.Dispatcher.Advance(CompletionControllerTestHost.s_debounceDelay * 5);

		// The host's request is the newest one, so the request that was scheduled before it never ran after it.
		Assert.HasCount(1, host.Provider.Requests);
	}

	[TestMethod]
	public void ScheduleRequest_AfterDisposal_DoesNothing()
	{
		using var host = new CompletionControllerTestHost();
		CompletionController controller = host.CreateController();

		controller.Dispose();
		controller.ScheduleRequest();
		host.Dispatcher.Advance(CompletionControllerTestHost.s_debounceDelay * 5);

		Assert.IsEmpty(host.Provider.Requests);
		Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
	}
}
