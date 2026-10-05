using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using EditorControl = ICSharpCode.AvalonEdit.TextEditor;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

public sealed partial class MarkdownRendererTests
{
	private static IEnumerable<T> FindAll<T>(FlowDocument document)
		where T : DependencyObject
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

	private static string GetParagraphText(Paragraph paragraph) => GetInlineText(paragraph.Inlines);

	private static string GetHyperlinkText(Hyperlink hyperlink) => GetInlineText(hyperlink.Inlines);

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
						yield return element;
				}

				break;

			case Section section:
				foreach (Block child in section.Blocks)
				{
					foreach (DependencyObject element in EnumerateBlock(child))
						yield return element;
				}

				break;

			case List list:
				foreach (ListItem item in list.ListItems)
				{
					yield return item;

					foreach (Block child in item.Blocks)
					{
						foreach (DependencyObject element in EnumerateBlock(child))
							yield return element;
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
									yield return element;
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
					yield return element;
			}
		}
	}

	private static Paragraph GetFirstCellParagraph(TableCell cell) => (Paragraph)cell.Blocks.FirstBlock!;

	private static Border GetInlineCodeBorder(FlowDocument document) => (Border)FindAll<InlineUIContainer>(document).Single().Child;

	private static Border GetCodeBlockBorder(FlowDocument document) => (Border)FindAll<BlockUIContainer>(document).Single().Child;

	private static (
		HostWindow Window,
		FlowDocumentScrollViewer Viewer,
		EditorControl Editor,
		ScrollViewer OuterScrollViewer
	) CreateScrollableTooltipWithCodeBlock()
	{
		var viewer = (FlowDocumentScrollViewer)CreateScrollableCodeBlockViewer();
		HostWindow window = WpfTestHost.ShowInHostWindow(viewer);

		viewer.UpdateLayout();
		WpfTestHost.PumpDispatcher(viewer.Dispatcher, DispatcherPriority.Background);

		EditorControl editor = WpfTestHost.FindVisualDescendants<EditorControl>(viewer).First();

		ScrollViewer outerScrollViewer = WpfTestHost
			.FindVisualDescendants<ScrollViewer>(viewer)
			.First(scrollViewer => scrollViewer.IsAncestorOf(editor));

		return (window, viewer, editor, outerScrollViewer);
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
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
		};

		HostWindow window = WpfTestHost.ShowInHostWindow(outerScrollViewer);

		content.UpdateLayout();
		WpfTestHost.PumpDispatcher(content.Dispatcher, DispatcherPriority.Background);

		return window;
	}

	protected override void AssumeWheelScrollingEnabled()
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
			RoutedEvent = UIElement.PreviewMouseWheelEvent,
		};

		source.RaiseEvent(previewEvent);

		if (previewEvent.Handled)
			return;

		var bubbleEvent = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = UIElement.MouseWheelEvent };

		source.RaiseEvent(bubbleEvent);
	}

	private static bool RaisePreviewMouseLeftButtonDown(UIElement element)
	{
		var mouseDown = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
		{
			RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
		};

		element.RaiseEvent(mouseDown);
		return mouseDown.Handled;
	}
}
