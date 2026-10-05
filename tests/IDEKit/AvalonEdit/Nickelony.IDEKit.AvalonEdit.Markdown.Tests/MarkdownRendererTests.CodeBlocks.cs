using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using EditorControl = ICSharpCode.AvalonEdit.TextEditor;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

public sealed partial class MarkdownRendererTests
{
	[TestMethod]
	public void CreateContent_FencedCodeBlock_EditorStaysInsideBorderContentArea()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("before\n\n```csharp\nvar value = 1;\n```\n\nafter");
		var viewer = (FlowDocumentScrollViewer)element;

		viewer.Measure(new Size(MarkdownRenderTheme.Default.MaxWidth, MarkdownRenderTheme.Default.MaxHeight));
		viewer.Arrange(new Rect(x: 0.0, y: 0.0, width: MarkdownRenderTheme.Default.MaxWidth, height: MarkdownRenderTheme.Default.MaxHeight));
		viewer.UpdateLayout();

		Border border = GetCodeBlockBorder(viewer.Document);

		var editor = (EditorControl)border.Child;

		Point editorPosition = editor.TranslatePoint(new Point(0.0, 0.0), border);
		double contentLeft = border.Padding.Left + border.BorderThickness.Left;

		double contentWidth =
			border.ActualWidth - border.Padding.Left - border.Padding.Right - border.BorderThickness.Left - border.BorderThickness.Right;

		Assert.IsGreaterThanOrEqualTo(contentLeft, editorPosition.X);
		Assert.IsLessThanOrEqualTo(contentWidth, editor.ActualWidth);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_DefaultOptions_AreNoKeyboardNavigationTabStop()
	{
		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor("lua", "local value = 1", MarkdownRenderTheme.Default);

		Assert.IsFalse(KeyboardNavigation.GetIsTabStop(editor));
		Assert.IsFalse(KeyboardNavigation.GetIsTabStop(editor.TextArea));
	}

	[TestMethod]
	public void CreateContentTree_HighlightingHookThrows_ReturnsPlainTextDocumentAndLogsWarning()
	{
		var logger = new RecordingLogger();

		var options = new MarkdownRenderOptions { Logger = logger };

		FlowDocument document = MarkdownRenderer.CreateContentTree(
			content: "```lua\nx = 1\n```",
			theme: MarkdownRenderTheme.Default,
			options,
			customHighlightingInstaller: (_, _) => throw new InvalidOperationException("highlighting failure")
		);

		Paragraph paragraph = document.Blocks.OfType<Paragraph>().Single();
		Assert.Contains("x = 1", GetParagraphText(paragraph));

		RecordingLogger.LogEntry entry = logger.Entries.Single();

		Assert.AreEqual(2000, entry.EventId.Id);
		Assert.AreEqual(LogLevel.Warning, entry.Level);
		Assert.IsTrue(entry.Message.Contains("Markdown rendering failed", StringComparison.Ordinal));
	}
}
