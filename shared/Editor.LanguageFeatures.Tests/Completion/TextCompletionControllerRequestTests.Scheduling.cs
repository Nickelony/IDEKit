#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Requests;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHost;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Requests;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHost;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

public sealed partial class TextCompletionControllerRequestTests
{
	[TestMethod]
	public void ScheduleRequest_PastDebounce_RunsScheduledRequestAndClearsScheduledState()
	{
		int requestCount = 0;
		var controller = CreateController(CreateEditor(), hooks: new TextCompletionControllerHooks
		{
			ScheduledRequestAsync = () =>
			{
				requestCount++;
				return Task.CompletedTask;
			}
		});

		try
		{
			Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);

			controller.ScheduleRequest();

			Assert.IsTrue(controller.CurrentPresentation.IsRequestScheduled);

			DispatcherTestUtils.PumpUntil(() => requestCount > 0);

			// The debounced request ran through the timer path and cleared the scheduled state.
			Assert.AreEqual(1, requestCount);
			Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
		}
		finally
		{
			controller.Dispose();
		}
	}

	[TestMethod]
	public void ScheduledRequest_ThrowingScheduledRequest_LogsFailureAndKeepsControllerUsable()
	{
		var logger = new CapturingLogger();
		var controller = CreateController(CreateEditor(), hooks: new TextCompletionControllerHooks
		{
			ScheduledRequestAsync = () => throw new InvalidOperationException("Completion request failed.")
		}, logger: logger);

		try
		{
			controller.ScheduleRequest();
			DispatcherTestUtils.PumpUntil(() => logger.Messages.Count > 0);

			// A failing scheduled request is logged instead of escaping the timer callback.
			Assert.AreEqual(1, logger.Messages.Count);
			StringAssert.Contains(logger.Messages[0], "Completion request failed");

			// The completion request failure carries the documented event id.
			Assert.AreEqual(1000, logger.Entries[0].EventId.Id);

			controller.ScheduleRequest();

			Assert.IsTrue(controller.CurrentPresentation.IsRequestScheduled);
		}
		finally
		{
			controller.Dispose();
		}
	}

	[TestMethod]
	public void CancelScheduledRequest_AfterScheduleRequest_ClearsScheduledState()
	{
		int requestCount = 0;
		var controller = CreateController(CreateEditor(), hooks: new TextCompletionControllerHooks
		{
			ScheduledRequestAsync = () =>
			{
				requestCount++;
				return Task.CompletedTask;
			}
		});

		try
		{
			controller.ScheduleRequest();
			Assert.IsTrue(controller.CurrentPresentation.IsRequestScheduled);

			controller.CancelScheduledRequest();

			// The scheduled request was canceled before the debounce elapsed: the state is cleared and the
			// callback never runs, even after the delay passes.
			Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);

			DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(50.0));

			Assert.AreEqual(0, requestCount);
		}
		finally
		{
			controller.Dispose();
		}
	}

	[TestMethod]
	public void ScheduledRequest_RequestObservingCanceledToken_TreatsCancellationAsCancellation()
	{
		var logger = new CapturingLogger();
		TextCompletionController? controller = null;
		bool scheduledRequestStarted = false;
		controller = CreateController(CreateEditor(), hooks: new TextCompletionControllerHooks
		{
			ScheduledRequestAsync = async () =>
			{
				scheduledRequestStarted = true;
				RequestHandle request = controller!.Requests.BeginRequest();
				await Task.Delay(Timeout.InfiniteTimeSpan, request.CancellationToken).ConfigureAwait(true);
			}
		}, logger: logger);

		try
		{
			controller.ScheduleRequest();
			DispatcherTestUtils.PumpUntil(() => scheduledRequestStarted);

			// Cancellation is an expected outcome of a superseded request, not a failure.
			controller.Requests.CancelInFlightRequest();
			DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(50.0));

			Assert.AreEqual(0, logger.Messages.Count);

			// The controller stays usable after the canceled scheduled request.
			controller.ScheduleRequest();
			Assert.IsTrue(controller.CurrentPresentation.IsRequestScheduled);
		}
		finally
		{
			controller.Dispose();
		}
	}

	[TestMethod]
	public void ScheduleRequest_WithoutScheduledRequestHook_IsIgnored()
	{
		var controller = CreateController(CreateEditor());

		try
		{
			// The hooks carry no scheduled-request callback, so the call has no effect and no timer is armed.
			controller.ScheduleRequest();

			Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
		}
		finally
		{
			controller.Dispose();
		}
	}

	[TestMethod]
	public void CancelScheduledRequest_WithNothingScheduled_IsSafe()
	{
		var controller = CreateController(CreateEditor());

		try
		{
			// Canceling with nothing armed is a no-op and must not disturb the tracked state.
			controller.CancelScheduledRequest();

			Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
		}
		finally
		{
			controller.Dispose();
		}
	}

	[TestMethod]
	public void BeginRequest_ReportsAValidUncanceledHandle()
	{
		var controller = CreateController(CreateEditor());

		try
		{
			// A fresh controller admits a request whose handle carries a valid, uncanceled token.
			RequestHandle request = controller.Requests.BeginRequest();

			Assert.AreNotEqual(RequestHandle.None, request);
			Assert.IsFalse(request.CancellationToken.IsCancellationRequested);
		}
		finally
		{
			controller.Dispose();
		}
	}

	[TestMethod]
	public void CloseWindow_CancelsScheduledRequest()
	{
		int requestCount = 0;
		var controller = CreateController(CreateEditor(), hooks: new TextCompletionControllerHooks
		{
			ScheduledRequestAsync = () =>
			{
				requestCount++;
				return Task.CompletedTask;
			}
		});

		try
		{
			controller.ScheduleRequest();
			Assert.IsTrue(controller.CurrentPresentation.IsRequestScheduled);

			// A window close cancels pending completion work, including a request that has not fired yet.
			controller.CloseWindow();

			Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);

			DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(50.0));

			Assert.AreEqual(0, requestCount);
		}
		finally
		{
			controller.Dispose();
		}
	}
}
