using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHost;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

public sealed partial class TextCompletionControllerTooltipTests
{
	[TestMethod]
	public void Tooltip_ThrowingResolver_IsNeverInvokedAndDoesNotThrow()
	{
		var logger = new CapturingLogger();
		bool resolveInvoked = false;
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				ResolveDescriptionAsync = (_, _) =>
				{
					resolveInvoked = true;
					throw new InvalidOperationException("Tooltip resolution failed.");
				}
			}, logger: logger);

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample", description: null)], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				completionWindow.CompletionList.SelectItem("sample");
				DispatcherTestUtils.PumpUntilIdle();

				// A failing resolver closes the tooltip instead of escaping the timer callback, and reports the
				// documented tooltip event id. Avalonia divergence: the disabled tooltip path never invokes the
				// resolver, so there is no failure to contain or report (event id 1001 never appears); only the
				// one-time unsupported-access warning (event id 1002) is produced when a window is opened.
				Assert.IsFalse(resolveInvoked);
				Assert.IsFalse(logger.Entries.Exists(entry => entry.EventId.Id == 1001));
				AssertTooltipPathDisabled(hosted.Controller, completionWindow);
			}
		}
	}

	[TestMethod]
	public void Tooltip_ResolverResultAfterCancel_IsNotApplied()
	{
		var completion = new TaskCompletionSource<object?>();
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				ResolveDescriptionAsync = (_, _) => completion.Task
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample", description: null)], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				completionWindow.CompletionList.SelectItem("sample");

				// Cancel the in-flight tooltip update, then let the old resolver finish late.
				hosted.Controller.CancelTooltipUpdate();
				completion.TrySetResult("late resolved description");

				// Settle the dispatcher so the rejection continuation actually runs before the assertions
				// instead of depending on continuation scheduling luck.
				DispatcherTestUtils.PumpUntilIdle();

				// The stale result must not reopen the tooltip or update the presentation state. Avalonia
				// divergence: the disabled tooltip path never started the resolve, so the late result has no
				// channel to reach the controller and the presentation stays tooltip-free.
				AssertTooltipPathDisabled(hosted.Controller, completionWindow);
			}
		}
	}

	[TestMethod]
	public void Tooltip_SelectionChangesWhileResolveIsInFlight_LateResultForThePreviousItemIsIgnored()
	{
		var firstResolution = new TaskCompletionSource<object?>();
		int resolveCount = 0;
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				// The first resolve stays in flight until the test completes it; later resolves finish
				// immediately so the new selection's tooltip is shown before the first one completes.
				ResolveDescriptionAsync = (_, _) =>
				{
					resolveCount++;

					return resolveCount == 1
						? firstResolution.Task
						: Task.FromResult<object?>("second description");
				}
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh(
					[CreateItem("sample", description: null), CreateItem("second", description: null)],
					0,
					5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// Selecting the first item would start a resolve that remains in flight. Avalonia divergence:
				// the disabled tooltip path never enters the resolver for any selection, so no resolve is
				// started or superseded at all.
				completionWindow.CompletionList.SelectItem("sample");
				completionWindow.CompletionList.SelectItem("second");

				Assert.AreEqual(0, resolveCount);

				// The would-be in-flight resolve finishes late; its stale result must not overwrite the newer
				// selection's tooltip or presentation state.
				firstResolution.TrySetResult("late first description");
				DispatcherTestUtils.PumpUntilIdle();

				AssertTooltipPathDisabled(hosted.Controller, completionWindow);
			}
		}
	}

	[TestMethod]
	public void Tooltip_SupersededResolverFailsLate_DoesNotClearTheNewerTooltip()
	{
		var logger = new CapturingLogger();
		var firstResolution = new TaskCompletionSource<object?>();
		int resolveCount = 0;
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				// The first resolve stays in flight until the test faults it; later resolves finish
				// immediately so the new selection's tooltip is shown before the first one fails.
				ResolveDescriptionAsync = (_, _) => ++resolveCount == 1
					? firstResolution.Task
					: Task.FromResult<object?>("second description")
			}, logger: logger);

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh(
					[CreateItem("sample", description: null), CreateItem("second", description: null)],
					0,
					5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				completionWindow.CompletionList.SelectItem("sample");
				completionWindow.CompletionList.SelectItem("second");

				Assert.AreEqual(0, resolveCount);

				// The superseded resolve faults late; its failure must not close the newer tooltip or clear
				// the newer presentation state, though the failure is still logged with the tooltip event id.
				// Avalonia divergence: the resolver never runs, so no failure is logged (event id 1001) and
				// the presentation stays tooltip-free.
				firstResolution.SetException(new InvalidOperationException("Stale tooltip failure."));
				DispatcherTestUtils.PumpUntilIdle();

				Assert.IsFalse(logger.Entries.Exists(entry => entry.EventId.Id == 1001));
				AssertTooltipPathDisabled(hosted.Controller, completionWindow);
			}
		}
	}

	[TestMethod]
	public void Tooltip_ResolverRegisteringAfterCancellation_SkipsTheLateResult()
	{
		var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolverStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		bool observedCanceledRegistration = false;

		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				ResolveDescriptionAsync = async (_, cancellationToken) =>
				{
					resolverStarted.TrySetResult();
					await completion.Task.ConfigureAwait(true);

					// The superseded resolve may still register on its canceled token; the registration must
					// observe cancellation instead of an ObjectDisposedException.
					using CancellationTokenRegistration registration = cancellationToken.Register(static () => { });
					observedCanceledRegistration = true;
					return (object?)"resolved too late";
				}
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample", description: null)], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				completionWindow.CompletionList.SelectItem("sample");

				// Supersede the in-flight resolve, then let it finish; the late result must not be applied.
				// Avalonia divergence: the disabled tooltip path never entered the resolver, so the
				// cancellation-registration branch is never reached. The sentinel must not complete.
				hosted.Controller.CancelTooltipUpdate();
				completion.TrySetResult(null);
				DispatcherTestUtils.PumpUntilIdle();

				Assert.IsFalse(resolverStarted.Task.IsCompleted);
				Assert.IsFalse(observedCanceledRegistration);
				AssertTooltipPathDisabled(hosted.Controller, completionWindow);
			}
		}
	}

	[TestMethod]
	public void Tooltip_WindowClosedBeforeTheDebounce_DoesNotResolveIntoTheClosedWindow()
	{
		int resolveCount = 0;
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				ResolveDescriptionAsync = (_, _) =>
				{
					resolveCount++;
					return Task.FromResult<object?>("late description");
				}
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample", description: null)], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// The selection change would schedule a debounced update whose delay has not elapsed yet.
				// Avalonia divergence: the disabled tooltip path arms no debounce at all, so the pending flag
				// is already false before the close.
				completionWindow.CompletionList.SelectItem("sample");
				Assert.IsFalse(hosted.Controller.TestHooks.IsTooltipUpdatePending);

				// The close cancels any pending update with the window, so the closed window's tooltip is
				// never resolved or opened afterwards.
				hosted.Controller.CloseWindow();

				DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(20.0));

				Assert.AreEqual(0, resolveCount);
				Assert.IsFalse(CompletionWindowTooltipAccess.TryGetTooltip(completionWindow, out _));
			}
		}
	}

	[TestMethod]
	public void Tooltip_WindowReplacedWhileResolveIsInFlight_LateResultIsNotAppliedToTheClosedWindow()
	{
		var firstResolution = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolverStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		int resolveCount = 0;
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				ResolveDescriptionAsync = (_, _) =>
				{
					resolveCount++;

					if (resolveCount > 1)
						return Task.FromResult<object?>("second description");

					resolverStarted.TrySetResult();
					return firstResolution.Task;
				}
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample", description: null)], 0, 5));

				CompletionWindow firstWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				firstWindow.CompletionList.SelectItem("sample");

				// The window is replaced while its tooltip resolve would still be in flight; the resolved
				// content belongs to the closed window and must never surface again. Avalonia divergence: the
				// disabled tooltip path never started the resolve, so there is no late content to leak.
				Assert.IsFalse(resolverStarted.Task.IsCompleted);

				hosted.Controller.CloseWindow();
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("second", description: null)], 0, 5));

				firstResolution.TrySetResult("late first description");
				DispatcherTestUtils.PumpUntilIdle();

				Assert.IsFalse(firstWindow.IsOpen);
				Assert.AreEqual(0, resolveCount);
				Assert.IsFalse(CompletionWindowTooltipAccess.TryGetTooltip(firstWindow, out _));
			}
		}
	}
}
