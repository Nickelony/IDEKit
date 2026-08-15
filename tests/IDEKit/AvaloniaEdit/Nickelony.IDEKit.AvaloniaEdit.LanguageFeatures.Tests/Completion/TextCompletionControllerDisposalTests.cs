using Avalonia.Threading;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Requests;
using Nickelony.IDEKit.IntelliSense.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHost;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

[AvaloniaTestClass]
public sealed class TextCompletionControllerDisposalTests
{
	[TestMethod]
	public void Dispose_CancelsInFlightRequestToken()
	{
		using TextCompletionController controller = CreateController(CreateEditor());

		RequestHandle request = controller.Requests.BeginRequest();

		controller.Dispose();

		Assert.IsTrue(request.CancellationToken.IsCancellationRequested);
	}

	[TestMethod]
	public async Task PublicOperations_AfterDisposal_ReturnSafeDefaultsAndDoNotThrow()
	{
		using TextCompletionController controller = CreateController(CreateEditor());

		controller.Dispose();

		controller.ScheduleRequest();
		controller.CancelScheduledRequest();
		controller.CloseWindow();
		controller.CancelTooltipUpdate();
		controller.Requests.InvalidateRequests();
		controller.Requests.CancelInFlightRequest();
		controller.ScheduleCloseIfEmpty();

		// State queries return safe defaults after disposal, and the calls above complete without throwing.
		Assert.IsNull(controller.WindowCoordinator.ActiveWindow);
		Assert.AreEqual(RequestHandle.None, controller.Requests.BeginRequest());
		Assert.IsFalse(controller.Requests.IsCurrent(RequestHandle.None));
		Assert.IsFalse(controller.OpenOrRefresh([], 0, 0));
		Assert.IsFalse(controller.ApplyDecision(TextCompletionSessionDecision.None));

		// A request that starts after disposal is never admitted and reports its documented not-applied
		// result instead of touching the closed session.
		bool requested = false;
		Assert.IsFalse(await controller.RequestAsync(_ =>
		{
			requested = true;
			return Task.FromResult(TextCompletionSessionDecision.None);
		}));
		Assert.IsFalse(requested);
	}

	[TestMethod]
	public void Dispose_DropsPendingRequestAndTooltipDebounces_AndIsIdempotent()
	{
		int requestCount = 0;
		int resolveCount = 0;
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				ResolveDescriptionAsync = (_, _) =>
				{
					resolveCount++;
					return Task.FromResult<object?>("resolved description");
				},
				ScheduledRequestAsync = () =>
				{
					requestCount++;
					return Task.CompletedTask;
				}
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([new TestCompletionData("sample", "Sample documentation.")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// Arm both debounces: the request timer, and - when the engine exposes a tooltip - the tooltip
				// timer through the selection change.
				hosted.Controller.ScheduleRequest();
				completionWindow.CompletionList.SelectItem("sample");

				Assert.IsTrue(hosted.Controller.CurrentPresentation.IsRequestScheduled);

				hosted.Controller.Dispose();
				hosted.Controller.Dispose();

				// Disposal is idempotent and drops the armed debounces: pumping past both delays must not
				// deliver the scheduled request or the tooltip resolution.
				DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(50.0));

				Assert.AreEqual(0, requestCount);
				Assert.AreEqual(0, resolveCount);
			}
		}
	}

	[TestMethod]
	public void ScheduleRequest_ArmedRepeatedly_RunsTheScheduledRequestOnce()
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
			// Arming again restarts the debounce delay, so the last schedule runs the scheduled request
			// exactly once.
			controller.ScheduleRequest();
			controller.ScheduleRequest();
			controller.ScheduleRequest();

			DispatcherTestUtils.PumpUntil(() => requestCount > 0);
			DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(20.0));

			Assert.AreEqual(1, requestCount);
			Assert.IsFalse(controller.CurrentPresentation.IsRequestScheduled);
		}
		finally
		{
			controller.Dispose();
		}
	}

	[TestMethod]
	public void TrackedWindow_ClosingItself_ResetsWindowAndTooltipState()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([new TestCompletionData("sample", "Sample documentation.")], 0, 5));
				Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// Closing the window without going through the controller models a window that closes itself, for
				// example after a commit, Escape, or focus loss.
				completionWindow.Close();

				Assert.IsNull(hosted.Coordinator.ActiveWindow);
				Assert.IsNull(hosted.Controller.WindowCoordinator.ActiveWindow);
				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsTooltipVisible);
				Assert.IsNull(hosted.Controller.CurrentPresentation.TooltipContent);
			}
		}
	}

	[TestMethod]
	public void CloseWindow_WithTrackedWindow_ReportsTheClosureOnceAndClearsState()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				int closedCount = 0;
				hosted.Coordinator.WindowClosed += (_, _) => closedCount++;

				Assert.IsTrue(hosted.Controller.OpenOrRefresh([new TestCompletionData("sample", "Sample documentation.")], 0, 5));
				Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);

				hosted.Controller.CloseWindow();

				// CloseWindow funnels the cleanup through the coordinator's close, so the window is dropped,
				// its closure is reported once, and the presentation is cleared without a duplicate pass.
				Assert.IsNull(hosted.Coordinator.ActiveWindow);
				Assert.AreEqual(1, closedCount);
				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsTooltipVisible);

				// A second close has nothing left to clean up and must not report the closure again.
				hosted.Controller.CloseWindow();

				Assert.AreEqual(1, closedCount);
			}
		}
	}

	[TestMethod]
	public void Dispose_WhenWindowIsOpen_ClosesTheWindowAndClearsState()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			Assert.IsTrue(hosted.Controller.OpenOrRefresh([new TestCompletionData("sample", "Sample documentation.")], 0, 5));
			Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);

			hosted.Controller.Dispose();

			Assert.IsNull(hosted.Coordinator.ActiveWindow);
			Assert.IsNull(hosted.Controller.WindowCoordinator.ActiveWindow);
			Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
		}
	}

	[TestMethod]
	public void OpenOrRefresh_AfterDisposal_WithItems_ReturnsFalseAndOpensNothing()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			hosted.Controller.Dispose();

			// The non-empty call is the disposal path the empty-list coverage does not reach: it must report its
			// documented not-applied result and must not create a window.
			Assert.IsFalse(hosted.Controller.OpenOrRefresh([new TestCompletionData("item")], 0, 1));
			Assert.IsFalse(hosted.Coordinator.IsWindowOpen);
			Assert.IsNull(hosted.Controller.WindowCoordinator.ActiveWindow);
		}
	}

	[TestMethod]
	public void ScheduleCloseIfEmpty_WithItemsPresent_KeepsTheWindowOpen()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([new TestCompletionData("sample", "Sample documentation.")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// Avalonia divergence: AvaloniaEdit filters the completion list lazily, so the list box is only
				// populated once the posted initial selection applies the query. Wait for that before the empty
				// check runs; otherwise the check would observe an unpopulated list and close the window.
				DispatcherTestUtils.PumpUntil(
					() => completionWindow.CompletionList.ListBox.ItemCount > 0,
					timeout: null,
					priority: DispatcherPriority.ApplicationIdle);

				hosted.Controller.ScheduleCloseIfEmpty();
				DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(50.0));

				// The posted empty-list check ran, but the list still has items, so the window stays open.
				Assert.IsTrue(hosted.Coordinator.IsWindowOpen);
			}
		}
	}
}
