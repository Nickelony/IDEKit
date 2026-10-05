using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;

public sealed partial class MarkdownRendererTests
{
	private static StackPanel GetBlockPanel(Control content) => (StackPanel)((ScrollViewer)content).Content!;

	private static TextBlock SingleParagraphBlock(Control content) => GetBlockPanel(content).Children.OfType<TextBlock>().Single();

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
	/// Enumerates every inline in the content tree, descending through the viewer, block panels, borders, grids, and inline hosts so a scenario
	/// can assert on emphasis, code spans, and links wherever they appear.
	/// </summary>
	/// <param name="control">The control to search under.</param>
	/// <returns>The inlines, in tree order.</returns>
	private static IEnumerable<Inline> FindInlines(Control control) =>
		FindControls(control).OfType<TextBlock>().SelectMany(textBlock => EnumerateInlines(textBlock.Inlines));

	/// <summary>Enumerates the controls in the content tree, descending through the viewer, block panels, borders, and grids.</summary>
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

	private static Border GetCodeBlockBorder(Control content) => FindControls(content).OfType<Border>().Single(border => border.Child is TextEditor);

	private static Border GetInlineCodeBorder(Control content) =>
		FindInlines(content).OfType<InlineUIContainer>().Select(container => container.Child).OfType<Border>().Single();

	private static MarkdownLinkElement GetLinkElement(Control content) =>
		FindInlines(content).OfType<InlineUIContainer>().Select(container => container.Child).OfType<MarkdownLinkElement>().Single();

	// Raises a pointer press on the element with the supplied pointer kind, exercising a link element's
	// routed press handler with a deterministic button state.
	private static PointerPressedEventArgs RaisePointerPressed(Interactive element, PointerUpdateKind pointerUpdateKind)
	{
		RawInputModifiers modifiers =
			pointerUpdateKind == PointerUpdateKind.LeftButtonPressed ? RawInputModifiers.LeftMouseButton : RawInputModifiers.RightMouseButton;

		var eventArgs = new PointerPressedEventArgs(
			source: element,
			pointer: new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true),
			rootVisual: element,
			rootVisualPosition: default,
			timestamp: 0UL,
			properties: new PointerPointProperties(modifiers, pointerUpdateKind),
			modifiers: KeyModifiers.None
		);

		element.RaiseEvent(eventArgs);

		return eventArgs;
	}

	// Raises a key-down on the element with the supplied key, exercising a link element's routed key handler
	// with a deterministic key state (and, when requested, an already-consumed event).
	private static KeyEventArgs RaiseKeyDown(Interactive element, Key key, bool handled = false)
	{
		var eventArgs = new KeyEventArgs
		{
			RoutedEvent = InputElement.KeyDownEvent,
			Key = key,
			Handled = handled,
		};

		element.RaiseEvent(eventArgs);

		return eventArgs;
	}
}
