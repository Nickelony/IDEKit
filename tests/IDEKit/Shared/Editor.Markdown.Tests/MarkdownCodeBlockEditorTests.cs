#if AVALONIAEDIT
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using AvaloniaEdit.Highlighting;
using Microsoft.Extensions.Logging;
using EditorControl = AvaloniaEdit.TextEditor;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;

#else
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Highlighting;
using Microsoft.Extensions.Logging;
using EditorControl = ICSharpCode.AvalonEdit.TextEditor;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

#endif

/// <summary>
/// Pins the toolkit-neutral code-block editor scenarios: the scrolling policy a standalone editor follows, the line-ending normalization,
/// the alias resolution against the engine's highlighting manager, and the plain-text fallback that a throwing highlighting hook produces.
/// </summary>
[STATestClass]
public sealed class MarkdownCodeBlockEditorTests
{
	[TestMethod]
	public void CreateCodeBlockEditor_ScrollingDisabled_DisablesTheVerticalScrollBar()
	{
		var options = new MarkdownRenderOptions { AllowScrolling = false };

		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor(language: "lua", code: "local value = 1", theme: null, options);

		// A standalone editor follows the same scrolling policy as an embedded block.
		Assert.AreEqual(ScrollBarVisibility.Disabled, editor.VerticalScrollBarVisibility);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_DefaultOptions_KeepsTheAutomaticVerticalScrollBar()
	{
		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor("lua", "local value = 1");
		Assert.AreEqual(ScrollBarVisibility.Auto, editor.VerticalScrollBarVisibility);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_NormalizesLineEndingsAndTrimsOnlyTheTrailingTerminator()
	{
		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor("lua", "a\r\nb\n\n\n");

		// Only the conventional final terminator is removed; the authored trailing blank lines stay.
		Assert.AreEqual("a\nb\n\n", editor.Text);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_CustomAlias_ResolvesConfiguredHighlighting()
	{
		var options = new MarkdownRenderOptions
		{
			HighlightingAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["mylang"] = ".cs" },
		};

		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor(language: "mylang", code: "x = 1", theme: null, options);

		Assert.IsNotNull(editor.SyntaxHighlighting);
		Assert.AreSame(HighlightingManager.Instance.GetDefinitionByExtension(".cs"), editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateContent_HighlightingHookThrows_ReturnsPlainTextAndLogsWarning()
	{
		var logger = new RecordingLogger();

		var options = new MarkdownRenderOptions { Logger = logger };

		object element = MarkdownRenderer.CreateContent(
			content: "```lua\nx = 1\n```",
			theme: null,
			options,
			customHighlightingInstaller: (_, _) => throw new InvalidOperationException("highlighting failure")
		);

		Assert.IsInstanceOfType<ScrollViewer>(element);
		Assert.AreEqual("```lua\nx = 1\n```", ((TextBlock)((ScrollViewer)element).Content!).Text);

		RecordingLogger.LogEntry entry = logger.Entries.Single();

		Assert.AreEqual(2000, entry.EventId.Id);
		Assert.AreEqual(LogLevel.Warning, entry.Level);
		Assert.IsTrue(entry.Message.Contains("Markdown rendering failed", StringComparison.Ordinal));
	}

	[TestMethod]
	public void CreateContent_HighlightingHookThrowsWithoutALogger_ReturnsPlainText()
	{
		// A throwing highlighting hook is the reproducible failure path; without a logger nothing is recorded.
		object element = MarkdownRenderer.CreateContent(
			content: "```lua\nx = 1\n```",
			theme: null,
			options: null,
			customHighlightingInstaller: (_, _) => throw new InvalidOperationException("highlighting failure")
		);

		Assert.IsInstanceOfType<ScrollViewer>(element);
		Assert.AreEqual("```lua\nx = 1\n```", ((TextBlock)((ScrollViewer)element).Content!).Text);
	}
}
