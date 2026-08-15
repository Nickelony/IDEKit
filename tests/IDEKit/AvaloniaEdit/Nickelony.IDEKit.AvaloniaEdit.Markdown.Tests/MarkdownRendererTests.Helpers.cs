using Avalonia.Controls;
using Avalonia.Controls.Documents;
using AvaloniaEdit;
using System.Text;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;

public sealed partial class MarkdownRendererTests
{
	private static StackPanel GetBlockPanel(Control content)
		=> (StackPanel)((ScrollViewer)content).Content!;

	private static TextBlock SingleParagraphBlock(Control content)
		=> GetBlockPanel(content).Children.OfType<TextBlock>().Single();

	private static string GetText(InlineCollection? inlines)
	{
		var builder = new StringBuilder();

		AppendText(builder, inlines);
		return builder.ToString();
	}

	private static void AppendText(StringBuilder builder, InlineCollection? inlines)
	{
		if (inlines is null)
			return;

		foreach (Inline inline in inlines)
		{
			switch (inline)
			{
				case Run run:
					builder.Append(run.Text);
					break;

				case LineBreak:
					builder.Append('\n');
					break;

				case InlineUIContainer { Child: TextBlock hostedBlock }:
					AppendText(builder, hostedBlock.Inlines);
					break;

				case Span span:
					AppendText(builder, span.Inlines);
					break;
			}
		}
	}

	/// <summary>
	/// Enumerates every inline in the content tree, descending through the viewer, block panels, borders,
	/// grids, and inline hosts so a scenario can assert on emphasis, code spans, and links wherever they appear.
	/// </summary>
	/// <param name="control">The control to search under.</param>
	/// <returns>The inlines, in tree order.</returns>
	private static IEnumerable<Inline> FindInlines(Control control)
		=> FindControls(control).OfType<TextBlock>().SelectMany(textBlock => EnumerateInlines(textBlock.Inlines));

	/// <summary>
	/// Enumerates the controls in the content tree, descending through the viewer, block panels, borders,
	/// and grids.
	/// </summary>
	/// <param name="control">The control to search under.</param>
	/// <returns>The controls, in tree order.</returns>
	private static IEnumerable<Control> FindControls(Control control)
	{
		yield return control;

		switch (control)
		{
			case ScrollViewer { Content: Control scrollContent }:
				foreach (Control child in FindControls(scrollContent))
					yield return child;
				break;

			case StackPanel panel:
				foreach (Control child in panel.Children)
				{
					foreach (Control descendant in FindControls(child))
						yield return descendant;
				}
				break;

			case Border { Child: Control borderChild }:
				foreach (Control child in FindControls(borderChild))
					yield return child;
				break;

			case Grid grid:
				foreach (Control child in grid.Children.OfType<Control>())
				{
					foreach (Control descendant in FindControls(child))
						yield return descendant;
				}
				break;
		}
	}

	private static IEnumerable<Inline> EnumerateInlines(InlineCollection? inlines)
	{
		if (inlines is null)
			yield break;

		foreach (Inline inline in inlines)
		{
			yield return inline;

			if (inline is Span span)
			{
				foreach (Inline child in EnumerateInlines(span.Inlines))
					yield return child;
			}

			if (inline is InlineUIContainer { Child: TextBlock hostedBlock })
			{
				foreach (Inline child in EnumerateInlines(hostedBlock.Inlines))
					yield return child;
			}
		}
	}

	private static Border GetCodeBlockBorder(Control content)
		=> FindControls(content).OfType<Border>().Single(border => border.Child is TextEditor);

	private static TextEditor GetCodeBlockEditor(Control content)
		=> (TextEditor)GetCodeBlockBorder(content).Child!;

	private static Border GetInlineCodeBorder(Control content)
		=> FindInlines(content)
			.OfType<InlineUIContainer>()
			.Select(container => container.Child)
			.OfType<Border>()
			.Single();

	private static MarkdownLinkElement GetLinkElement(Control content)
		=> FindInlines(content)
			.OfType<InlineUIContainer>()
			.Select(container => container.Child)
			.OfType<MarkdownLinkElement>()
			.Single();

	private static string CreateScrollableBody()
		=> string.Join("\n\n", Enumerable.Repeat("Some tooltip paragraph text that wraps over several lines.", 8));
}
