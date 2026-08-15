using Avalonia.Controls;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHost;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

/// <summary>
/// Pins the completion controller's tooltip-aware behavior on AvaloniaEdit.
/// </summary>
/// <remarks>
/// Avalonia divergence: AvaloniaEdit publishes no completion tooltip member, so
/// <see cref="CompletionWindowTooltipAccess.TryGetTooltip"/> always reports the tooltip as unavailable and
/// the controller gracefully disables the tooltip path - it never schedules a debounced update, never
/// invokes a description resolver or the configuration hook, and never reports tooltip state. The reference
/// suite asserted the rendered tooltip (wrapping text block, stock/controller rendering parity, resolver
/// results, skin chrome); none of that surface exists in the mirror, so every test below pins the observable
/// graceful-disable contract the mirror actually produces instead, and the controller's window/list behavior
/// stays covered by the sibling suites.
/// </remarks>
[AvaloniaTestClass]
public sealed partial class TextCompletionControllerTooltipTests
{
	[TestMethod]
	public void Tooltip_StringDescription_IsNotShownWithoutATooltipMember()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				// The hooks carry no scheduled-request callback: the disabled tooltip path must not depend
				// on scheduling either, so opening and selecting must not arm a tooltip update.
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				completionWindow.CompletionList.SelectItem("sample");

				// The controller's window and list still work; only the tooltip decoration is disabled.
				Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);
				AssertTooltipPathDisabled(hosted.Controller, completionWindow);
			}
		}
	}

	[TestMethod]
	public void Tooltip_StringDescription_DoesNotScheduleAParallelControllerRendering()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// The reference compared the stock handler's rendering with the controller's debounced
				// rewrite of the same tooltip. Avalonia divergence: the mirror writes no tooltip at all, so
				// there is no second writer to compare; the assertion that no controller rendering is
				// scheduled pins the only observable difference.
				completionWindow.CompletionList.SelectItem("sample");

				Assert.IsFalse(
					hosted.Controller.TestHooks.IsTooltipUpdatePending,
					"The disabled tooltip path must not schedule a controller rendering.");
			}
		}
	}

	[TestMethod]
	public void Tooltip_ResolvedDescription_IsNotRequestedWithoutATooltipMember()
	{
		int resolverCalls = 0;
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				ResolveDescriptionAsync = (_, _) =>
				{
					resolverCalls++;
					return Task.FromResult<object?>("resolved description");
				}
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				// No schedule initialization: the tooltip timer is armed by the controller constructor.
				// Avalonia divergence: the timer is armed but the disabled tooltip path never enters the
				// description resolve, so the resolver stays uninvoked and no content is displayed.
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample", description: null)], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				completionWindow.CompletionList.SelectItem("sample");

				Assert.AreEqual(0, resolverCalls);
				AssertTooltipPathDisabled(hosted.Controller, completionWindow);
			}
		}
	}

	[TestMethod]
	public void Tooltip_NullDescriptionWithoutResolver_StaysHidden()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample", description: null)], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				completionWindow.CompletionList.SelectItem("sample");

				AssertTooltipPathDisabled(hosted.Controller, completionWindow);
			}
		}
	}

	[TestMethod]
	public void Tooltip_NoSelectedItem_DoesNotReportTooltipState()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				completionWindow.CompletionList.SelectItem("sample");

				// Clearing the selection drives the documented "no selected item" branch of the debounced
				// update. Avalonia divergence: the disabled tooltip path owns no update to run that branch,
				// so clearing the selection must simply leave the presentation tooltip-free.
				ListBox listBox = completionWindow.CompletionList.ListBox
					?? throw new InvalidOperationException("The completion list box was not available.");

				listBox.SelectedItem = null;

				Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);
				AssertTooltipPathDisabled(hosted.Controller, completionWindow);
			}
		}
	}

	[TestMethod]
	public void Tooltip_ResolverReturningNullContent_IsNeverInvoked()
	{
		int resolverCalls = 0;
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				ResolveDescriptionAsync = (_, _) =>
				{
					resolverCalls++;
					return Task.FromResult<object?>(null);
				}
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				completionWindow.CompletionList.SelectItem("sample");

				// The item's synchronous description was applied first; the resolver's null content then
				// hides the tooltip instead of leaving the previous content visible. Avalonia divergence: the
				// disabled tooltip path never invokes the resolver, so neither the null result nor the
				// synchronous description can surface in a tooltip.
				Assert.AreEqual(0, resolverCalls);
				AssertTooltipPathDisabled(hosted.Controller, completionWindow);
			}
		}
	}

	[TestMethod]
	public void CloseWindow_WithTheTooltipPathDisabled_ClearsThePresentation()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				completionWindow.CompletionList.SelectItem("sample");

				Assert.IsTrue(hosted.Controller.CurrentPresentation.IsListVisible);

				// Closing the window resets the presentation state with the window.
				hosted.Controller.CloseWindow();

				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsTooltipVisible);
				Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
				Assert.IsNull(hosted.Coordinator.ActiveWindow);
			}
		}
	}

	[TestMethod]
	public void Tooltip_NonStringDescription_IsNotWrittenToATooltip()
	{
		var content = new StackPanel();
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([new TestCompletionData("sample", content)], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				completionWindow.CompletionList.SelectItem("sample");

				// Non-string descriptions are opaque to the controller and are assigned to the tooltip
				// unchanged instead of being wrapped like string descriptions. Avalonia divergence: there is
				// no tooltip to assign to, so the content element stays untouched.
				Assert.IsEmpty(content.Children);
				AssertTooltipPathDisabled(hosted.Controller, completionWindow);
			}
		}
	}

	// Asserts the documented graceful-disable contract the mirror implements. Because AvaloniaEdit exposes no
	// tooltip member, the controller never schedules a tooltip update, never reports tooltip state, and the
	// accessor reports the tooltip as unavailable.
	private static void AssertTooltipPathDisabled(TextCompletionController controller, CompletionWindow completionWindow)
	{
		Assert.IsFalse(CompletionWindowTooltipAccess.IsFieldAvailable);
		Assert.IsFalse(CompletionWindowTooltipAccess.TryGetTooltip(completionWindow, out ToolTip? tooltip));
		Assert.IsNull(tooltip);
		Assert.IsFalse(controller.TestHooks.IsTooltipUpdatePending, "The disabled tooltip path must not schedule an update.");
		Assert.IsFalse(controller.CurrentPresentation.IsTooltipVisible);
		Assert.IsNull(controller.CurrentPresentation.TooltipContent);
	}
}
