using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Media;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;

/// <summary>
/// The AvaloniaEdit binding of the shared <see cref="MarkdownRendererTestsBase"/> scenarios; the shared scenarios read the control tree
/// through the hooks in <c>MarkdownRendererTests.Probes.cs</c>, and this suite holds the scenarios that only the control emitter has.
/// </summary>
[AvaloniaTestClass]
public sealed partial class MarkdownRendererTests : MarkdownRendererTestsBase
{
	[TestMethod]
	public void CreateContent_PlainText_ProducesWrappingTextBlock()
	{
		Control element = MarkdownRenderer.CreateContent("plain text");
		TextBlock textBlock = SingleParagraphBlock(element);

		Assert.AreEqual("plain text", GetText(textBlock.Inlines));
		Assert.AreEqual(TextWrapping.Wrap, textBlock.TextWrapping);
	}

	[TestMethod]
	public void CreateContent_InlineCode_BoundsTheTextByTheCodeMaxWidth()
	{
		Border border = GetInlineCodeBorder(MarkdownRenderer.CreateContent("Use `TextEditor` here."));

		var textBlock = (TextBlock)border.Child!;

		Assert.IsLessThanOrEqualTo(MarkdownRenderTheme.Default.CodeMaxWidth, textBlock.MaxWidth);
	}

	[TestMethod]
	public void CreateContent_LongLink_WrapsWithinTheParagraphWidth()
	{
		string label = new('w', 240);
		Control element = MarkdownRenderer.CreateContent($"[{label}](https://example.com)");
		MarkdownLinkElement link = GetLinkElement(element);
		TextBlock paragraph = SingleParagraphBlock(element);

		// The link is an InlineUIContainer host. A long label must wrap within the paragraph instead of
		// overflowing the render width and clipping, because horizontal scrolling is disabled. The measured
		// width is asserted (not the arranged bounds): an unconstrained measure would keep the full label
		// width, while a wrapping measure is clamped to the paragraph's available width.
		using HostWindow window = AvaloniaTestHost.ShowInHostWindow(element);
		element.UpdateLayout();

		Assert.IsLessThanOrEqualTo(
			paragraph.Bounds.Width + 1.0,
			link.DesiredSize.Width,
			$"The link measured {link.DesiredSize.Width} against a paragraph width of {paragraph.Bounds.Width}."
		);
	}

	[TestMethod]
	public void CreateContent_LinkActivation_OnlyTheLeftButtonOpens()
	{
		Uri? openedUri = null;

		var options = new MarkdownRenderOptions
		{
			OpenHyperlink = uri =>
			{
				openedUri = uri;
				return true;
			},
		};

		MarkdownLinkElement link = GetLinkElement(MarkdownRenderer.CreateContent("[docs](https://example.com)", null, options));

		// A right-button press must neither open the link nor mark the event handled, so the host's context
		// gesture still runs.
		PointerPressedEventArgs rightPress = RaisePointerPressed(link, PointerUpdateKind.RightButtonPressed);

		Assert.IsNull(openedUri);
		Assert.IsFalse(rightPress.Handled);

		PointerPressedEventArgs leftPress = RaisePointerPressed(link, PointerUpdateKind.LeftButtonPressed);

		Assert.AreEqual("https://example.com/", openedUri?.AbsoluteUri);
		Assert.IsTrue(leftPress.Handled);
	}

	[TestMethod]
	public void CreateContent_LinkActivation_EnterOrSpaceOpensTheLink()
	{
		Uri? openedUri = null;

		var options = new MarkdownRenderOptions
		{
			OpenHyperlink = uri =>
			{
				openedUri = uri;
				return true;
			},
		};

		MarkdownLinkElement enterLink = GetLinkElement(MarkdownRenderer.CreateContent("[docs](https://example.com)", null, options));

		KeyEventArgs enterPress = RaiseKeyDown(enterLink, Key.Enter);

		Assert.AreEqual("https://example.com/", openedUri?.AbsoluteUri);
		Assert.IsTrue(enterPress.Handled);

		openedUri = null;

		MarkdownLinkElement spaceLink = GetLinkElement(MarkdownRenderer.CreateContent("[docs](https://example.com)", null, options));

		KeyEventArgs spacePress = RaiseKeyDown(spaceLink, Key.Space);

		Assert.AreEqual("https://example.com/", openedUri?.AbsoluteUri);
		Assert.IsTrue(spacePress.Handled);
	}

	[TestMethod]
	public void CreateContent_LinkActivation_OtherKeyDoesNotOpenTheLink()
	{
		Uri? openedUri = null;

		var options = new MarkdownRenderOptions
		{
			OpenHyperlink = uri =>
			{
				openedUri = uri;
				return true;
			},
		};

		MarkdownLinkElement link = GetLinkElement(MarkdownRenderer.CreateContent("[docs](https://example.com)", null, options));

		// Only Enter and Space activate a link; another key must neither open it nor mark the event handled.
		KeyEventArgs press = RaiseKeyDown(link, Key.A);

		Assert.IsNull(openedUri);
		Assert.IsFalse(press.Handled);
	}

	[TestMethod]
	public void CreateContent_LinkActivation_ConsumedKeyDoesNotOpenTheLink()
	{
		Uri? openedUri = null;

		var options = new MarkdownRenderOptions
		{
			OpenHyperlink = uri =>
			{
				openedUri = uri;
				return true;
			},
		};

		MarkdownLinkElement link = GetLinkElement(MarkdownRenderer.CreateContent("[docs](https://example.com)", null, options));

		// A host that consumed the key must keep the handling, so the link does not open on Enter.
		KeyEventArgs press = RaiseKeyDown(link, Key.Enter, handled: true);

		Assert.IsNull(openedUri);
		Assert.IsTrue(press.Handled);
	}

	[TestMethod]
	public void CreateContent_ListItemMarker_UsesTheThemeBodyFontAndForeground()
	{
		var theme = new MarkdownRenderTheme { BodyFontSize = 21.0 };
		Control listContent = MarkdownRenderer.CreateContent("- one\n- two", theme);
		var markers = GetListMarkerBlocks(listContent).ToList();
		TextBlock body = SingleParagraphBlock(MarkdownRenderer.CreateContent("one", theme));

		// No ancestor supplies the body font or foreground, so every marker carries the theme's own values.
		Assert.HasCount(2, markers);

		foreach (TextBlock marker in markers)
		{
			Assert.AreEqual("\u2022", marker.Text);
			Assert.AreEqual(theme.BodyFontSize, marker.FontSize);
			Assert.AreEqual(body.FontFamily.Name, marker.FontFamily.Name);
			Assert.AreEqual(body.Foreground, marker.Foreground);
		}
	}

	[TestMethod]
	public void CreateContent_NestedEmphasis_RunsTakeTheThemeForeground()
	{
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with { Foreground = new ArgbColor(A: 0xFF, R: 0xCC, G: 0xCC, B: 0xCC) };
		Control element = MarkdownRenderer.CreateContent("**bold _both_** and _italic_", theme);

		var host = new Window { Content = element };
		host.Show();

		try
		{
			// A run inside a span inherits the span's foreground, so a dark theme never shows the toolkit's black default.
			foreach (Run run in FindInlines(element).OfType<Run>())
				Assert.AreEqual(Color.FromRgb(0xCC, 0xCC, 0xCC), ((ISolidColorBrush)run.Foreground!).Color, run.Text);
		}
		finally
		{
			host.Close();
		}
	}

	[TestMethod]
	public void CreateContent_InlineCode_AlignsWithTheBottomOfTheText()
	{
		Control element = MarkdownRenderer.CreateContent("Use `TextEditor` here.");

		InlineUIContainer code = FindInlines(element).OfType<InlineUIContainer>().Single();

		Assert.AreEqual(BaselineAlignment.TextBottom, code.BaselineAlignment);
	}

	[TestMethod]
	public void CreateContent_Table_WrapsALongCellWithinTheRenderWidth()
	{
		string longCell = string.Join(' ', Enumerable.Repeat("word", 120));
		Control element = MarkdownRenderer.CreateContent($"| name | text |\n|--|--|\n| a | {longCell} |");
		TextBlock cell = GetCellText((Grid)GetBlockPanel(element).Children.Single(), 3);

		// Horizontal scrolling is disabled, so a cell wider than the render width would clip. The grid itself is clamped to the window, and the
		// cell is not, so the cell's width tells.
		using HostWindow window = AvaloniaTestHost.ShowInHostWindow(element);
		element.UpdateLayout();

		Assert.IsLessThanOrEqualTo(
			window.Bounds.Width,
			cell.Bounds.Width,
			$"The cell is {cell.Bounds.Width} wide in a window {window.Bounds.Width} wide."
		);
	}

	[TestMethod]
	public void CreateContentTree_ReturnsTheBlockPanelWithoutViewerChrome()
	{
		Control tree = MarkdownRenderer.CreateContentTree("plain text");
		Assert.IsInstanceOfType<StackPanel>(tree);
	}

	[TestMethod]
	public void CreateContentTree_HighlightingHookThrows_ReturnsPlainTextPanelAndLogsWarning()
	{
		var logger = new RecordingLogger();
		var options = new MarkdownRenderOptions { Logger = logger };

		Control tree = MarkdownRenderer.CreateContentTree(
			content: "```lua\nx = 1\n```",
			theme: MarkdownRenderTheme.Default,
			options,
			customHighlightingInstaller: (_, _) => throw new InvalidOperationException("highlighting failure")
		);

		TextBlock text = ((StackPanel)tree).Children.OfType<TextBlock>().Single();
		Assert.Contains("x = 1", text.Text!);

		RecordingLogger.LogEntry entry = logger.Entries.Single();

		Assert.AreEqual(2000, entry.EventId.Id);
		Assert.AreEqual(LogLevel.Warning, entry.Level);
		Assert.IsTrue(entry.Message.Contains("Markdown rendering failed", StringComparison.Ordinal));
	}
}
