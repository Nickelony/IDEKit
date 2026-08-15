using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using System.Windows.Controls;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHost;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

public sealed partial class TextCompletionControllerTooltipTests
{
	[TestMethod]
	public void Tooltip_ThrowingResolver_HidesTooltipAndDoesNotThrow()
	{
		var logger = new CapturingLogger();
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				ResolveDescriptionAsync = (_, _) => throw new InvalidOperationException("Tooltip resolution failed.")
			}, logger: logger);

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample", description: null)], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				completionWindow.CompletionList.SelectItem("sample");
				PumpPastTooltipDebounce(hosted.Controller);

				Assert.IsTrue(CompletionWindowTooltipAccess.TryGetTooltip(completionWindow, out ToolTip? tooltip));
				Assert.IsNotNull(tooltip);

				// A failing resolver closes the tooltip instead of escaping the timer callback, and reports the
				// documented tooltip event id.
				Assert.IsFalse(tooltip.IsOpen);
				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsTooltipVisible);
				Assert.IsNull(hosted.Controller.CurrentPresentation.TooltipContent);
				Assert.AreEqual(1, logger.Entries.Count);
				Assert.AreEqual(1001, logger.Entries[0].EventId.Id);
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

				Assert.IsTrue(CompletionWindowTooltipAccess.TryGetTooltip(completionWindow, out ToolTip? tooltip));
				Assert.IsNotNull(tooltip);

				completionWindow.CompletionList.SelectItem("sample");
				PumpPastTooltipDebounce(hosted.Controller);

				// Cancel the in-flight tooltip update, then let the old resolver finish late.
				hosted.Controller.CancelTooltipUpdate();
				completion.TrySetResult("late resolved description");

				// Settle the dispatcher so the rejection continuation actually runs before the assertions instead
				// of depending on continuation scheduling luck.
				DispatcherTestUtils.PumpUntilIdle();

				// The stale result must not reopen the tooltip or update the presentation state.
				Assert.IsFalse(tooltip.IsOpen);
				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsTooltipVisible);
				Assert.IsNull(hosted.Controller.CurrentPresentation.TooltipContent);
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

				Assert.IsTrue(CompletionWindowTooltipAccess.TryGetTooltip(completionWindow, out ToolTip? tooltip));
				Assert.IsNotNull(tooltip);

				// Selecting the first item starts a resolve that remains in flight.
				completionWindow.CompletionList.SelectItem("sample");
				PumpPastTooltipDebounce(hosted.Controller);
				Assert.AreEqual(1, resolveCount);

				// Selecting another item supersedes the in-flight resolve instead of waiting for it: the
				// new selection's resolve runs and its content is applied.
				completionWindow.CompletionList.SelectItem("second");
				PumpPastTooltipDebounce(hosted.Controller);

				Assert.AreEqual(2, resolveCount);

				var content = tooltip.Content as TextBlock;

				Assert.IsNotNull(content);
				Assert.AreEqual("second description", content.Text);
				Assert.AreEqual("second description", hosted.Controller.CurrentPresentation.TooltipContent);

				// The superseded resolve ignores the canceled token and finishes late; its stale result
				// must not overwrite the newer selection's tooltip or presentation state.
				firstResolution.TrySetResult("late first description");
				DispatcherTestUtils.PumpUntilIdle();

				var lateContent = tooltip.Content as TextBlock;

				Assert.IsNotNull(lateContent);
				Assert.AreEqual("second description", lateContent.Text);
				Assert.AreEqual("second description", hosted.Controller.CurrentPresentation.TooltipContent);
				Assert.IsTrue(tooltip.IsOpen);
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

				Assert.IsTrue(CompletionWindowTooltipAccess.TryGetTooltip(completionWindow, out ToolTip? tooltip));
				Assert.IsNotNull(tooltip);

				completionWindow.CompletionList.SelectItem("sample");
				PumpPastTooltipDebounce(hosted.Controller);
				Assert.AreEqual(1, resolveCount);

				completionWindow.CompletionList.SelectItem("second");
				PumpPastTooltipDebounce(hosted.Controller);
				Assert.AreEqual(2, resolveCount);

				var content = tooltip.Content as TextBlock;

				Assert.IsNotNull(content);
				Assert.AreEqual("second description", content.Text);

				// The superseded resolve faults late; its failure must not close the newer tooltip or clear
				// the newer presentation state, though the failure is still logged with the tooltip event id.
				firstResolution.SetException(new InvalidOperationException("Stale tooltip failure."));
				DispatcherTestUtils.PumpUntil(() => logger.Entries.Count > 0);

				Assert.IsTrue(tooltip.IsOpen);
				Assert.AreEqual("second description", hosted.Controller.CurrentPresentation.TooltipContent);
				Assert.IsTrue(hosted.Controller.CurrentPresentation.IsTooltipVisible);
				Assert.AreEqual(1, logger.Entries.Count);
				Assert.AreEqual(1001, logger.Entries[0].EventId.Id);
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

				Assert.IsTrue(CompletionWindowTooltipAccess.TryGetTooltip(completionWindow, out ToolTip? tooltip));
				Assert.IsNotNull(tooltip);

				completionWindow.CompletionList.SelectItem("sample");
				PumpPastTooltipDebounce(hosted.Controller);
				resolverStarted.Task.GetAwaiter().GetResult();

				// Supersede the in-flight resolve, then let it finish; the late result must not be applied.
				hosted.Controller.CancelTooltipUpdate();
				completion.TrySetResult(null);
				DispatcherTestUtils.PumpUntil(() => observedCanceledRegistration);

				Assert.IsTrue(observedCanceledRegistration);
				Assert.IsFalse(tooltip.IsOpen);
				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsTooltipVisible);
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

				ToolTip tooltip = GetCompletionTooltip(completionWindow);

				// The selection change schedules a debounced update whose delay has not elapsed yet.
				completionWindow.CompletionList.SelectItem("sample");
				Assert.IsTrue(hosted.Controller.TestHooks.IsTooltipUpdatePending);

				// The close cancels the pending update with the window, so the closed window's tooltip is never
				// resolved or opened afterwards.
				hosted.Controller.CloseWindow();

				DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(20.0));

				Assert.AreEqual(0, resolveCount);
				Assert.IsFalse(tooltip.IsOpen);
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

				ToolTip firstTooltip = GetCompletionTooltip(firstWindow);

				firstWindow.CompletionList.SelectItem("sample");
				PumpPastTooltipDebounce(hosted.Controller);
				resolverStarted.Task.GetAwaiter().GetResult();

				// The window is replaced while its tooltip resolve is still in flight; the resolved content
				// belongs to the closed window and must never surface again.
				hosted.Controller.CloseWindow();
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("second", description: null)], 0, 5));

				firstResolution.TrySetResult("late first description");
				DispatcherTestUtils.PumpUntilIdle();

				Assert.IsFalse(firstTooltip.IsOpen);
				Assert.AreNotEqual("late first description", (firstTooltip.Content as TextBlock)?.Text);
			}
		}
	}
}
