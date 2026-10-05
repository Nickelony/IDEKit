using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

[STATestClass]
[TestCategory(TestCategories.InteractiveWindow)]
public sealed partial class CompletionPresenterTooltipTests
{
	[TestMethod]
	public void Tooltip_StringDescription_IsShownAsWrappingTextBlock_WithoutScheduledRequests()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					// The hooks carry no scheduled-request callback: tooltip support must not depend on scheduling.
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.SelectItem("sample");
					PumpPastTooltipDebounce(hosted.Completion);

					Assert.IsTrue(CompletionTooltipResolver.TryGetTooltip(completionWindow, out ToolTip? tooltip));
					Assert.IsNotNull(tooltip);

					var content = tooltip.Content as TextBlock;

					Assert.IsNotNull(content, $"Expected a wrapping TextBlock but found {tooltip.Content?.GetType().Name ?? "null"}.");
					Assert.AreEqual(TextWrapping.Wrap, content.TextWrapping);
					Assert.AreEqual("A description that must wrap.", content.Text);

					// The presenter's own debounced path produced the tooltip, not only AvalonEdit's stock handler.
					Assert.IsTrue(hosted.Controller.CurrentPresentation.IsTooltipVisible);
					Assert.AreEqual("A description that must wrap.", hosted.Controller.CurrentPresentation.TooltipContent!.Value.Content);
				}
			}
		}
	}

	[TestMethod]
	public void TooltipResolveDelay_OfThePresentation_HoldsBackTheDescriptionResolve()
	{
		int resolveCount = 0;

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController(
				hooks: CompletionTooltipTestHelpers.CreateCountingHooks(onResolve: () => resolveCount++),

				presentation: CreatePresentation() with
				{
					TooltipResolveDelay = TimeSpan.FromHours(1.0),
				}
			);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.SelectItem("sample");
					DispatcherTestHelpers.PumpFrames(TimeSpan.FromMilliseconds(50.0));

					// The selection armed the debounce with the delay of the presentation, so the resolve has not run yet.
					Assert.IsTrue(hosted.Completion.IsTooltipUpdatePending);
					Assert.AreEqual(0, resolveCount);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_StringDescription_ControllerRenderingMatchesStockRendering()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					Assert.IsTrue(CompletionTooltipResolver.TryGetTooltip(completionWindow, out ToolTip? tooltip));

					// AvalonEdit's stock selection-changed handler renders a string description immediately.
					completionWindow.CompletionList.SelectItem("sample");

					var stockContent = tooltip!.Content as TextBlock;

					Assert.IsNotNull(
						stockContent,
						$"Expected the stock handler to render a TextBlock but found {tooltip.Content?.GetType().Name ?? "null"}."
					);

					PumpPastTooltipDebounce(hosted.Completion);

					// The controller's debounced pass rewrites the same tooltip. Both writers must render the
					// same string shape; a stock rendering change that the controller does not mirror would
					// otherwise diverge silently.
					var controllerContent = tooltip.Content as TextBlock;

					Assert.IsNotNull(
						controllerContent,
						$"Expected the controller to render a TextBlock but found {tooltip.Content?.GetType().Name ?? "null"}."
					);

					Assert.AreNotSame(stockContent, controllerContent, "The controller must rewrite the tooltip content.");

					Assert.AreSequenceEqual(
						GetLocallySetProperties(stockContent),
						GetLocallySetProperties(controllerContent),
						"The stock and controller tooltip renderings must set the same local properties."
					);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_ResolvedDescription_IsDisplayedAfterDebounce()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(
				hooks: new CompletionPresenterHooks { ResolveDescriptionAsync = (_, _) => Task.FromResult<object?>("resolved description") }
			);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					// No schedule initialization: the constructor creates the tooltip debouncer.
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample", description: null)], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.SelectItem("sample");
					PumpPastTooltipDebounce(hosted.Completion);

					Assert.IsTrue(CompletionTooltipResolver.TryGetTooltip(completionWindow, out ToolTip? tooltip));
					Assert.IsNotNull(tooltip);

					var content = tooltip.Content as TextBlock;

					Assert.IsNotNull(content);
					Assert.AreEqual("resolved description", content.Text);
					Assert.IsTrue(tooltip.IsOpen);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_NullDescriptionWithoutResolver_StaysHidden()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample", description: null)], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.SelectItem("sample");
					PumpPastTooltipDebounce(hosted.Completion);

					Assert.IsTrue(CompletionTooltipResolver.TryGetTooltip(completionWindow, out ToolTip? tooltip));
					Assert.IsNotNull(tooltip);
					Assert.IsFalse(tooltip.IsOpen);
					Assert.IsFalse(hosted.Controller.CurrentPresentation.IsTooltipVisible);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_NoSelectedItem_ClosesTheTooltipAndClearsTheState()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.SelectItem("sample");
					PumpPastTooltipDebounce(hosted.Completion);

					Assert.IsTrue(CompletionTooltipResolver.TryGetTooltip(completionWindow, out ToolTip? tooltip));
					Assert.IsNotNull(tooltip);
					Assert.IsTrue(hosted.Controller.CurrentPresentation.IsTooltipVisible);

					// Clearing the selection drives the documented "no selected item" branch of the debounced
					// update: an already open tooltip closes and the presentation state resets.
					ListBox listBox =
						completionWindow.CompletionList.ListBox ?? throw new InvalidOperationException("The completion list box was not available.");

					listBox.SelectedItem = null;
					PumpPastTooltipDebounce(hosted.Completion);

					Assert.IsFalse(tooltip.IsOpen);
					Assert.IsFalse(hosted.Controller.CurrentPresentation.IsTooltipVisible);
					Assert.IsNull(hosted.Controller.CurrentPresentation.TooltipContent);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_ResolverReturningNullContent_HidesTheTooltip()
	{
		int resolverCalls = 0;

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(
				hooks: new CompletionPresenterHooks
				{
					ResolveDescriptionAsync = (_, _) =>
					{
						resolverCalls++;
						return Task.FromResult<object?>(null);
					},
				}
			);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.SelectItem("sample");
					PumpPastTooltipDebounce(hosted.Completion);

					// The item's synchronous description was applied first; the resolver's null content then hides
					// the tooltip instead of leaving the previous content visible.
					Assert.AreEqual(1, resolverCalls);
					Assert.IsTrue(CompletionTooltipResolver.TryGetTooltip(completionWindow, out ToolTip? tooltip));
					Assert.IsNotNull(tooltip);
					Assert.IsFalse(tooltip.IsOpen);
					Assert.IsFalse(hosted.Controller.CurrentPresentation.IsTooltipVisible);
					Assert.IsNull(hosted.Controller.CurrentPresentation.TooltipContent);
				}
			}
		}
	}

	[TestMethod]
	public void Dismiss_WithVisibleTooltip_HidesTheTooltipAndClearsThePresentation()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.SelectItem("sample");
					PumpPastTooltipDebounce(hosted.Completion);

					Assert.IsTrue(CompletionTooltipResolver.TryGetTooltip(completionWindow, out ToolTip? tooltip));
					Assert.IsNotNull(tooltip);
					Assert.IsTrue(hosted.Controller.CurrentPresentation.IsTooltipVisible);

					// Closing the window hides the tooltip with the window and resets the presentation state.
					hosted.Completion.Dismiss();

					Assert.IsFalse(tooltip.IsOpen);
					Assert.IsFalse(hosted.Controller.CurrentPresentation.IsTooltipVisible);
					Assert.IsFalse(hosted.Controller.CurrentPresentation.IsListVisible);
					Assert.IsNull(hosted.Presenter.ActiveWindow);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_NonStringDescription_IsAssignedUnchanged()
	{
		var content = new StackPanel();

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([new StubCompletionData("sample", content)], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.SelectItem("sample");
					PumpPastTooltipDebounce(hosted.Completion);

					// Non-string descriptions are opaque to the controller and are assigned to the tooltip unchanged
					// instead of being wrapped like string descriptions.
					Assert.IsTrue(CompletionTooltipResolver.TryGetTooltip(completionWindow, out ToolTip? tooltip));
					Assert.IsNotNull(tooltip);
					Assert.AreSame(content, tooltip.Content);
					Assert.IsTrue(hosted.Controller.CurrentPresentation.IsTooltipVisible);
					Assert.AreSame(content, hosted.Controller.CurrentPresentation.TooltipContent!.Value.Content);
				}
			}
		}
	}

	// Captures the properties each writer explicitly set, so the equivalence check covers any property an
	// upstream stock-rendering change adds without the controller following. Sorted by name because the
	// local-value enumeration order is not part of any contract.
	private static List<(string Property, object? Value)> GetLocallySetProperties(DependencyObject element)
	{
		var values = new List<(string, object?)>();
		LocalValueEnumerator enumerator = element.GetLocalValueEnumerator();

		while (enumerator.MoveNext())
		{
			LocalValueEntry entry = enumerator.Current;
			values.Add((entry.Property.Name, entry.Value));
		}

		values.Sort((left, right) => string.CompareOrdinal(left.Item1, right.Item1));
		return values;
	}
}
