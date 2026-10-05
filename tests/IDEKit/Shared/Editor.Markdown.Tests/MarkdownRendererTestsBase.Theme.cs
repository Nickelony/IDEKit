#if AVALONIAEDIT
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Nickelony.IDEKit.Core.Media;
using EditorControl = AvaloniaEdit.TextEditor;
using SharedControl = Avalonia.Controls.Control;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;

#else
using System.Windows;
using System.Windows.Controls;
using Nickelony.IDEKit.Core.Media;
using EditorControl = ICSharpCode.AvalonEdit.TextEditor;
using SharedControl = System.Windows.FrameworkElement;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

#endif

public abstract partial class MarkdownRendererTestsBase
{
	/// <summary>Returns the margin of each heading and paragraph directly under the content root, in document order.</summary>
	protected abstract IReadOnlyList<(double Top, double Bottom)> GetTopLevelTextMargins(SharedControl content);

	/// <summary>Returns the color of the border of the only blockquote in the content.</summary>
	protected abstract ArgbColor GetBlockquoteBorderColor(SharedControl content);

	/// <summary>Returns the color of a text block whose foreground is a single solid color.</summary>
	protected abstract ArgbColor GetSolidForeground(TextBlock textBlock);

	/// <summary>Returns the vertical and horizontal scroll bar visibility of the viewer that wraps the content.</summary>
	protected abstract (ScrollBarVisibility Vertical, ScrollBarVisibility Horizontal) GetViewerScrollBars(SharedControl content);

	[TestMethod]
	public void CreateContent_OutOfRangeBlendRatio_IsClamped()
	{
		MarkdownRenderTheme highTheme = MarkdownRenderTheme.Default with { CodeBackgroundBlendRatio = 5.0 };

		// The white base color is blended fully toward black.
		Assert.AreEqual(
			ArgbColor.FromRgb(0, 0, 0),
			GetSolidBackground(FindInlineCodeBorder(MarkdownRenderer.CreateContent("Use `x` here.", highTheme)))
		);

		MarkdownRenderTheme lowTheme = MarkdownRenderTheme.Default with { CodeBackgroundBlendRatio = -1.0 };

		// Clamped to zero: the base color is used unchanged.
		Assert.AreEqual(
			ArgbColor.FromRgb(0xFF, 0xFF, 0xFF),
			GetSolidBackground(FindInlineCodeBorder(MarkdownRenderer.CreateContent("Use `x` here.", lowTheme)))
		);
	}

	[TestMethod]
	public void CreateContent_DefaultThemeBorders_ContrastWithTheSurface()
	{
		// White surface: the border blends toward black (18% of the way) instead of staying white.
		Assert.AreEqual(ArgbColor.FromRgb(0xD1, 0xD1, 0xD1), GetBlockquoteBorderColor(MarkdownRenderer.CreateContent("> quoted")));

		// Dark surface: the border blends toward white and stays lighter than the surface.
		MarkdownRenderTheme darkTheme = MarkdownRenderTheme.Default with
		{
			SurfaceBackground = ArgbColor.FromRgb(0x1E, 0x1E, 0x1E),
		};

		ArgbColor darkBorder = GetBlockquoteBorderColor(MarkdownRenderer.CreateContent("> quoted", darkTheme));

		Assert.IsTrue(darkBorder.R > 0x1E && darkBorder.G > 0x1E && darkBorder.B > 0x1E);
	}

	[TestMethod]
	public void CreateContent_ThemeFlowDirection_FlowsIntoCodeSurfacesAndTheEditor()
	{
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with { FlowDirection = MarkdownFlowDirection.RightToLeft };

		SharedControl viewer = MarkdownRenderer.CreateContent("Use `x` here.\n\n```lua\nx = 1\n```", theme);

		Border codeBorder = FindCodeBlockBorder(viewer);

		Assert.AreEqual(FlowDirection.RightToLeft, viewer.FlowDirection);
		Assert.AreEqual(FlowDirection.RightToLeft, FindInlineCodeBorder(viewer).FlowDirection);
		Assert.AreEqual(FlowDirection.RightToLeft, codeBorder.FlowDirection);
		Assert.AreEqual(FlowDirection.RightToLeft, ((EditorControl)codeBorder.Child!).FlowDirection);
	}

	[TestMethod]
	public void CreateContent_BlockSpacing_DrivesBlockMargins()
	{
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with { BlockSpacing = 20.0 };

		IReadOnlyList<(double Top, double Bottom)> textMargins = GetTopLevelTextMargins(MarkdownRenderer.CreateContent("# Heading\n\ntext", theme));

		Border breakBorder = FindThematicBreak(MarkdownRenderer.CreateContent("---", theme));
		Border codeBorder = FindCodeBlockBorder(MarkdownRenderer.CreateContent("```lua\nx = 1\n```", theme));

		Assert.HasCount(2, textMargins);
		Assert.AreEqual(20.0, textMargins[0].Top);
		Assert.AreEqual(20.0, textMargins[0].Bottom);
		Assert.AreEqual(20.0, textMargins[1].Bottom);
		Assert.AreEqual(20.0, breakBorder.Margin.Top);
		Assert.AreEqual(20.0, breakBorder.Margin.Bottom);
		Assert.AreEqual(20.0, codeBorder.Margin.Top);
		Assert.AreEqual(20.0, codeBorder.Margin.Bottom);
	}

	[TestMethod]
	public void CreateContent_ScrollingDisabled_ViewerCarriesThemeLimitsAndDisabledScrollBars()
	{
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with { MaxWidth = 321.0, MaxHeight = 123.0 };

		var options = new MarkdownRenderOptions { AllowScrolling = false };
		SharedControl viewer = MarkdownRenderer.CreateContent("Some text.", theme, options);

		(ScrollBarVisibility vertical, ScrollBarVisibility horizontal) = GetViewerScrollBars(viewer);

		Assert.AreEqual(321.0, viewer.MaxWidth);
		Assert.AreEqual(123.0, viewer.MaxHeight);
		Assert.AreEqual(ScrollBarVisibility.Disabled, vertical);
		Assert.AreEqual(ScrollBarVisibility.Disabled, horizontal);
	}

	[TestMethod]
	public void CreatePlainTextContent_ThemeValues_FlowIntoTheFallback()
	{
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with
		{
			FlowDirection = MarkdownFlowDirection.RightToLeft,
			MaxWidth = 123.0,
			MaxHeight = 45.0,
		};

		ScrollViewer scrollViewer = MarkdownRenderer.CreatePlainTextContent("plain text", theme);
		var textBlock = (TextBlock)scrollViewer.Content!;

		Assert.AreEqual(FlowDirection.RightToLeft, scrollViewer.FlowDirection);
		Assert.AreEqual(FlowDirection.RightToLeft, textBlock.FlowDirection);
		Assert.AreEqual(123.0, scrollViewer.MaxWidth);
		Assert.AreEqual(45.0, scrollViewer.MaxHeight);
	}

	[TestMethod]
	public void CreatePlainTextContent_MarkdownSyntax_IsDisplayedLiterally()
	{
		ScrollViewer scrollViewer = MarkdownRenderer.CreatePlainTextContent("**bold** and `code`");
		var textBlock = (TextBlock)scrollViewer.Content!;

		Assert.AreEqual("**bold** and `code`", textBlock.Text);
	}

	[TestMethod]
	public void CreatePlainTextContent_ThemeAndOptions_FlowIntoTheFallback()
	{
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with
		{
			BodyFontFamily = "Courier New",
			BodyFontSize = 17.0,
			Foreground = ArgbColor.FromRgb(0xFF, 0x00, 0x00),
		};

		var options = new MarkdownRenderOptions { AllowScrolling = false };
		ScrollViewer scrollViewer = MarkdownRenderer.CreatePlainTextContent("plain text", theme, options);
		var textBlock = (TextBlock)scrollViewer.Content!;

		Assert.AreEqual(ScrollBarVisibility.Disabled, scrollViewer.VerticalScrollBarVisibility);
		AssertFontFamilyMatches("Courier New", textBlock);

		Assert.AreEqual(17.0, textBlock.FontSize);
		Assert.AreEqual(ArgbColor.FromRgb(0xFF, 0x00, 0x00), GetSolidForeground(textBlock));
		Assert.AreEqual(TextWrapping.Wrap, textBlock.TextWrapping);
	}

	[TestMethod]
	public void CreateContent_ADocumentWithEveryBlockKind_ResolvesTheThemeResourcesOnce() =>
		AssertResolvesOnce(theme => _ = MarkdownRenderer.CreateContent(EveryBlockKindMarkdown, theme));

	[TestMethod]
	public void CreateContentTree_ADocumentWithEveryBlockKind_ResolvesTheThemeResourcesOnce() =>
		AssertResolvesOnce(theme => _ = MarkdownRenderer.CreateContentTree(EveryBlockKindMarkdown, theme));

	[TestMethod]
	public void CreateContent_WhitespaceOnlyContent_ResolvesTheThemeResourcesOnce() =>
		AssertResolvesOnce(theme => _ = MarkdownRenderer.CreateContent("   \n  ", theme));

	[TestMethod]
	public void CreatePlainTextContent_ResolvesTheThemeResourcesOnce() =>
		AssertResolvesOnce(theme => _ = MarkdownRenderer.CreatePlainTextContent("plain text", theme));

	[TestMethod]
	public void CreateCodeBlockEditor_ResolvesTheThemeResourcesOnce() =>
		AssertResolvesOnce(theme => _ = MarkdownRenderer.CreateCodeBlockEditor("lua", "local value = 1", theme));

	private const string EveryBlockKindMarkdown =
		"# Heading\n\n"
		+ "A paragraph with `code`, **bold**, *italic*, a [link](https://example.com) and ![alt](https://example.com/image.png).\n\n"
		+ "- first item\n- second item with `code`\n\n"
		+ "1. one\n2. two\n\n"
		+ "> A quote with `code`.\n\n"
		+ "---\n\n"
		+ "| A | B |\n| --- | --- |\n| `a` | **b** |\n| [c](https://example.com) | d |\n\n"
		+ "```csharp\nvar x = 1;\n```\n";

	// A theme instance of its own, so the count starts at the resolution this check makes and the render adds to it.
	private static void AssertResolvesOnce(Action<MarkdownRenderTheme> render)
	{
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with { BodyFontSize = 13.5 };
		var resources = MarkdownThemeResources.Resolve(theme);

		int resolvedBefore = resources.ResolveCount;

		render(theme);

		Assert.AreEqual(resolvedBefore + 1, resources.ResolveCount, "A render resolves its theme once and hands the resources to every element.");
	}
}
