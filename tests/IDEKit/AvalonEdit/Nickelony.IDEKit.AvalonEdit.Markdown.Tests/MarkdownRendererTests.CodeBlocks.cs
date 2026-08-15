using ICSharpCode.AvalonEdit.Highlighting;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using AvalonTextEditor = ICSharpCode.AvalonEdit.TextEditor;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

public sealed partial class MarkdownRendererTests
{
	[TestMethod]
	public void CreateContent_FencedCodeBlock_ProducesTextEditorCodeBlock()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("before\n\n```lua\nlocal value = 1\nprint(value)\n```\n\nafter");
		var viewer = (FlowDocumentScrollViewer)element;

		AvalonTextEditor editor = GetCodeBlockEditor(viewer.Document);

		Assert.AreEqual("local value = 1\nprint(value)", editor.Text);
		Assert.IsFalse(editor.Focusable);
		Assert.IsFalse(editor.IsTabStop);
	}

	[TestMethod]
	public void CreateContent_CodeBlockInsideBlockquote_ProducesCodeBlockEditor()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("> ```\n> local x = 1\n> ```");
		var viewer = (FlowDocumentScrollViewer)element;

		AvalonTextEditor editor = GetCodeBlockEditor(viewer.Document);

		Assert.AreEqual("local x = 1", editor.Text);
	}

	[TestMethod]
	public void CreateContent_FencedCodeBlock_EditorStaysInsideBorderContentArea()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("before\n\n```csharp\nvar value = 1;\n```\n\nafter");
		var viewer = (FlowDocumentScrollViewer)element;

		viewer.Measure(new Size(MarkdownRenderTheme.Default.MaxWidth, MarkdownRenderTheme.Default.MaxHeight));
		viewer.Arrange(new Rect(0.0, 0.0, MarkdownRenderTheme.Default.MaxWidth, MarkdownRenderTheme.Default.MaxHeight));
		viewer.UpdateLayout();

		Border border = GetCodeBlockBorder(viewer.Document);
		var editor = (AvalonTextEditor)border.Child;

		Point editorPosition = editor.TranslatePoint(new Point(0.0, 0.0), border);
		double contentLeft = border.Padding.Left + border.BorderThickness.Left;
		double contentWidth = border.ActualWidth
			- border.Padding.Left
			- border.Padding.Right
			- border.BorderThickness.Left
			- border.BorderThickness.Right;

		Assert.IsTrue(editorPosition.X >= contentLeft);
		Assert.IsTrue(editor.ActualWidth <= contentWidth);
	}

	[TestMethod]
	public void CreateContent_ShortCodeBlock_KeepsAutomaticHeightAndScrollBar()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("```lua\nlocal value = 1\n```");
		var viewer = (FlowDocumentScrollViewer)element;
		Border border = GetCodeBlockBorder(viewer.Document);
		var editor = (AvalonTextEditor)border.Child;

		// The scroll bar is automatic and only appears when the content actually overflows, and a block
		// below the limit keeps an automatic height so the host layout reports the exact extent.
		Assert.AreEqual(ScrollBarVisibility.Auto, editor.VerticalScrollBarVisibility);
		Assert.IsTrue(double.IsNaN(editor.Height), "A short block must not fix its height.");
		Assert.IsTrue(editor.MaxHeight > 0.0, "A short block still carries the visible-line clamp.");
	}

	[TestMethod]
	public void CreateContent_LongCodeBlock_ClampsToTheVisibleLineLimit()
	{
		string code = string.Join("\n", Enumerable.Range(1, 40).Select(index => $"local value{index} = {index}"));
		var theme = MarkdownRenderTheme.Default with { MaxVisibleCodeBlockLines = 5 };
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent($"```lua\n{code}\n```", theme);
		Border border = GetCodeBlockBorder(viewer.Document);
		var editor = (AvalonTextEditor)border.Child;

		double lineHeight = Math.Ceiling(editor.TextArea.TextView.DefaultLineHeight);
		double expectedClamp = Math.Max(lineHeight + 4.0, Math.Ceiling(5 * lineHeight) + 2.0);

		Assert.AreEqual(ScrollBarVisibility.Auto, editor.VerticalScrollBarVisibility);
		Assert.AreEqual(expectedClamp, editor.Height, "The editor height must be the visible-line clamp.");
		Assert.AreEqual(expectedClamp, editor.MaxHeight);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_DefaultOptions_IsReadOnlyAndKeepsAutomaticHeight()
	{
		AvalonTextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("lua", "local value = 1");

		Assert.IsTrue(editor.IsReadOnly);
		Assert.IsTrue(editor.WordWrap);
		Assert.IsFalse(editor.ShowLineNumbers);
		Assert.IsFalse(editor.Options.EnableHyperlinks);
		Assert.IsFalse(editor.Options.EnableEmailHyperlinks);
		Assert.AreEqual(ScrollBarVisibility.Disabled, editor.HorizontalScrollBarVisibility);
		Assert.IsTrue(double.IsNaN(editor.Height), "A directly created editor is not height-clamped.");
		Assert.IsTrue(double.IsPositiveInfinity(editor.MaxHeight), "A directly created editor is not height-clamped.");
	}

	[TestMethod]
	public void CreateContent_LanguageTokens_ArePassedToTheHighlightingHook()
	{
		var languages = new List<string?>();
		var options = new MarkdownRenderOptions
		{
			CustomHighlightingInstaller = (_, language) =>
			{
				languages.Add(language);
				return false;
			}
		};

		MarkdownRenderer.CreateContent("```\ncode\n```\n\n    indented code", MarkdownRenderTheme.Default, options);

		// A bare fence carries an empty language token, while an indented block carries none at all.
		CollectionAssert.AreEqual(new string?[] { string.Empty, null }, languages);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_DefaultOptions_ArePassive()
	{
		AvalonTextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("lua", "local value = 1", MarkdownRenderTheme.Default);

		Assert.IsFalse(editor.Focusable);
		Assert.IsFalse(editor.IsTabStop);
		Assert.IsFalse(editor.TextArea.Focusable);
		Assert.IsFalse(editor.TextArea.IsTabStop);
		Assert.IsFalse(editor.Options.EnableTextDragDrop);
		Assert.IsTrue(RaisePreviewMouseLeftButtonDown(editor), "Mouse selection must be suppressed by default.");
	}

	[TestMethod]
	public void CreateCodeBlockEditor_SelectionAllowed_KeepsNativeMouseBehavior()
	{
		var options = new MarkdownRenderOptions { AllowCodeBlockSelection = true };

		AvalonTextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("lua", "local value = 1", MarkdownRenderTheme.Default, options);

		Assert.IsFalse(editor.Focusable, "Selection does not change the focus policy.");
		Assert.IsTrue(editor.Options.EnableTextDragDrop);
		Assert.IsFalse(RaisePreviewMouseLeftButtonDown(editor), "An opt-in code block must not suppress the mouse-down.");
	}

	[TestMethod]
	public void CreateContent_CodeBlockFollowsSelectionOption()
	{
		var options = new MarkdownRenderOptions { AllowCodeBlockSelection = true };
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("```lua\nx = 1\n```", MarkdownRenderTheme.Default, options);
		AvalonTextEditor editor = GetCodeBlockEditor(viewer.Document);

		Assert.IsFalse(RaisePreviewMouseLeftButtonDown(editor));

		var passiveViewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("```lua\nx = 1\n```");
		AvalonTextEditor passiveEditor = GetCodeBlockEditor(passiveViewer.Document);

		Assert.IsTrue(RaisePreviewMouseLeftButtonDown(passiveEditor));
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

		MarkdownRenderer.CreateCodeBlockEditor("lua", "x = 1", MarkdownRenderTheme.Default, options);

		Assert.IsTrue(invoked);
	}

	[TestMethod]
	public void CreateContent_CustomHighlightingHook_IsInvokedForFencedCode()
	{
		bool invoked = false;

		var options = new MarkdownRenderOptions
		{
			CustomHighlightingInstaller = (editor, language) =>
			{
				invoked = true;
				return false;
			}
		};

		MarkdownRenderer.CreateContent("```lua\nx = 1\n```", MarkdownRenderTheme.Default, options);

		Assert.IsTrue(invoked);
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

		AvalonTextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("mylang", "x = 1", MarkdownRenderTheme.Default, options);

		Assert.IsNotNull(editor.SyntaxHighlighting);
		Assert.AreSame(HighlightingManager.Instance.GetDefinitionByExtension(".cs"), editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_AliasMapWithOrdinalComparer_ResolvesCaseInsensitively()
	{
		var options = new MarkdownRenderOptions
		{
			HighlightingAliases = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["MyLang"] = ".cs"
			}
		};

		AvalonTextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("mylang", "x = 1", MarkdownRenderTheme.Default, options);

		// The options copy normalizes the map to case-insensitive keys, so a fence whose casing differs
		// from the assigned key still resolves.
		Assert.IsNotNull(editor.SyntaxHighlighting);
		Assert.AreSame(HighlightingManager.Instance.GetDefinitionByExtension(".cs"), editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_TypeScriptLanguage_ResolvesNoHighlighting()
	{
		// AvalonEdit ships no TypeScript definition, and the default aliases add no lookup that could
		// succeed; hosts must register one through the custom highlighting hook.
		AvalonTextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("ts", "let x = 1", MarkdownRenderTheme.Default);

		Assert.IsNull(editor.SyntaxHighlighting);
	}

	[DataRow("python", ".py", DisplayName = "python")]
	[DataRow("PYTHON", ".py", DisplayName = "PYTHON")]
	[DataRow("javascript", ".js", DisplayName = "javascript")]
	[DataRow("JAVASCRIPT", ".js", DisplayName = "JAVASCRIPT")]
	[DataRow("cs", ".cs", DisplayName = "cs")]
	[DataRow("js", ".js", DisplayName = "js")]
	[DataRow("json", ".json", DisplayName = "json")]
	[TestMethod]
	public void CreateCodeBlockEditor_CommonLanguageName_ResolvesHighlighting(string language, string extension)
	{
		AvalonTextEditor editor = MarkdownRenderer.CreateCodeBlockEditor(language, "x = 1", MarkdownRenderTheme.Default);

		Assert.IsNotNull(editor.SyntaxHighlighting, $"The language '{language}' did not resolve.");
		Assert.AreSame(
			HighlightingManager.Instance.GetDefinitionByExtension(extension),
			editor.SyntaxHighlighting,
			$"The language '{language}' resolved to an unexpected definition.");
	}

	[TestMethod]
	public void CreateCodeBlockEditor_UnresolvedLanguage_ReturnsNullHighlighting()
	{
		AvalonTextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("definitely-not-a-language", "x = 1", MarkdownRenderTheme.Default);

		Assert.IsNull(editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_UnresolvedLanguage_LogsDebugEvent2002ThroughConfiguredLogger()
	{
		var logger = new CapturingLogger();
		var options = new MarkdownRenderOptions { Logger = logger };

		MarkdownRenderer.CreateCodeBlockEditor("definitely-not-a-language", "x = 1", MarkdownRenderTheme.Default, options);

		CapturingLogger.LogEntry entry = logger.Entries.Single();

		Assert.AreEqual(2002, entry.EventId.Id);
		Assert.AreEqual(LogLevel.Debug, entry.Level);
		Assert.IsTrue(entry.Message.Contains("definitely-not-a-language", StringComparison.Ordinal));
	}

	[TestMethod]
	public void CreateCodeBlockEditor_CustomHighlightingHookHandled_SkipsBuiltInResolution()
	{
		IHighlightingDefinition jsonDefinition = HighlightingManager.Instance.GetDefinition("Json")!;

		var options = new MarkdownRenderOptions
		{
			CustomHighlightingInstaller = (editor, _) =>
			{
				editor.SyntaxHighlighting = jsonDefinition;
				return true;
			}
		};

		AvalonTextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("csharp", "x = 1", MarkdownRenderTheme.Default, options);

		Assert.AreSame(jsonDefinition, editor.SyntaxHighlighting);
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

		FrameworkElement element = MarkdownRenderer.CreateContent("```lua\nx = 1\n```", MarkdownRenderTheme.Default, options);

		Assert.IsInstanceOfType<ScrollViewer>(element);
		Assert.AreEqual("```lua\nx = 1\n```", ((TextBlock)((ScrollViewer)element).Content).Text);

		CapturingLogger.LogEntry entry = logger.Entries.Single();

		Assert.AreEqual(2000, entry.EventId.Id);
		Assert.AreEqual(LogLevel.Warning, entry.Level);
		Assert.IsTrue(entry.Message.Contains("Markdown rendering failed", StringComparison.Ordinal));
	}

	[TestMethod]
	public void CreateContent_CrLfInput_RendersParagraphsAndCodeBlock()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("first\r\n\r\n```lua\r\nlocal value = 1\r\n```", MarkdownRenderTheme.Default);
		var viewer = (FlowDocumentScrollViewer)element;

		Assert.AreEqual(2, viewer.Document.Blocks.Count);

		Border border = GetCodeBlockBorder(viewer.Document);
		var editor = (AvalonTextEditor)border.Child;

		Assert.AreEqual("local value = 1", editor.Text);
	}

	[TestMethod]
	public void CreateContent_IndentedCodeBlock_ProducesCodeBlockEditor()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("    var x = 1;");
		var viewer = (FlowDocumentScrollViewer)element;
		Border border = GetCodeBlockBorder(viewer.Document);
		var editor = (AvalonTextEditor)border.Child;

		Assert.AreEqual("var x = 1;", editor.Text);
	}

	[TestMethod]
	public void CreateContent_FenceInfoWithAttributes_ResolvesLanguageToken()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("```cs title=\"sample\"\nvar x = 1;\n```");
		var viewer = (FlowDocumentScrollViewer)element;
		Border border = GetCodeBlockBorder(viewer.Document);
		var editor = (AvalonTextEditor)border.Child;

		Assert.IsNotNull(editor.SyntaxHighlighting, "The first info-string token must resolve the language.");
	}

	[TestMethod]
	public void CreateCodeBlockEditor_NormalizesLineEndingsAndTrimsOnlyTheTrailingTerminator()
	{
		AvalonTextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("lua", "a\r\nb\n\n\n");

		// Only the conventional final terminator is removed; the authored trailing blank lines stay.
		Assert.AreEqual("a\nb\n\n", editor.Text);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_TrailingTerminator_IsRemoved()
	{
		AvalonTextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("lua", "a\nb\n");

		Assert.AreEqual("a\nb", editor.Text);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_TextWithoutTrailingBlankLines_IsUnchanged()
	{
		AvalonTextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("lua", "a\nb");

		Assert.AreEqual("a\nb", editor.Text);
	}

	[TestMethod]
	public void CreateFlowDocument_HighlightingHookThrows_ReturnsPlainTextDocumentAndLogsWarning()
	{
		var logger = new CapturingLogger();
		var options = new MarkdownRenderOptions
		{
			CustomHighlightingInstaller = (_, _) => throw new InvalidOperationException("highlighting failure"),
			Logger = logger
		};

		FlowDocument document = MarkdownRenderer.CreateFlowDocument("```lua\nx = 1\n```", MarkdownRenderTheme.Default, options);

		Paragraph paragraph = document.Blocks.OfType<Paragraph>().Single();
		StringAssert.Contains(GetParagraphText(paragraph), "x = 1");

		CapturingLogger.LogEntry entry = logger.Entries.Single();

		Assert.AreEqual(2000, entry.EventId.Id);
		Assert.AreEqual(LogLevel.Warning, entry.Level);
		Assert.IsTrue(entry.Message.Contains("Markdown rendering failed", StringComparison.Ordinal));
	}

	[TestMethod]
	public void CreateCodeBlockEditor_HookDeclines_UsesBuiltInResolution()
	{
		var options = new MarkdownRenderOptions
		{
			CustomHighlightingInstaller = (_, _) => false
		};

		AvalonTextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("cs", "x = 1", MarkdownRenderTheme.Default, options);

		Assert.AreSame(HighlightingManager.Instance.GetDefinitionByExtension(".cs"), editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_HookThrows_PropagatesToTheCaller()
	{
		var options = new MarkdownRenderOptions
		{
			CustomHighlightingInstaller = (_, _) => throw new InvalidOperationException("highlighting failure")
		};

		Assert.ThrowsExactly<InvalidOperationException>(() =>
			MarkdownRenderer.CreateCodeBlockEditor("lua", "x = 1", MarkdownRenderTheme.Default, options));
	}

	[TestMethod]
	public void CreateCodeBlockEditor_NullLanguage_ReturnsEditorWithoutHighlightingOrDiagnostics()
	{
		var logger = new CapturingLogger();
		var options = new MarkdownRenderOptions { Logger = logger };

		AvalonTextEditor editor = MarkdownRenderer.CreateCodeBlockEditor(null, "x = 1", MarkdownRenderTheme.Default, options);

		Assert.IsNull(editor.SyntaxHighlighting);
		Assert.AreEqual(0, logger.Entries.Count, "A block without a language is not an unresolved language.");
	}

	[TestMethod]
	public void CreateCodeBlockEditor_BlankAliasValue_IsIgnored()
	{
		var options = new MarkdownRenderOptions
		{
			HighlightingAliases = new Dictionary<string, string> { ["cs"] = "   " }
		};

		AvalonTextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("cs", "x = 1", MarkdownRenderTheme.Default, options);

		// The blank alias is ignored, so the language falls back to the built-in extension resolution; a
		// used blank alias would resolve nothing.
		Assert.AreSame(HighlightingManager.Instance.GetDefinitionByExtension(".cs"), editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_AliasWithoutExtension_ResolvesDefinitionName()
	{
		var options = new MarkdownRenderOptions
		{
			HighlightingAliases = new Dictionary<string, string> { ["probelang"] = "C#" }
		};

		AvalonTextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("probelang", "x = 1", MarkdownRenderTheme.Default, options);

		Assert.AreSame(HighlightingManager.Instance.GetDefinition("C#"), editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateContent_HostAlias_ResolvesFencedLanguage()
	{
		var options = new MarkdownRenderOptions
		{
			HighlightingAliases = new Dictionary<string, string> { ["probelang"] = ".cs" }
		};
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("```probelang\nvar x = 1;\n```", MarkdownRenderTheme.Default, options);
		AvalonTextEditor editor = GetCodeBlockEditor(viewer.Document);

		Assert.AreSame(HighlightingManager.Instance.GetDefinitionByExtension(".cs"), editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateContent_ScrollingDisabled_CodeBlockDisablesScrollBar()
	{
		var options = new MarkdownRenderOptions { AllowScrolling = false };
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("```lua\nlocal value = 1\n```", MarkdownRenderTheme.Default, options);
		AvalonTextEditor editor = GetCodeBlockEditor(viewer.Document);

		Assert.AreEqual(ScrollBarVisibility.Disabled, editor.VerticalScrollBarVisibility);
		Assert.IsTrue(editor.MaxHeight > 0.0, "A block still carries the visible-line clamp when scrolling is disabled.");
	}

	[DataRow("", DisplayName = "Empty")]
	[DataRow("   \n\n", DisplayName = "BlankLinesOnly")]
	[TestMethod]
	public void CreateCodeBlockEditor_BlankCode_NormalizesToEmptyText(string code)
	{
		AvalonTextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("lua", code);

		Assert.AreEqual(string.Empty, editor.Text);
	}
}
