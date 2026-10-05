#if AVALONIAEDIT
using Avalonia.Media;
using SharedControl = Avalonia.Controls.Control;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;

#else
using System.Windows;
using System.Windows.Media;
using SharedControl = System.Windows.FrameworkElement;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

#endif

/// <summary>
/// Pins the engine-neutral, viewer-level theme propagation shared by both editor bindings: the theme's flow direction and size limits reach the outer
/// viewer of rendered content and of the plain-text fallback.
/// </summary>
/// <remarks>
/// The content tree itself differs per engine and is asserted by the binding-specific suites.
/// </remarks>
[STATestClass]
public sealed class MarkdownRendererScenarioTests
{
	[TestMethod]
	public void CreateContent_ThemeFlowDirection_FlowsIntoTheViewer()
	{
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with { FlowDirection = MarkdownFlowDirection.RightToLeft };
		SharedControl viewer = MarkdownRenderer.CreateContent("Some text.", theme);

		Assert.AreEqual(FlowDirection.RightToLeft, viewer.FlowDirection);
	}

	[TestMethod]
	public void CreatePlainTextContent_ThemeFlowDirection_FlowsIntoTheViewer()
	{
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with { FlowDirection = MarkdownFlowDirection.RightToLeft };
		SharedControl viewer = MarkdownRenderer.CreatePlainTextContent("plain text", theme);

		Assert.AreEqual(FlowDirection.RightToLeft, viewer.FlowDirection);
	}

	[TestMethod]
	public void CreateContent_ThemeSizeLimits_FlowIntoTheViewer()
	{
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with { MaxWidth = 321.0, MaxHeight = 123.0 };
		SharedControl viewer = MarkdownRenderer.CreateContent("Some text.", theme);

		Assert.AreEqual(321.0, viewer.MaxWidth);
		Assert.AreEqual(123.0, viewer.MaxHeight);
	}

	[TestMethod]
	public void CreatePlainTextContent_ThemeSizeLimits_FlowIntoTheViewer()
	{
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with { MaxWidth = 321.0, MaxHeight = 123.0 };
		SharedControl viewer = MarkdownRenderer.CreatePlainTextContent("plain text", theme);

		Assert.AreEqual(321.0, viewer.MaxWidth);
		Assert.AreEqual(123.0, viewer.MaxHeight);
	}
}
