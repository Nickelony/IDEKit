using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using Nickelony.IDEKit.LanguageFeatures.Markup;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>Verifies the tooltip's default resolve: an item of the default adapter that carries a resolve callback resolves itself.</summary>
public sealed partial class CompletionPresenterTooltipTests
{
	[TestMethod]
	public void Tooltip_ItemWithAResolveCallback_ShowsTheResolvedMarkdownThroughTheBuilder()
	{
		var builder = new RecordingMarkdownBuilder();

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(hooks: builder.Hooks());

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					// The item has no documentation until it resolves, as an item of a language server does.
					CompletionItem item = new("format") { ResolveCallback = _ => Task.FromResult(ResolvedItem("format", FormatMarkdown)) };

					Assert.IsTrue(hosted.Completion.OpenOrRefresh([new CompletionItemAdapter(item)], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.SelectItem("format");
					PumpPastTooltipDebounce(hosted.Completion);
					DispatcherTestHelpers.PumpUntil(() => builder.Inputs.Count > 0);

					ToolTip tooltip = GetCompletionTooltip(completionWindow);

					Assert.AreEqual(FormatMarkdown, builder.Inputs[0]);
					Assert.Contains(tooltip.Content, builder.Built);
					Assert.IsTrue(tooltip.IsOpen);

					Assert.AreEqual(
						new MarkupContent(FormatMarkdown, MarkupKind.Markdown),
						hosted.Controller.CurrentPresentation.TooltipContent!.Value.Content
					);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_ItemWithAResolveCallbackAndNoBuilder_ShowsTheResolvedMarkdownAsWrappingText()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					CompletionItem item = new("format") { ResolveCallback = _ => Task.FromResult(ResolvedItem("format", FormatMarkdown)) };

					Assert.IsTrue(hosted.Completion.OpenOrRefresh([new CompletionItemAdapter(item)], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					KeepOpenWhenFocusIsLost(completionWindow);
					completionWindow.CompletionList.SelectItem("format");
					PumpPastTooltipDebounce(hosted.Completion);
					DispatcherTestHelpers.PumpUntil(() => GetCompletionTooltip(completionWindow).Content is TextBlock);

					var content = GetCompletionTooltip(completionWindow).Content as TextBlock;

					Assert.AreEqual(FormatMarkdown, content?.Text);
					Assert.AreEqual(TextWrapping.Wrap, content?.TextWrapping);
					Assert.AreEqual(FormatMarkdown, hosted.Controller.CurrentPresentation.TooltipContent!.Value.Content);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_ResolvedItemWithoutDocumentation_KeepsTheDescriptionAlreadyShown()
	{
		var builder = new RecordingMarkdownBuilder();
		var resolved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(hooks: builder.Hooks());

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					CompletionItem item = new("format")
					{
						Documentation = new MarkupContent(FormatMarkdown, MarkupKind.Markdown),

						ResolveCallback = _ =>
						{
							resolved.SetResult();
							return Task.FromResult(new CompletionItem("format"));
						},
					};

					Assert.IsTrue(hosted.Completion.OpenOrRefresh([new CompletionItemAdapter(item)], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.SelectItem("format");
					PumpPastTooltipDebounce(hosted.Completion);
					DispatcherTestHelpers.PumpUntil(() => resolved.Task.IsCompleted);
					DispatcherTestHelpers.PumpUntilIdle();

					ToolTip tooltip = GetCompletionTooltip(completionWindow);

					// The resolved item adds no documentation, so the tooltip keeps the element of the description it already showed.
					Assert.IsTrue(tooltip.IsOpen);
					Assert.Contains(tooltip.Content, builder.Built);
					Assert.IsTrue(hosted.Controller.CurrentPresentation.IsTooltipVisible);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_HostResolver_WinsOverTheItemsOwnResolve()
	{
		int itemResolves = 0;

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(
				hooks: new CompletionPresenterHooks { ResolveDescriptionAsync = (_, _) => Task.FromResult<object?>("host text") }
			);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					CompletionItem item = new("format")
					{
						ResolveCallback = _ =>
						{
							itemResolves++;
							return Task.FromResult(ResolvedItem("format", FormatMarkdown));
						},
					};

					Assert.IsTrue(hosted.Completion.OpenOrRefresh([new CompletionItemAdapter(item)], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.SelectItem("format");
					PumpPastTooltipDebounce(hosted.Completion);
					DispatcherTestHelpers.PumpUntil(() => GetCompletionTooltip(completionWindow).Content is TextBlock { Text: "host text" });

					Assert.AreEqual(0, itemResolves);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_Resolve_RunsAfterTheDebounceAndOnlyForTheSelectedItem()
	{
		int firstResolves = 0;
		int secondResolves = 0;

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					CompletionItem first = new("first")
					{
						ResolveCallback = _ =>
						{
							firstResolves++;
							return Task.FromResult(ResolvedItem("first", "one"));
						},
					};

					CompletionItem second = new("second")
					{
						ResolveCallback = _ =>
						{
							secondResolves++;
							return Task.FromResult(ResolvedItem("second", "two"));
						},
					};

					Assert.IsTrue(
						hosted.Completion.OpenOrRefresh(
							[new CompletionItemAdapter(first), new CompletionItemAdapter(second)],
							TextRange.FromBounds(0, 5)
						)
					);

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					// Both selections happen before the debounce can run, so only the last one is resolved, and nothing resolves in the selection change itself.
					completionWindow.CompletionList.SelectItem("first");
					completionWindow.CompletionList.SelectItem("second");

					Assert.AreEqual(0, firstResolves + secondResolves);

					PumpPastTooltipDebounce(hosted.Completion);
					DispatcherTestHelpers.PumpUntil(() => secondResolves > 0);
					DispatcherTestHelpers.PumpUntilIdle();

					Assert.AreEqual(0, firstResolves);
					Assert.AreEqual(1, secondResolves);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_FailedItemResolve_KeepsTheDescriptionAndLogsTheFailure()
	{
		var logger = new RecordingLogger();
		var builder = new RecordingMarkdownBuilder();

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(hooks: builder.Hooks(), logger: logger);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					CompletionItem item = new("format")
					{
						Documentation = new MarkupContent(FormatMarkdown, MarkupKind.Markdown),
						ResolveCallback = _ => throw new InvalidOperationException("The server failed."),
					};

					Assert.IsTrue(hosted.Completion.OpenOrRefresh([new CompletionItemAdapter(item)], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					KeepOpenWhenFocusIsLost(completionWindow);
					completionWindow.CompletionList.SelectItem("format");
					PumpPastTooltipDebounce(hosted.Completion);
					DispatcherTestHelpers.PumpUntil(() => logger.Entries.Count > 0);
					DispatcherTestHelpers.PumpUntilIdle();

					ToolTip tooltip = GetCompletionTooltip(completionWindow);

					Assert.HasCount(1, logger.Entries);
					Assert.AreEqual(1001, logger.Entries[0].EventId.Id);
					Assert.IsTrue(tooltip.IsOpen);
					Assert.Contains(tooltip.Content, builder.Built);
					Assert.IsTrue(hosted.Controller.CurrentPresentation.IsTooltipVisible);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_CanceledItemResolve_KeepsTheDescriptionWithoutLogging()
	{
		var logger = new RecordingLogger();
		var resolved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController(logger: logger);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					CompletionItem item = new("format")
					{
						Documentation = "Formats its arguments.",

						ResolveCallback = _ =>
						{
							resolved.SetResult();
							throw new OperationCanceledException();
						},
					};

					Assert.IsTrue(hosted.Completion.OpenOrRefresh([new CompletionItemAdapter(item)], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.SelectItem("format");
					PumpPastTooltipDebounce(hosted.Completion);
					DispatcherTestHelpers.PumpUntil(() => resolved.Task.IsCompleted);
					DispatcherTestHelpers.PumpUntilIdle();

					// A canceled resolve is a superseded one, so it reports no failure and the tooltip keeps what it showed.
					Assert.IsEmpty(logger.Entries);
					Assert.AreEqual("Formats its arguments.", (GetCompletionTooltip(completionWindow).Content as TextBlock)?.Text);
					Assert.IsTrue(hosted.Controller.CurrentPresentation.IsTooltipVisible);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_MarkdownDocumentationEqualToItsResolved_RunsTheBuilderOncePerSelection()
	{
		var builder = new RecordingMarkdownBuilder();
		var resolved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(hooks: builder.Hooks());

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					CompletionItem item = new("format")
					{
						Documentation = new MarkupContent(FormatMarkdown, MarkupKind.Markdown),

						ResolveCallback = _ =>
						{
							resolved.SetResult();
							return Task.FromResult(ResolvedItem("format", FormatMarkdown));
						},
					};

					Assert.IsTrue(hosted.Completion.OpenOrRefresh([new CompletionItemAdapter(item)], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.SelectItem("format");
					PumpPastTooltipDebounce(hosted.Completion);
					DispatcherTestHelpers.PumpUntil(() => resolved.Task.IsCompleted);
					DispatcherTestHelpers.PumpUntilIdle();

					// The selection change built the element, and the debounced pass and the resolved result reuse it.
					Assert.HasCount(1, builder.Inputs);
					Assert.AreSame(builder.Built[0], GetCompletionTooltip(completionWindow).Content);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_MarkdownDocumentationWithoutAResolveCallback_RunsTheBuilderOncePerSelection()
	{
		var builder = new RecordingMarkdownBuilder();

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(hooks: builder.Hooks());

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([MarkdownItem("format", FormatMarkdown)], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.SelectItem("format");
					PumpPastTooltipDebounce(hosted.Completion);

					Assert.HasCount(1, builder.Inputs);
					Assert.AreSame(builder.Built[0], GetCompletionTooltip(completionWindow).Content);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_ReselectingAnItemOfAnotherDocumentation_BuildsAgain()
	{
		var builder = new RecordingMarkdownBuilder();

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(hooks: builder.Hooks());

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(
						hosted.Completion.OpenOrRefresh(
							[MarkdownItem("format", FormatMarkdown), MarkdownItem("rep", "Repeats `s`.")],
							TextRange.FromBounds(0, 5)
						)
					);

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.SelectItem("format");
					PumpPastTooltipDebounce(hosted.Completion);
					completionWindow.CompletionList.SelectItem("rep");
					PumpPastTooltipDebounce(hosted.Completion);

					Assert.AreSequenceEqual([FormatMarkdown, "Repeats `s`."], builder.Inputs);
					Assert.AreSame(builder.Built[1], GetCompletionTooltip(completionWindow).Content);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_ElementOfAClosedWindow_IsNotReusedByTheNextWindow()
	{
		var builder = new RecordingMarkdownBuilder();

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(hooks: builder.Hooks());

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([MarkdownItem("format", FormatMarkdown)], TextRange.FromBounds(0, 5)));

					CompletionWindow first =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					first.CompletionList.SelectItem("format");
					PumpPastTooltipDebounce(hosted.Completion);

					hosted.Completion.Dismiss();

					Assert.IsTrue(hosted.Completion.OpenOrRefresh([MarkdownItem("format", FormatMarkdown)], TextRange.FromBounds(0, 5)));

					CompletionWindow second =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					second.CompletionList.SelectItem("format");
					PumpPastTooltipDebounce(hosted.Completion);

					// An element belongs to the tooltip it was shown in, so the next window builds its own.
					Assert.HasCount(2, builder.Inputs);
					Assert.AreSame(builder.Built[1], GetCompletionTooltip(second).Content);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_ReselectedItem_ResolvesOnceAndShowsTheKeptResult()
	{
		int resolves = 0;

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					var first = new CompletionItemAdapter(
						new CompletionItem("first")
						{
							ResolveCallback = _ =>
							{
								resolves++;
								return Task.FromResult(ResolvedItem("first", "one"));
							},
						}
					);

					var second = new CompletionItemAdapter(new CompletionItem("second"));

					Assert.IsTrue(hosted.Completion.OpenOrRefresh([first, second], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.ListBox.SelectedItem = first;
					PumpPastTooltipDebounce(hosted.Completion);
					DispatcherTestHelpers.PumpUntil(() => GetCompletionTooltip(completionWindow).Content is TextBlock { Text: "one" });

					completionWindow.CompletionList.ListBox.SelectedItem = second;
					PumpPastTooltipDebounce(hosted.Completion);
					DispatcherTestHelpers.PumpUntilIdle();

					completionWindow.CompletionList.ListBox.SelectedItem = first;
					PumpPastTooltipDebounce(hosted.Completion);
					DispatcherTestHelpers.PumpUntil(() => GetCompletionTooltip(completionWindow).Content is TextBlock { Text: "one" });

					Assert.AreEqual(1, resolves);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_FailedResolve_IsAskedAgainWhenTheItemIsSelectedAgain()
	{
		int resolves = 0;

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(logger: new RecordingLogger());

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					var first = new CompletionItemAdapter(
						new CompletionItem("first")
						{
							ResolveCallback = _ =>
							{
								resolves++;

								return resolves == 1
									? throw new InvalidOperationException("The server failed.")
									: Task.FromResult(ResolvedItem("first", "one"));
							},
						}
					);

					var second = new CompletionItemAdapter(new CompletionItem("second"));

					Assert.IsTrue(hosted.Completion.OpenOrRefresh([first, second], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.ListBox.SelectedItem = first;
					PumpPastTooltipDebounce(hosted.Completion);
					DispatcherTestHelpers.PumpUntil(() => resolves == 1);
					DispatcherTestHelpers.PumpUntilIdle();

					completionWindow.CompletionList.ListBox.SelectedItem = second;
					PumpPastTooltipDebounce(hosted.Completion);
					DispatcherTestHelpers.PumpUntilIdle();

					completionWindow.CompletionList.ListBox.SelectedItem = first;
					PumpPastTooltipDebounce(hosted.Completion);
					DispatcherTestHelpers.PumpUntil(() => GetCompletionTooltip(completionWindow).Content is TextBlock { Text: "one" });

					// A failure keeps nothing, so the second selection asks again and shows what the server then sends.
					Assert.AreEqual(2, resolves);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_CanceledResolve_IsAskedAgainWhenTheItemIsSelectedAgain()
	{
		int resolves = 0;

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					var first = new CompletionItemAdapter(
						new CompletionItem("first")
						{
							ResolveCallback = _ =>
							{
								resolves++;
								return resolves == 1 ? throw new OperationCanceledException() : Task.FromResult(ResolvedItem("first", "one"));
							},
						}
					);

					var second = new CompletionItemAdapter(new CompletionItem("second"));

					Assert.IsTrue(hosted.Completion.OpenOrRefresh([first, second], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.ListBox.SelectedItem = first;
					PumpPastTooltipDebounce(hosted.Completion);
					DispatcherTestHelpers.PumpUntil(() => resolves == 1);
					DispatcherTestHelpers.PumpUntilIdle();

					completionWindow.CompletionList.ListBox.SelectedItem = second;
					PumpPastTooltipDebounce(hosted.Completion);
					DispatcherTestHelpers.PumpUntilIdle();

					completionWindow.CompletionList.ListBox.SelectedItem = first;
					PumpPastTooltipDebounce(hosted.Completion);
					DispatcherTestHelpers.PumpUntil(() => GetCompletionTooltip(completionWindow).Content is TextBlock { Text: "one" });

					Assert.AreEqual(2, resolves);
				}
			}
		}
	}

	// The editor closes its completion window when the text area loses the keyboard focus unless the window is active, and another process taking
	// the foreground during a parallel run raises that loss mid-test. A window reported active stays open, so the focus state of the desktop stays
	// out of the test.
	private static void KeepOpenWhenFocusIsLost(CompletionWindow completionWindow) => TestWindowActivation.Activate(completionWindow);

	private static CompletionItem ResolvedItem(string label, string markdown) =>
		new(label) { Documentation = new MarkupContent(markdown, MarkupKind.Markdown) };
}
