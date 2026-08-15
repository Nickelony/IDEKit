using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AvalonTextEditor = ICSharpCode.AvalonEdit.TextEditor;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

public sealed partial class MarkdownRendererTests
{
	[TestMethod]
	public void CreateContent_OutOfRangeBlendRatio_IsClamped()
	{
		var highTheme = MarkdownRenderTheme.Default with { CodeBackgroundBlendRatio = 5.0 };
		var highViewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("Use `x` here.", highTheme);
		var highBorder = (Border)FindAll<InlineUIContainer>(highViewer.Document).Single().Child;

		// The white base color is blended fully toward black.
		Assert.AreEqual(Color.FromRgb(0, 0, 0), ((SolidColorBrush)highBorder.Background).Color);

		var lowTheme = MarkdownRenderTheme.Default with { CodeBackgroundBlendRatio = -1.0 };
		var lowViewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("Use `x` here.", lowTheme);
		var lowBorder = (Border)FindAll<InlineUIContainer>(lowViewer.Document).Single().Child;

		// Clamped to zero: the base color is used unchanged.
		Assert.AreEqual(Colors.White, ((SolidColorBrush)lowBorder.Background).Color);
	}

	[TestMethod]
	public void Theme_NullBrush_ThrowsArgumentNullReportingThePropertyName()
	{
		// The three brush members share one contract: a non-null SolidColorBrush, rejected at the
		// offending with-expression instead of later while content is rendered.
		ArgumentNullException foregroundException = Assert.ThrowsExactly<ArgumentNullException>(
			() => _ = MarkdownRenderTheme.Default with { Foreground = null! });
		Assert.AreEqual(nameof(MarkdownRenderTheme.Foreground), foregroundException.ParamName);

		ArgumentNullException surfaceException = Assert.ThrowsExactly<ArgumentNullException>(
			() => _ = MarkdownRenderTheme.Default with { SurfaceBackground = null! });
		Assert.AreEqual(nameof(MarkdownRenderTheme.SurfaceBackground), surfaceException.ParamName);

		ArgumentNullException linkException = Assert.ThrowsExactly<ArgumentNullException>(
			() => _ = MarkdownRenderTheme.Default with { LinkForeground = null! });
		Assert.AreEqual(nameof(MarkdownRenderTheme.LinkForeground), linkException.ParamName);
	}

	[TestMethod]
	public void CreateContent_DefaultThemeBorders_ContrastWithTheSurface()
	{
		// White surface: the border blends toward black (18% of the way) instead of staying white.
		FrameworkElement element = MarkdownRenderer.CreateContent("> quoted");
		var viewer = (FlowDocumentScrollViewer)element;
		Section section = FindAll<Section>(viewer.Document).Single();

		Assert.AreEqual(Color.FromRgb(0xD1, 0xD1, 0xD1), ((SolidColorBrush)section.BorderBrush).Color);

		// Dark surface: the border blends toward white and stays lighter than the surface.
		var darkTheme = MarkdownRenderTheme.Default with { SurfaceBackground = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)) };
		var darkElement = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("> quoted", darkTheme);
		Section darkSection = FindAll<Section>(darkElement.Document).Single();
		Color darkBorderColor = ((SolidColorBrush)darkSection.BorderBrush).Color;

		Assert.IsTrue(darkBorderColor.R > 0x1E && darkBorderColor.G > 0x1E && darkBorderColor.B > 0x1E);
	}

	[TestMethod]
	public void CreateContent_ThemeFlowDirection_FlowsIntoViewerDocumentAndCodeBlocks()
	{
		var theme = MarkdownRenderTheme.Default with { FlowDirection = FlowDirection.RightToLeft };
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("Use `x` here.\n\n```lua\nx = 1\n```", theme);

		Assert.AreEqual(FlowDirection.RightToLeft, viewer.FlowDirection);
		Assert.AreEqual(FlowDirection.RightToLeft, viewer.Document.FlowDirection);

		var inlineBorder = (Border)FindAll<InlineUIContainer>(viewer.Document).Single().Child;
		var codeBorder = (Border)FindAll<BlockUIContainer>(viewer.Document).Single().Child;
		var editor = (AvalonTextEditor)codeBorder.Child;

		Assert.AreEqual(FlowDirection.RightToLeft, inlineBorder.FlowDirection);
		Assert.AreEqual(FlowDirection.RightToLeft, codeBorder.FlowDirection);
		Assert.AreEqual(FlowDirection.RightToLeft, editor.FlowDirection);
	}

	[TestMethod]
	public void CreateContent_DefaultViewer_IsPassive()
	{
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("Some text.");

		Assert.IsFalse(viewer.IsSelectionEnabled);
		Assert.IsFalse(viewer.Focusable);
		Assert.IsFalse(viewer.IsTabStop);
	}

	[TestMethod]
	public void CreateContent_ContentInteractionEnabled_ViewerAllowsSelectionAndKeyboardFocus()
	{
		var options = new MarkdownRenderOptions { AllowContentInteraction = true };
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("Some text.", MarkdownRenderTheme.Default, options);

		Assert.IsTrue(viewer.IsSelectionEnabled);
		Assert.IsTrue(viewer.Focusable);
		Assert.IsTrue(viewer.IsTabStop);
	}
}
