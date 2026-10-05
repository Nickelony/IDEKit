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

/// <summary>Verifies how the completion tooltip shows Markdown documentation through the host's Markdown builder.</summary>
public sealed partial class CompletionPresenterTooltipTests
{
	private const string FormatMarkdown = "```lua\nstring.format(f, ...)\n```\n\n**Formats** its arguments:\n\n- `%d`\n- `%s`";

	[TestMethod]
	public void Tooltip_MarkdownDocumentation_ShowsTheBuiltElementInTheSelectionChange()
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

					// The editor's handler wrote the raw Markdown first; the element replaced it before any frame could show it, so the debounce did not run.
					Assert.IsTrue(hosted.Completion.IsTooltipUpdatePending);
					Assert.HasCount(1, builder.Inputs);
					Assert.AreEqual(FormatMarkdown, builder.Inputs[0]);
					Assert.AreSame(builder.Built[0], GetCompletionTooltip(completionWindow).Content);
					Assert.IsTrue(GetCompletionTooltip(completionWindow).IsOpen);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_MarkdownDocumentation_StaysRenderedAfterTheDebouncedUpdateAndReportsTheDocumentation()
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

					ToolTip tooltip = GetCompletionTooltip(completionWindow);

					Assert.IsTrue(tooltip.IsOpen);
					Assert.Contains(tooltip.Content, builder.Built, "The tooltip shows an element the builder returned.");
					Assert.IsTrue(hosted.Controller.CurrentPresentation.IsTooltipVisible);

					// The state carries the documentation with its kind, so a host that renders from the state renders Markdown too.
					Assert.AreEqual(
						new MarkupContent(FormatMarkdown, MarkupKind.Markdown),
						hosted.Controller.CurrentPresentation.TooltipContent!.Value.Content
					);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_PlainTextDocumentation_NeverReachesTheMarkdownBuilder()
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
					var plain = new CompletionItemAdapter(new CompletionItem("print") { Documentation = new MarkupContent("**not** markdown") });

					Assert.IsTrue(hosted.Completion.OpenOrRefresh([plain], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.SelectItem("print");
					PumpPastTooltipDebounce(hosted.Completion);

					var content = GetCompletionTooltip(completionWindow).Content as TextBlock;

					Assert.IsNotNull(content);
					Assert.AreEqual("**not** markdown", content.Text);
					Assert.AreEqual(TextWrapping.Wrap, content.TextWrapping);
					Assert.IsEmpty(builder.Inputs);
					Assert.AreEqual("**not** markdown", hosted.Controller.CurrentPresentation.TooltipContent!.Value.Content);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_MarkdownDocumentationWithoutABuilder_ShowsTheMarkdownAsWrappingText()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

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

					var content = GetCompletionTooltip(completionWindow).Content as TextBlock;

					Assert.IsNotNull(content);
					Assert.AreEqual(FormatMarkdown, content.Text);
					Assert.AreEqual(TextWrapping.Wrap, content.TextWrapping);
					Assert.AreEqual(FormatMarkdown, hosted.Controller.CurrentPresentation.TooltipContent!.Value.Content);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_BuilderReturningNull_ShowsTheMarkdownAsWrappingText()
	{
		var builder = new RecordingMarkdownBuilder { Build = _ => null };

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

					Assert.AreEqual(FormatMarkdown, (GetCompletionTooltip(completionWindow).Content as TextBlock)?.Text);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_ThrowingBuilder_LogsTheFailureAndShowsTheMarkdownAsWrappingText()
	{
		var logger = new RecordingLogger();
		var builder = new RecordingMarkdownBuilder { Build = _ => throw new InvalidOperationException("The Markdown renderer failed.") };

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(hooks: builder.Hooks(), logger: logger);

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

					Assert.AreEqual(FormatMarkdown, (GetCompletionTooltip(completionWindow).Content as TextBlock)?.Text);
					Assert.IsNotEmpty(logger.Entries);
					Assert.IsTrue(logger.Entries.All(entry => entry.EventId.Id == 1001));
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_SelectingAPlainItemAfterAMarkdownOne_ReplacesTheRenderedElement()
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
							[MarkdownItem("format", FormatMarkdown), CreateItem("print", "Prints its arguments.")],
							TextRange.FromBounds(0, 5)
						)
					);

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.SelectItem("format");
					PumpPastTooltipDebounce(hosted.Completion);
					completionWindow.CompletionList.SelectItem("print");
					PumpPastTooltipDebounce(hosted.Completion);

					var content = GetCompletionTooltip(completionWindow).Content as TextBlock;

					Assert.IsNotNull(content);
					Assert.AreEqual("Prints its arguments.", content.Text);
					Assert.AreEqual("Prints its arguments.", hosted.Controller.CurrentPresentation.TooltipContent!.Value.Content);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_HostItemWithMarkdownLookingText_NeverReachesTheMarkdownBuilder()
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
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample", FormatMarkdown)], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.SelectItem("sample");
					PumpPastTooltipDebounce(hosted.Completion);

					// A host item supplies its own description, so only the default adapter's documentation has a kind.
					Assert.IsEmpty(builder.Inputs);
					Assert.AreEqual(FormatMarkdown, (GetCompletionTooltip(completionWindow).Content as TextBlock)?.Text);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_ResolvedMarkdownContent_IsBuiltByItsKind()
	{
		var builder = new RecordingMarkdownBuilder();

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(
				hooks: builder.Hooks(
					resolveDescriptionAsync: (_, _) => Task.FromResult<object?>(new MarkupContent(FormatMarkdown, MarkupKind.Markdown))
				)
			);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					// The item has no documentation until the host's resolver supplies it, as an item of a language server does before it is resolved.
					Assert.IsTrue(
						hosted.Completion.OpenOrRefresh([new CompletionItemAdapter(new CompletionItem("format"))], TextRange.FromBounds(0, 5))
					);

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
	public void Tooltip_ResolvedPlainTextContent_ShowsItsTextWithoutTheBuilder()
	{
		var builder = new RecordingMarkdownBuilder();

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(
				hooks: builder.Hooks(resolveDescriptionAsync: (_, _) => Task.FromResult<object?>(new MarkupContent("Resolved text.")))
			);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(
						hosted.Completion.OpenOrRefresh([new CompletionItemAdapter(new CompletionItem("format"))], TextRange.FromBounds(0, 5))
					);

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					completionWindow.CompletionList.SelectItem("format");
					PumpPastTooltipDebounce(hosted.Completion);
					DispatcherTestHelpers.PumpUntil(() => GetCompletionTooltip(completionWindow).Content is TextBlock);

					Assert.AreEqual("Resolved text.", (GetCompletionTooltip(completionWindow).Content as TextBlock)?.Text);
					Assert.IsEmpty(builder.Inputs);
				}
			}
		}
	}

	private static CompletionItemAdapter MarkdownItem(string label, string markdown) =>
		new(new CompletionItem(label) { Documentation = new MarkupContent(markdown, MarkupKind.Markdown) });

	// Builds a marker element for each Markdown text and records what it was given and what it returned.
	private sealed class RecordingMarkdownBuilder
	{
		internal List<string> Inputs { get; } = [];

		internal List<UIElement> Built { get; } = [];

		internal Func<string, UIElement?> Build { get; init; } = static markdown => new Border { Tag = markdown };

		internal CompletionPresenterHooks Hooks(Func<ICompletionData, CancellationToken, Task<object?>>? resolveDescriptionAsync = null) =>
			new() { BuildMarkdownContent = Record, ResolveDescriptionAsync = resolveDescriptionAsync };

		private UIElement? Record(string markdown)
		{
			Inputs.Add(markdown);

			UIElement? built = Build(markdown);

			if (built is not null)
				Built.Add(built);

			return built;
		}
	}
}
