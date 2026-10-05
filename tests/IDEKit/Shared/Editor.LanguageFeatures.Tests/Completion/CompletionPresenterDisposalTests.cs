#if AVALONIAEDIT
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHelpers;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionWindowTestHelpers;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHelpers;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionWindowTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed class CompletionPresenterDisposalTests
{
	[TestMethod]
	public void Dispose_WhileARequestIsInFlight_CancelsTheTokenAndDiscardsTheDecision()
	{
		var provider = new ScriptedCompletionProvider();

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController(provider: provider);

		using (hosted.HostWindow)
		{
			using var request = new GatedCompletionRequest(hosted.Controller, provider);

			hosted.Controller.Dispose();

			Assert.IsTrue(request.Token.IsCancellationRequested);

			// A provider that ignores the token and returns a decision anyway must not open a window on a disposed controller.
			request.Release();
			DispatcherTestHelpers.PumpUntil(() => request.Result.IsCompleted);

			Assert.IsFalse(request.Result.GetAwaiter().GetResult());
			Assert.IsNull(hosted.Presenter.ActiveWindow);
		}
	}

	[TestMethod]
	public async Task PublicOperations_AfterDisposal_ReturnSafeDefaultsAndDoNotThrow()
	{
		var provider = new ScriptedCompletionProvider();

		using CompletionSession session = CreateController(CreateEditor(), provider: provider);
		CompletionController controller = session.Controller;
		CompletionPresenter completion = session.Completion;

		session.Dispose();

		controller.ScheduleRequest();
		controller.CancelScheduledRequest();
		completion.Dismiss();
		CompletionTooltipTestHelpers.CancelPendingUpdate(completion);
		controller.InvalidateRequests();
		controller.CancelInFlightRequest();
		completion.ScheduleDismissWhenEmpty();

		// State queries return safe defaults after disposal, and the calls above complete without throwing.
		Assert.IsNull(completion.WindowPresenter.ActiveWindow);
		Assert.IsFalse(completion.OpenOrRefresh([], TextRange.FromBounds(0, 0)));
		Assert.IsFalse(completion.ApplyDirective(CompletionSessionDirective.None));

		// A request that starts after disposal is never admitted and reports its documented not-applied
		// result instead of touching the closed session.
		Assert.IsFalse(await controller.RequestAsync());
		Assert.IsEmpty(provider.Requests);
	}

	[TestMethod]
	public void Dispose_DropsPendingRequestAndTooltipDebounces_AndIsIdempotent()
	{
		int resolveCount = 0;
		var provider = new ScriptedCompletionProvider();

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(
				hooks: CompletionTooltipTestHelpers.CreateCountingHooks(onResolve: () => resolveCount++),
				provider: provider
			);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(
						hosted.Completion.OpenOrRefresh([new StubCompletionData("sample", "Sample documentation.")], TextRange.FromBounds(0, 5))
					);

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					// Arm both debounces: the request timer, and, when the engine exposes a tooltip, the tooltip
					// timer through the selection change.
					hosted.Controller.ScheduleRequest();
					completionWindow.CompletionList.SelectItem("sample");

					Assert.IsTrue(hosted.Controller.CurrentPresentation.IsRequestScheduled);

					hosted.Controller.Dispose();
					hosted.Controller.Dispose();
					hosted.Completion.Dispose();
					hosted.Completion.Dispose();

					// Disposal is idempotent and drops the armed debounces: the controller drops the request timer and
					// the presenter drops the tooltip timer, so pumping past both delays must not deliver the scheduled
					// request or the tooltip resolution.
					DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(50.0));

					Assert.IsEmpty(provider.Requests);
					Assert.AreEqual(0, resolveCount);
				}
			}
		}
	}

	[TestMethod]
	public void TrackedWindow_ClosingItself_ResetsWindowAndTooltipState()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(
						hosted.Completion.OpenOrRefresh([new StubCompletionData("sample", "Sample documentation.")], TextRange.FromBounds(0, 5))
					);

					Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					// Closing the window without going through the controller models a window that closes itself, for
					// example after a commit, Escape, or focus loss.
					completionWindow.Close();

					Assert.IsNull(hosted.Presenter.ActiveWindow);
					Assert.IsNull(hosted.Completion.WindowPresenter.ActiveWindow);
					Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
					Assert.IsFalse(hosted.Controller.CurrentPresentation.IsTooltipVisible);
					Assert.IsNull(hosted.Controller.CurrentPresentation.TooltipContent);
				}
			}
		}
	}

	[TestMethod]
	public void Dismiss_WithTrackedWindow_ReportsTheClosureOnceAndClearsState()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					int closedCount = 0;
					hosted.Presenter.WindowClosed += (_, _) => closedCount++;

					Assert.IsTrue(
						hosted.Completion.OpenOrRefresh([new StubCompletionData("sample", "Sample documentation.")], TextRange.FromBounds(0, 5))
					);

					Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);

					hosted.Completion.Dismiss();

					// Dismiss funnels the cleanup through the presenter's close, so the window is dropped,
					// its closure is reported once, and the presentation is cleared without a duplicate pass.
					Assert.IsNull(hosted.Presenter.ActiveWindow);
					Assert.AreEqual(1, closedCount);
					Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
					Assert.IsFalse(hosted.Controller.CurrentPresentation.IsTooltipVisible);

					// A second close has nothing left to clean up and must not report the closure again.
					hosted.Completion.Dismiss();

					Assert.AreEqual(1, closedCount);
				}
			}
		}
	}

	[TestMethod]
	public void Dispose_WhenWindowIsOpen_ClosesTheWindowAndClearsState()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			Assert.IsTrue(hosted.Completion.OpenOrRefresh([new StubCompletionData("sample", "Sample documentation.")], TextRange.FromBounds(0, 5)));
			Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);

			hosted.Controller.Dispose();
			hosted.Completion.Dispose();

			Assert.IsNull(hosted.Presenter.ActiveWindow);
			Assert.IsNull(hosted.Completion.WindowPresenter.ActiveWindow);
			Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
		}
	}

	[TestMethod]
	public void OpenOrRefresh_AfterDisposal_WithItems_ReturnsFalseAndOpensNothing()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			hosted.Controller.Dispose();
			hosted.Completion.Dispose();

			// The non-empty call is the disposal path the empty-list coverage does not reach: it must report its
			// documented not-applied result and must not create a window.
			Assert.IsFalse(hosted.Completion.OpenOrRefresh([new StubCompletionData("item")], TextRange.FromBounds(0, 1)));
			Assert.IsFalse(hosted.Presenter.IsWindowOpen);
			Assert.IsNull(hosted.Completion.WindowPresenter.ActiveWindow);
		}
	}

	[TestMethod]
	public void ScheduleDismissWhenEmpty_WithItemsPresent_KeepsTheWindowOpen()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(
						hosted.Completion.OpenOrRefresh([new StubCompletionData("sample", "Sample documentation.")], TextRange.FromBounds(0, 5))
					);

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					AwaitPopulatedList(completionWindow);
					hosted.Completion.ScheduleDismissWhenEmpty();
					DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(50.0));

					// The posted empty-list check ran, but the list still has items, so the window stays open.
					Assert.IsTrue(hosted.Presenter.IsWindowOpen);
				}
			}
		}
	}
}
