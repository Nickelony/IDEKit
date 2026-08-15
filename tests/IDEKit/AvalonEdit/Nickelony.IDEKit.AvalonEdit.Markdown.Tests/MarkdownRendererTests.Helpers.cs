using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using AvalonTextEditor = ICSharpCode.AvalonEdit.TextEditor;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

public sealed partial class MarkdownRendererTests
{
	private static IEnumerable<T> FindAll<T>(FlowDocument document) where T : DependencyObject
	{
		foreach (Block block in document.Blocks)
		{
			foreach (DependencyObject element in EnumerateBlock(block))
			{
				if (element is T match)
					yield return match;
			}
		}
	}

	private static string GetParagraphText(Paragraph paragraph)
		=> GetInlineText(paragraph.Inlines);

	private static string GetHyperlinkText(Hyperlink hyperlink)
		=> GetInlineText(hyperlink.Inlines);

	private static string GetInlineText(InlineCollection inlines)
	{
		var builder = new StringBuilder();

		foreach (Inline inline in inlines)
			AppendInlineText(builder, inline);

		return builder.ToString();
	}

	private static void AppendInlineText(StringBuilder builder, Inline inline)
	{
		switch (inline)
		{
			case Run run:
				builder.Append(run.Text);
				break;

			case LineBreak:
				builder.Append('\n');
				break;

			case InlineUIContainer { Child: TextBlock textBlock }:
				builder.Append(textBlock.Text);
				break;

			case Span span:
				foreach (Inline child in span.Inlines)
					AppendInlineText(builder, child);

				break;
		}
	}

	private static IEnumerable<DependencyObject> EnumerateBlock(Block block)
	{
		yield return block;

		switch (block)
		{
			case Paragraph paragraph:
				foreach (Inline inline in paragraph.Inlines)
				{
					foreach (DependencyObject element in EnumerateInline(inline))
					{
						yield return element;
					}
				}
				break;

			case Section section:
				foreach (Block child in section.Blocks)
				{
					foreach (DependencyObject element in EnumerateBlock(child))
					{
						yield return element;
					}
				}
				break;

			case List list:
				foreach (ListItem item in list.ListItems)
				{
					yield return item;

					foreach (Block child in item.Blocks)
					{
						foreach (DependencyObject element in EnumerateBlock(child))
						{
							yield return element;
						}
					}
				}
				break;

			case Table table:
				foreach (TableRowGroup rowGroup in table.RowGroups)
				{
					yield return rowGroup;

					foreach (TableRow row in rowGroup.Rows)
					{
						yield return row;

						foreach (TableCell cell in row.Cells)
						{
							yield return cell;

							foreach (Block child in cell.Blocks)
							{
								foreach (DependencyObject element in EnumerateBlock(child))
								{
									yield return element;
								}
							}
						}
					}
				}
				break;
		}
	}

	private static IEnumerable<DependencyObject> EnumerateInline(Inline inline)
	{
		yield return inline;

		if (inline is Span span)
		{
			foreach (Inline child in span.Inlines)
			{
				foreach (DependencyObject element in EnumerateInline(child))
				{
					yield return element;
				}
			}
		}
	}

	private static Paragraph GetFirstCellParagraph(TableCell cell)
		=> (Paragraph)cell.Blocks.FirstBlock!;

	private static Border GetInlineCodeBorder(FlowDocument document)
		=> (Border)FindAll<InlineUIContainer>(document).Single().Child;

	private static Border GetCodeBlockBorder(FlowDocument document)
		=> (Border)FindAll<BlockUIContainer>(document).Single().Child;

	private static AvalonTextEditor GetCodeBlockEditor(FlowDocument document)
		=> (AvalonTextEditor)GetCodeBlockBorder(document).Child;

	private static string CreateScrollableTooltipBody()
		=> string.Join("\n\n", Enumerable.Repeat("Some tooltip paragraph text that wraps over several lines.", 8));

	private static (HostWindow Window, FlowDocumentScrollViewer Viewer, AvalonTextEditor Editor, ScrollViewer OuterScrollViewer) CreateScrollableTooltipWithCodeBlock()
	{
		string body = CreateScrollableTooltipBody();
		string code = string.Join("\n", Enumerable.Range(1, 40).Select(index => $"local value{index} = {index}"));
		var theme = MarkdownRenderTheme.Default with { MaxHeight = 150.0, MaxVisibleCodeBlockLines = 5 };

		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent($"{body}\n\n```lua\n{code}\n```", theme);
		HostWindow window = WPFTestHost.ShowInHostWindow(viewer);

		viewer.UpdateLayout();
		WPFTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

		AvalonTextEditor editor = WPFTestHost.FindVisualDescendants<AvalonTextEditor>(viewer).First();
		ScrollViewer outerScrollViewer = WPFTestHost.FindVisualDescendants<ScrollViewer>(viewer).First(scrollViewer => scrollViewer.IsAncestorOf(editor));

		return (window, viewer, editor, outerScrollViewer);
	}

	/// <summary>
	/// Hosts a standalone code-block editor (one created directly, without the rendered viewer) inside a
	/// scrollable host so the editor's own wheel routing can be observed.
	/// </summary>
	/// <returns>The host window, the standalone editor, and the enclosing host scroller.</returns>
	private static (HostWindow Window, AvalonTextEditor Editor, ScrollViewer HostScrollViewer) CreateStandaloneCodeBlockEditorInHostScroller()
	{
		string code = string.Join("\n", Enumerable.Range(1, 40).Select(index => $"local value{index} = {index}"));
		AvalonTextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("lua", code);
		editor.Height = 120.0;

		var filler = new Border { Height = 400.0 };
		var panel = new StackPanel();
		panel.Children.Add(editor);
		panel.Children.Add(filler);

		var hostScrollViewer = new ScrollViewer
		{
			Content = panel,
			Height = 200.0,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
		};

		HostWindow window = WPFTestHost.ShowInHostWindow(hostScrollViewer);

		hostScrollViewer.UpdateLayout();
		WPFTestHost.PumpDispatcher(hostScrollViewer.Dispatcher, DispatcherPriority.Background);

		return (window, editor, hostScrollViewer);
	}

	private static HostWindow ShowContentInsideHostScroller(FrameworkElement content, out ScrollViewer outerScrollViewer)
	{
		ArgumentNullException.ThrowIfNull(content);

		var filler = new Border { Height = 400.0 };
		var panel = new StackPanel();

		panel.Children.Add(content);
		panel.Children.Add(filler);

		outerScrollViewer = new ScrollViewer
		{
			Content = panel,
			Height = 200.0,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
		};

		HostWindow window = WPFTestHost.ShowInHostWindow(outerScrollViewer);

		content.UpdateLayout();
		WPFTestHost.PumpDispatcher(content.Dispatcher, DispatcherPriority.Background);

		return window;
	}

	private static void AssumeWheelScrollingEnabled()
	{
		if (SystemParameters.WheelScrollLines == 0)
			Assert.Inconclusive("The test requires wheel scrolling, which this machine has disabled.");
	}

	private static void RaiseMouseWheel(UIElement source, int delta)
	{
		// Mirrors the WPF input system: the preview event is raised first, and the bubbling event is
		// raised only when no handler consumed the preview event.
		var previewEvent = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
		{
			RoutedEvent = UIElement.PreviewMouseWheelEvent
		};

		source.RaiseEvent(previewEvent);

		if (previewEvent.Handled)
			return;

		var bubbleEvent = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
		{
			RoutedEvent = UIElement.MouseWheelEvent
		};

		source.RaiseEvent(bubbleEvent);
	}

	private static bool RaisePreviewMouseLeftButtonDown(UIElement element)
	{
		var mouseDown = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
		{
			RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent
		};

		element.RaiseEvent(mouseDown);
		return mouseDown.Handled;
	}
}
