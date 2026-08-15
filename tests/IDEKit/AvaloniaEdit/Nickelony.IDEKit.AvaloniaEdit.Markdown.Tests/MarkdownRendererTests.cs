using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using Microsoft.Extensions.Logging;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;

[AvaloniaTestClass]
public sealed partial class MarkdownRendererTests
{
	[TestMethod]
	public void CreateContent_WhitespaceOnlyContent_ReturnsFallbackScrollViewer()
	{
		Control element = MarkdownRenderer.CreateContent("   \n  ");

		Assert.IsInstanceOfType<ScrollViewer>(element);
		Assert.IsInstanceOfType<TextBlock>(((ScrollViewer)element).Content);
	}

	[TestMethod]
	public void CreateContent_PlainText_ProducesWrappingTextBlock()
	{
		Control element = MarkdownRenderer.CreateContent("plain text");
		TextBlock textBlock = SingleParagraphBlock(element);

		Assert.AreEqual("plain text", GetText(textBlock.Inlines));
		Assert.AreEqual(TextWrapping.Wrap, textBlock.TextWrapping);
	}

	[TestMethod]
	public void CreateContent_Heading_ScalesFontSizeAndIsBold()
	{
		TextBlock heading = SingleParagraphBlock(MarkdownRenderer.CreateContent("# Heading"));

		Assert.IsTrue(heading.FontSize > MarkdownRenderTheme.Default.BodyFontSize);
		Assert.AreEqual(FontWeight.Bold, heading.FontWeight);
	}

	[TestMethod]
	public void CreateContent_EmptyHeadingFontSizeScaleList_UsesBodyFontSize()
	{
		var theme = new MarkdownRenderTheme { HeadingFontSizeScales = [] };
		TextBlock heading = SingleParagraphBlock(MarkdownRenderer.CreateContent("# Heading", theme));

		Assert.AreEqual(MarkdownRenderTheme.Default.BodyFontSize, heading.FontSize);
		Assert.AreEqual(FontWeight.Bold, heading.FontWeight);
	}

	[TestMethod]
	public void CreateContent_Emphasis_ProducesBoldAndItalic()
	{
		Control element = MarkdownRenderer.CreateContent("**bold** and *italic*");
		List<Inline> inlines = FindInlines(element).ToList();

		Assert.AreEqual(1, inlines.OfType<Bold>().Count());
		Assert.AreEqual(1, inlines.OfType<Italic>().Count());
	}

	[TestMethod]
	public void CreateContent_Strikethrough_ProducesStrikethroughDecoration()
	{
		Control element = MarkdownRenderer.CreateContent("~~gone~~");
		var span = FindInlines(element).OfType<Span>().Single();

		Assert.IsTrue(span.TextDecorations is { Count: > 0 });
		Assert.AreEqual(TextDecorationLocation.Strikethrough, span.TextDecorations![0].Location);
	}

	[TestMethod]
	public void CreateContent_SoftLineBreak_RendersAsSpace()
	{
		TextBlock textBlock = SingleParagraphBlock(MarkdownRenderer.CreateContent("alpha\nbeta"));

		Assert.AreEqual("alpha beta", GetText(textBlock.Inlines));
		Assert.AreEqual(0, FindInlines(textBlock).OfType<LineBreak>().Count());
	}

	[TestMethod]
	public void CreateContent_HardLineBreak_ProducesLineBreak()
	{
		TextBlock textBlock = SingleParagraphBlock(MarkdownRenderer.CreateContent("alpha  \nbeta"));

		Assert.AreEqual(1, FindInlines(textBlock).OfType<LineBreak>().Count());
		Assert.AreEqual("alpha\nbeta", GetText(textBlock.Inlines));
	}

	[TestMethod]
	public void CreateContent_InlineCode_ProducesBorderedCodeSurface()
	{
		Control element = MarkdownRenderer.CreateContent("Use `TextEditor` here.");
		Border border = GetInlineCodeBorder(element);
		var textBlock = (TextBlock)border.Child!;

		Assert.AreEqual("TextEditor", textBlock.Text);
		Assert.AreEqual(MarkdownRenderTheme.Default.CodeFontFamily, textBlock.FontFamily);
		Assert.AreEqual(TextWrapping.Wrap, textBlock.TextWrapping);
		Assert.IsTrue(textBlock.MaxWidth <= MarkdownRenderTheme.Default.CodeMaxWidth);
	}

	[TestMethod]
	public void CreateContent_HttpsLink_ProducesOpenableLinkThatStaysOutOfTheTabOrder()
	{
		MarkdownLinkElement link = GetLinkElement(MarkdownRenderer.CreateContent("[docs](https://example.com)"));

		Assert.AreEqual("https://example.com/", link.OpenableUri?.AbsoluteUri);
		Assert.AreEqual("docs", GetText(link.Inlines));
		Assert.IsFalse(link.Focusable);
	}

	[TestMethod]
	public void CreateContent_UnsupportedSchemeLink_HasNoOpenableUri()
	{
		MarkdownLinkElement link = GetLinkElement(MarkdownRenderer.CreateContent("[x](javascript:alert(1))"));

		Assert.IsNull(link.OpenableUri);
		Assert.IsFalse(link.Focusable);
	}

	[TestMethod]
	public void CreateContent_ContentInteractionEnabled_MakesOpenableLinkFocusable()
	{
		var options = new MarkdownRenderOptions { AllowContentInteraction = true };
		MarkdownLinkElement link = GetLinkElement(MarkdownRenderer.CreateContent("[docs](https://example.com)", null, options));

		Assert.IsTrue(link.Focusable);
	}

	[TestMethod]
	public void CreateContent_ContentInteractionEnabled_UnsupportedSchemeLinkStaysUnfocusable()
	{
		var options = new MarkdownRenderOptions { AllowContentInteraction = true };
		MarkdownLinkElement link = GetLinkElement(MarkdownRenderer.CreateContent("[x](javascript:alert(1))", null, options));

		// A link the renderer cannot open must not become a dead keyboard stop.
		Assert.IsFalse(link.Focusable);
	}

	[TestMethod]
	public void CreateContent_LinkActivation_InvokesConfiguredOpener()
	{
		Uri? openedUri = null;
		var options = new MarkdownRenderOptions
		{
			OpenHyperlink = uri =>
			{
				openedUri = uri;
				return true;
			}
		};

		MarkdownLinkElement link = GetLinkElement(MarkdownRenderer.CreateContent("[docs](https://example.com)", null, options));

		link.Open!(link.OpenableUri!);

		Assert.AreEqual("https://example.com/", openedUri?.AbsoluteUri);
	}

	[TestMethod]
	public void CreateContent_OpenHyperlinkThrows_LogsWarningAndUsesExternalOpener()
	{
		var logger = new CapturingLogger();
		bool externalOpenerCalled = false;

		var options = new MarkdownRenderOptions
		{
			OpenHyperlink = _ => throw new InvalidOperationException("opener failure"),
			OpenExternalUri = _ =>
			{
				externalOpenerCalled = true;
				return true;
			},
			Logger = logger
		};

		MarkdownLinkElement link = GetLinkElement(MarkdownRenderer.CreateContent("[docs](https://example.com)", null, options));

		link.Open!(link.OpenableUri!);

		Assert.IsTrue(externalOpenerCalled);

		CapturingLogger.LogEntry entry = logger.Entries.Single();

		Assert.AreEqual(2001, entry.EventId.Id);
		Assert.AreEqual(LogLevel.Warning, entry.Level);
	}

	[TestMethod]
	public void CreateContent_EmailAutolinkWithoutMailtoScheme_HasNoOpenableUri()
	{
		MarkdownLinkElement link = GetLinkElement(MarkdownRenderer.CreateContent("Write to <user@example.com> today."));

		Assert.IsNull(link.OpenableUri);
		Assert.AreEqual("user@example.com", GetText(link.Inlines));
	}

	[TestMethod]
	public void CreateContent_Image_RendersAltTextWithoutAnImageControl()
	{
		Control element = MarkdownRenderer.CreateContent("![alt text](https://example.com/image.png)");

		Assert.AreEqual("alt text", GetText(SingleParagraphBlock(element).Inlines));
		Assert.AreEqual(0, FindInlines(element).OfType<InlineUIContainer>().Count());
	}

	[TestMethod]
	public void CreateContent_ImageWithoutAltText_RendersMutedPlaceholder()
	{
		Control element = MarkdownRenderer.CreateContent("![](https://example.com/image.png)");

		Assert.AreEqual("https://example.com/image.png", GetText(SingleParagraphBlock(element).Inlines));
	}

	[TestMethod]
	public void CreateContent_RawHtmlBlock_RendersAsLiteralText()
	{
		Assert.AreEqual("<div>raw</div>", GetText(SingleParagraphBlock(MarkdownRenderer.CreateContent("<div>raw</div>")).Inlines));
	}

	[TestMethod]
	public void CreateContent_HtmlEntity_DecodesToCharacter()
	{
		Assert.AreEqual("AT&T <ok> A", GetText(SingleParagraphBlock(MarkdownRenderer.CreateContent("AT&amp;T &lt;ok&gt; &#65;")).Inlines));
	}

	[TestMethod]
	public void CreateContent_FencedCodeBlock_ProducesReadOnlyEditor()
	{
		Control element = MarkdownRenderer.CreateContent("before\n\n```lua\nlocal value = 1\nprint(value)\n```\n\nafter");
		TextEditor editor = GetCodeBlockEditor(element);

		Assert.AreEqual("local value = 1\nprint(value)", editor.Text);
		Assert.IsTrue(editor.IsReadOnly);
		Assert.IsFalse(editor.Focusable);
		Assert.IsFalse(editor.IsTabStop);
	}

	[TestMethod]
	public void CreateContent_IndentedCodeBlock_ProducesEditor()
	{
		TextEditor editor = GetCodeBlockEditor(MarkdownRenderer.CreateContent("    var x = 1;"));

		Assert.AreEqual("var x = 1;", editor.Text);
	}

	[TestMethod]
	public void CreateContent_CodeBlockInsideBlockquote_ProducesEditor()
	{
		TextEditor editor = GetCodeBlockEditor(MarkdownRenderer.CreateContent("> ```\n> local x = 1\n> ```"));

		Assert.AreEqual("local x = 1", editor.Text);
	}

	[TestMethod]
	public void CreateContent_CrLfInput_RendersParagraphsAndCodeBlock()
	{
		Control element = MarkdownRenderer.CreateContent("first\r\n\r\n```lua\r\nlocal value = 1\r\n```");

		Assert.AreEqual(2, GetBlockPanel(element).Children.Count);
		Assert.AreEqual("local value = 1", GetCodeBlockEditor(element).Text);
	}

	[TestMethod]
	public void CreateContent_ShortCodeBlock_KeepsAutomaticHeightAndScrollBar()
	{
		Control element = MarkdownRenderer.CreateContent("```lua\nlocal value = 1\n```");
		TextEditor editor = GetCodeBlockEditor(element);

		Assert.AreEqual(ScrollBarVisibility.Auto, editor.VerticalScrollBarVisibility);
		Assert.IsTrue(double.IsNaN(editor.Height), "A short block must not fix its height.");
		Assert.IsTrue(editor.MaxHeight > 0.0, "A short block still carries the visible-line clamp.");
	}

	[TestMethod]
	public void CreateContent_LongCodeBlock_IsClampedToTheVisibleLineLimit()
	{
		string code = string.Join("\n", Enumerable.Range(1, 40).Select(index => $"local value{index} = {index}"));
		var theme = MarkdownRenderTheme.Default with { MaxVisibleCodeBlockLines = 5 };

		TextEditor editor = GetCodeBlockEditor(MarkdownRenderer.CreateContent($"```lua\n{code}\n```", theme));

		Assert.AreEqual(ScrollBarVisibility.Auto, editor.VerticalScrollBarVisibility);
		Assert.IsTrue(editor.MaxHeight > 0.0, "The block must carry the visible-line clamp.");
		Assert.IsTrue(double.IsNaN(editor.Height) || editor.Height <= editor.MaxHeight);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_DefaultOptions_IsReadOnlyAndNotHeightClamped()
	{
		TextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("lua", "local value = 1");

		Assert.IsTrue(editor.IsReadOnly);
		Assert.IsTrue(editor.WordWrap);
		Assert.IsFalse(editor.ShowLineNumbers);
		Assert.IsFalse(editor.Options.EnableHyperlinks);
		Assert.IsFalse(editor.Options.EnableEmailHyperlinks);
		Assert.IsFalse(editor.Options.EnableTextDragDrop);
		Assert.IsFalse(editor.Focusable);
		Assert.IsFalse(editor.IsTabStop);
		Assert.IsTrue(double.IsNaN(editor.Height), "A directly created editor is not height-clamped.");
		Assert.IsTrue(double.IsPositiveInfinity(editor.MaxHeight), "A directly created editor is not height-clamped.");
	}

	[TestMethod]
	public void CreateCodeBlockEditor_SelectionAllowed_KeepsNativeMouseBehavior()
	{
		var options = new MarkdownRenderOptions { AllowCodeBlockSelection = true };

		TextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("lua", "local value = 1", null, options);

		Assert.IsFalse(editor.Focusable, "Selection does not change the focus policy.");
		Assert.IsTrue(editor.Options.EnableTextDragDrop);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_NormalizesLineEndingsAndTrimsOnlyTheTrailingTerminator()
	{
		TextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("lua", "a\r\nb\n\n\n");

		Assert.AreEqual("a\nb\n\n", editor.Text);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_CustomAlias_ResolvesConfiguredHighlighting()
	{
		var options = new MarkdownRenderOptions
		{
			HighlightingAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["mylang"] = ".cs"
			}
		};

		TextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("mylang", "x = 1", null, options);

		Assert.IsNotNull(editor.SyntaxHighlighting);
		Assert.AreSame(HighlightingManager.Instance.GetDefinitionByExtension(".cs"), editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_UnresolvedLanguage_LogsDebugEvent2002()
	{
		var logger = new CapturingLogger();
		var options = new MarkdownRenderOptions { Logger = logger };

		MarkdownRenderer.CreateCodeBlockEditor("definitely-not-a-language", "x = 1", null, options);

		CapturingLogger.LogEntry entry = logger.Entries.Single();

		Assert.AreEqual(2002, entry.EventId.Id);
		Assert.AreEqual(LogLevel.Debug, entry.Level);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_CustomHighlightingHook_IsInvoked()
	{
		bool invoked = false;

		var options = new MarkdownRenderOptions
		{
			CustomHighlightingInstaller = (editor, language) =>
			{
				invoked = true;
				Assert.AreEqual("lua", language);
				return false;
			}
		};

		MarkdownRenderer.CreateCodeBlockEditor("lua", "x = 1", null, options);

		Assert.IsTrue(invoked);
	}

	[TestMethod]
	public void CreateContent_HighlightingHookThrows_ReturnsPlainTextAndLogsWarning()
	{
		var logger = new CapturingLogger();
		var options = new MarkdownRenderOptions
		{
			CustomHighlightingInstaller = (_, _) => throw new InvalidOperationException("highlighting failure"),
			Logger = logger
		};

		Control element = MarkdownRenderer.CreateContent("```lua\nx = 1\n```", null, options);

		Assert.IsInstanceOfType<ScrollViewer>(element);
		Assert.AreEqual("```lua\nx = 1\n```", ((TextBlock)((ScrollViewer)element).Content!).Text);

		CapturingLogger.LogEntry entry = logger.Entries.Single();

		Assert.AreEqual(2000, entry.EventId.Id);
		Assert.AreEqual(LogLevel.Warning, entry.Level);
	}

	[TestMethod]
	public void CreateContent_BulletList_ProducesMarkerAndItems()
	{
		Control element = MarkdownRenderer.CreateContent("- one\n- two");
		var list = (StackPanel)GetBlockPanel(element).Children.Single();

		Assert.AreEqual(2, list.Children.Count);

		foreach (Control item in list.Children)
		{
			TextBlock marker = ((Grid)item).Children.OfType<TextBlock>().Single();
			Assert.AreEqual("\u2022", marker.Text);
		}
	}

	[TestMethod]
	public void CreateContent_OrderedList_StartsAtTheAuthoredNumber()
	{
		Control element = MarkdownRenderer.CreateContent("3. three\n4. four");
		var list = (StackPanel)GetBlockPanel(element).Children.Single();

		var markers = list.Children.OfType<Grid>().Select(grid => grid.Children.OfType<TextBlock>().Single().Text).ToList();

		CollectionAssert.AreEqual(new[] { "3.", "4." }, markers);
	}

	[TestMethod]
	public void CreateContent_OrderedListStartingAtZero_ClampsToNumberOne()
	{
		Control element = MarkdownRenderer.CreateContent("0. zero\n1. one");
		var list = (StackPanel)GetBlockPanel(element).Children.Single();

		TextBlock marker = ((Grid)list.Children[0]).Children.OfType<TextBlock>().Single();

		Assert.AreEqual("1.", marker.Text);
	}

	[TestMethod]
	public void CreateContent_TightList_SuppressesItemSpacingAndLooseListKeepsIt()
	{
		Control tight = MarkdownRenderer.CreateContent("- one\n- two");

		foreach (TextBlock block in GetListItemTextBlocks(tight))
			Assert.AreEqual(0.0, block.Margin.Top);

		Control loose = MarkdownRenderer.CreateContent("- one\n\n- two");

		foreach (TextBlock block in GetListItemTextBlocks(loose))
			Assert.AreEqual(MarkdownRenderTheme.Default.BlockSpacing, block.Margin.Bottom);
	}

	private static IEnumerable<TextBlock> GetListItemTextBlocks(Control content)
	{
		var list = (StackPanel)GetBlockPanel(content).Children.Single();

		foreach (Grid item in list.Children.OfType<Grid>())
		{
			var itemContent = (StackPanel)item.Children[1];

			foreach (TextBlock block in itemContent.Children.OfType<TextBlock>())
				yield return block;
		}
	}

	[TestMethod]
	public void CreateContent_Blockquote_ProducesLeftBar()
	{
		Control element = MarkdownRenderer.CreateContent("> quoted");
		Border quote = GetBlockPanel(element).Children.OfType<Border>().Single();

		Assert.AreEqual(3.0, quote.BorderThickness.Left);
		Assert.AreEqual(0.0, quote.BorderThickness.Top);
	}

	[TestMethod]
	public void CreateContent_ThematicBreak_ProducesOnePixelBar()
	{
		Control element = MarkdownRenderer.CreateContent("before\n\n---\n\nafter");
		Border separator = GetBlockPanel(element).Children.OfType<Border>().Single();

		Assert.AreEqual(1.0, separator.Height);
	}

	[TestMethod]
	public void CreateContent_Table_ProducesGridWithHeaderAndAlignment()
	{
		Control element = MarkdownRenderer.CreateContent("| left | right |\n|:--|--:|\n| 1 | 2 |");
		var grid = (Grid)GetBlockPanel(element).Children.Single();

		Assert.AreEqual(2, grid.RowDefinitions.Count);
		Assert.AreEqual(2, grid.ColumnDefinitions.Count);

		TextBlock headerLeft = GetCellText(grid, 0);
		TextBlock headerRight = GetCellText(grid, 1);

		Assert.AreEqual(FontWeight.Bold, headerLeft.FontWeight);
		Assert.AreEqual(TextAlignment.Left, headerLeft.TextAlignment);
		Assert.AreEqual(TextAlignment.Right, headerRight.TextAlignment);
	}

	private static TextBlock GetCellText(Grid grid, int childIndex)
	{
		var cell = (Border)grid.Children[childIndex];
		var content = (StackPanel)cell.Child!;

		return (TextBlock)content.Children[0];
	}

	[TestMethod]
	public void CreateContent_DarkSurface_CodeBackgroundContrastsWithSurface()
	{
		var darkSurface = Color.FromRgb(0x1E, 0x1E, 0x1E);
		var darkTheme = MarkdownRenderTheme.Default with { SurfaceBackground = new SolidColorBrush(darkSurface) };

		Control element = MarkdownRenderer.CreateContent("Use `x` here.\n\n```\ncode\n```", darkTheme);

		Color inlineBackground = ((ISolidColorBrush)GetInlineCodeBorder(element).Background!).Color;
		Color codeBackground = ((ISolidColorBrush)GetCodeBlockBorder(element).Background!).Color;

		// Both code surfaces blend toward the contrasting pole, so they are lighter than the dark surface.
		Assert.IsTrue(inlineBackground.R > darkSurface.R && inlineBackground.G > darkSurface.G && inlineBackground.B > darkSurface.B);
		Assert.AreEqual(inlineBackground, codeBackground);
	}

	[TestMethod]
	public void CreateContent_DefaultViewer_IsPassive()
	{
		var viewer = (ScrollViewer)MarkdownRenderer.CreateContent("Some text.");

		Assert.IsFalse(viewer.Focusable);
		Assert.IsFalse(viewer.IsTabStop);
		Assert.IsInstanceOfType<TextBlock>(SingleParagraphBlock(viewer));
	}

	[TestMethod]
	public void CreateContent_ContentInteractionEnabled_UsesSelectableText()
	{
		var options = new MarkdownRenderOptions { AllowContentInteraction = true };
		var viewer = (ScrollViewer)MarkdownRenderer.CreateContent("Some text.", null, options);

		Assert.IsTrue(viewer.Focusable);
		Assert.IsTrue(viewer.IsTabStop);
		Assert.IsInstanceOfType<SelectableTextBlock>(SingleParagraphBlock(viewer));
	}

	[TestMethod]
	public void CreatePlainTextContent_RendersTextInScrollViewer()
	{
		var viewer = (ScrollViewer)MarkdownRenderer.CreatePlainTextContent("plain text");
		var textBlock = (TextBlock)viewer.Content!;

		Assert.AreEqual("plain text", textBlock.Text);
		Assert.IsFalse(viewer.Focusable);
		Assert.IsFalse(viewer.IsTabStop);
	}

	[TestMethod]
	public void CreateContent_RenderFailure_IsNotReportedWithoutALogger()
	{
		// A throwing highlighting hook is the reproducible failure path; without a logger nothing is recorded.
		var options = new MarkdownRenderOptions
		{
			CustomHighlightingInstaller = (_, _) => throw new InvalidOperationException("failure")
		};

		Control element = MarkdownRenderer.CreateContent("```lua\nx = 1\n```", null, options);

		Assert.IsInstanceOfType<ScrollViewer>(element);
	}

	[TestMethod]
	public void CreateContentTree_ReturnsTheBlockPanelWithoutViewerChrome()
	{
		Control tree = MarkdownRenderer.CreateContentTree("plain text");

		Assert.IsInstanceOfType<StackPanel>(tree);
	}
}
