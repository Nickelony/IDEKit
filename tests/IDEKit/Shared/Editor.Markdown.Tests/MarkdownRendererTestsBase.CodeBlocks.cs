#if AVALONIAEDIT
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Microsoft.Extensions.Logging;
using EditorControl = AvaloniaEdit.TextEditor;
using SharedControl = Avalonia.Controls.Control;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;

#else
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using EditorControl = ICSharpCode.AvalonEdit.TextEditor;
using SharedControl = System.Windows.FrameworkElement;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

#endif

public abstract partial class MarkdownRendererTestsBase
{
	/// <summary>
	/// Presses the primary pointer button on the text surface of a code-block editor, as the pointer would, and returns whether a handler marked
	/// the press handled.
	/// </summary>
	protected abstract bool PressPrimaryButtonOn(EditorControl editor);

	[TestMethod]
	public void CreateContent_FencedCodeBlock_ProducesReadOnlyEditor()
	{
		SharedControl element = MarkdownRenderer.CreateContent("before\n\n```lua\nlocal value = 1\nprint(value)\n```\n\nafter");
		EditorControl editor = FindCodeBlockEditor(element);

		Assert.AreEqual("local value = 1\nprint(value)", editor.Text);
		Assert.IsTrue(editor.IsReadOnly);
		Assert.IsFalse(editor.Focusable);
		Assert.IsFalse(editor.IsTabStop);
	}

	[TestMethod]
	public void CreateContent_IndentedCodeBlock_ProducesEditor()
	{
		EditorControl editor = FindCodeBlockEditor(MarkdownRenderer.CreateContent("    var x = 1;"));
		Assert.AreEqual("var x = 1;", editor.Text);
	}

	[TestMethod]
	public void CreateContent_CodeBlockInsideBlockquote_ProducesEditor()
	{
		EditorControl editor = FindCodeBlockEditor(MarkdownRenderer.CreateContent("> ```\n> local x = 1\n> ```"));
		Assert.AreEqual("local x = 1", editor.Text);
	}

	[TestMethod]
	public void CreateContent_FenceWithOnlyBlankContent_ProducesAnEmptyEditor()
	{
		EditorControl editor = FindCodeBlockEditor(MarkdownRenderer.CreateContent("```lua\n   \n```"));
		Assert.AreEqual(string.Empty, editor.Text);
	}

	[TestMethod]
	public void CreateContent_FenceWithAnAuthoredTrailingBlankLine_KeepsTheBlankLine()
	{
		EditorControl editor = FindCodeBlockEditor(MarkdownRenderer.CreateContent("```lua\nlocal value = 1\n\n```"));
		Assert.AreEqual("local value = 1\n", editor.Text);
	}

	[TestMethod]
	public void CreateContent_CrLfInput_RendersParagraphsAndCodeBlock()
	{
		SharedControl element = MarkdownRenderer.CreateContent("first\r\n\r\n```lua\r\nlocal value = 1\r\n```");

		Assert.AreEqual(2, CountTopLevelBlocks(element));
		Assert.AreEqual("local value = 1", FindCodeBlockEditor(element).Text);
	}

	[TestMethod]
	public void CreateContent_ShortCodeBlock_KeepsAutomaticHeightAndScrollBar()
	{
		EditorControl editor = FindCodeBlockEditor(MarkdownRenderer.CreateContent("```lua\nlocal value = 1\n```"));

		// The scroll bar is automatic and only appears when the content overflows, and a block below the limit
		// keeps an automatic height so the host layout reports the exact extent.
		Assert.AreEqual(ScrollBarVisibility.Auto, editor.VerticalScrollBarVisibility);
		Assert.IsTrue(double.IsNaN(editor.Height), "A short block must not fix its height.");
		Assert.IsGreaterThan(0.0, editor.MaxHeight, "A short block still carries the visible-line clamp.");
	}

	[TestMethod]
	public void CreateContent_LongCodeBlock_IsClampedToTheVisibleLineLimit()
	{
		string code = string.Join("\n", Enumerable.Range(1, 40).Select(index => $"local value{index} = {index}"));
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with { MaxVisibleCodeBlockLines = 5 };

		EditorControl editor = FindCodeBlockEditor(MarkdownRenderer.CreateContent($"```lua\n{code}\n```", theme));

		// Measure a real line height when the text view reports one, and fall back to the font size otherwise,
		// so the asserted band is grounded in the actual layout rather than the factory's private padding.
		double measuredLineHeight = editor.TextArea.TextView.DefaultLineHeight;

		double lineHeight =
			double.IsFinite(measuredLineHeight) && measuredLineHeight > 0.0
				? Math.Ceiling(measuredLineHeight)
				: Math.Ceiling(Math.Max(1.0, editor.FontSize * 1.4));

		double fiveLineHeight = Math.Ceiling(5 * lineHeight);

		Assert.AreEqual(ScrollBarVisibility.Auto, editor.VerticalScrollBarVisibility);

		Assert.IsTrue(
			editor.MaxHeight >= fiveLineHeight && editor.MaxHeight <= 6.0 * lineHeight,
			$"The clamp {editor.MaxHeight} must sit in the 5-6 visible-line band."
		);

		Assert.AreEqual(editor.MaxHeight, editor.Height, "The editor height must be the visible-line clamp.");
	}

	[TestMethod]
	public void CreateCodeBlockEditor_DefaultOptions_IsReadOnlyAndNotHeightClamped()
	{
		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor("lua", "local value = 1");

		Assert.IsTrue(editor.IsReadOnly);
		Assert.IsTrue(editor.WordWrap);
		Assert.IsFalse(editor.ShowLineNumbers);
		Assert.IsFalse(editor.Options.EnableHyperlinks);
		Assert.IsFalse(editor.Options.EnableEmailHyperlinks);
		Assert.IsTrue(double.IsNaN(editor.Height), "A directly created editor is not height-clamped.");
		Assert.IsTrue(double.IsPositiveInfinity(editor.MaxHeight), "A directly created editor is not height-clamped.");
	}

	[TestMethod]
	public void CreateCodeBlockEditor_DefaultOptions_ArePassive()
	{
		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor("lua", "local value = 1");

		Assert.IsFalse(editor.Focusable);
		Assert.IsFalse(editor.IsTabStop);
		Assert.IsFalse(editor.Options.EnableTextDragDrop);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_SelectionAllowed_KeepsNativeMouseBehavior()
	{
		var options = new MarkdownRenderOptions { AllowCodeBlockSelection = true };

		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor(language: "lua", code: "local value = 1", theme: null, options);

		Assert.IsFalse(editor.Focusable, "Selection does not change the focus policy.");
		Assert.IsTrue(editor.Options.EnableTextDragDrop);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_UnresolvedLanguage_LogsDebugEvent2002()
	{
		var logger = new RecordingLogger();
		var options = new MarkdownRenderOptions { Logger = logger };

		MarkdownRenderer.CreateCodeBlockEditor(language: "definitely-not-a-language", code: "x = 1", theme: null, options);

		RecordingLogger.LogEntry entry = logger.Entries.Single();

		Assert.AreEqual(2002, entry.EventId.Id);
		Assert.AreEqual(LogLevel.Debug, entry.Level);
		Assert.IsTrue(entry.Message.Contains("definitely-not-a-language", StringComparison.Ordinal));
	}

	[TestMethod]
	public void CreateCodeBlockEditor_CustomHighlightingHook_IsInvoked()
	{
		bool invoked = false;

		MarkdownRenderer.CreateCodeBlockEditor(
			language: "lua",
			code: "x = 1",
			theme: null,

			customHighlightingInstaller: (editor, language) =>
			{
				invoked = true;
				Assert.AreEqual("lua", language);
				return false;
			}
		);

		Assert.IsTrue(invoked);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_DefaultOptions_DisablesTheHorizontalScrollBar()
	{
		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor("lua", "local value = 1");
		Assert.AreEqual(ScrollBarVisibility.Disabled, editor.HorizontalScrollBarVisibility);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_DefaultOptions_SuppressMouseSelectionInTheTextArea()
	{
		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor("lua", "local value = 1", MarkdownRenderTheme.Default);

		Assert.IsFalse(editor.TextArea.Focusable);
		Assert.IsFalse(editor.TextArea.IsTabStop);
		Assert.IsTrue(PressPrimaryButtonOn(editor), "Mouse selection must be suppressed by default.");
	}

	[TestMethod]
	public void CreateCodeBlockEditor_SelectionAllowed_DoesNotSuppressTheMouseDown()
	{
		var options = new MarkdownRenderOptions { AllowCodeBlockSelection = true };

		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor(
			language: "lua",
			code: "local value = 1",
			theme: MarkdownRenderTheme.Default,
			options
		);

		Assert.IsFalse(PressPrimaryButtonOn(editor), "An opt-in code block must not suppress the mouse-down.");
	}

	[TestMethod]
	public void CreateContent_CodeBlockFollowsSelectionOption()
	{
		var options = new MarkdownRenderOptions { AllowCodeBlockSelection = true };

		EditorControl editor = FindCodeBlockEditor(MarkdownRenderer.CreateContent("```lua\nx = 1\n```", MarkdownRenderTheme.Default, options));

		Assert.IsFalse(PressPrimaryButtonOn(editor));

		EditorControl passiveEditor = FindCodeBlockEditor(MarkdownRenderer.CreateContent("```lua\nx = 1\n```"));

		Assert.IsTrue(PressPrimaryButtonOn(passiveEditor));
	}

	[TestMethod]
	public void CreateCodeBlockEditor_TrailingTerminator_IsRemoved()
	{
		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor("lua", "a\nb\n");
		Assert.AreEqual("a\nb", editor.Text);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_TextWithoutTrailingBlankLines_IsUnchanged()
	{
		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor("lua", "a\nb");
		Assert.AreEqual("a\nb", editor.Text);
	}

	[DataRow("", DisplayName = "Empty")]
	[DataRow("   \n\n", DisplayName = "BlankLinesOnly")]
	[TestMethod]
	public void CreateCodeBlockEditor_BlankCode_NormalizesToEmptyText(string code)
	{
		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor("lua", code);
		Assert.AreEqual(string.Empty, editor.Text);
	}

	[TestMethod]
	public void CreateContent_ScrollingDisabled_CodeBlockDisablesScrollBar()
	{
		var options = new MarkdownRenderOptions { AllowScrolling = false };

		EditorControl editor = FindCodeBlockEditor(
			MarkdownRenderer.CreateContent("```lua\nlocal value = 1\n```", MarkdownRenderTheme.Default, options)
		);

		Assert.AreEqual(ScrollBarVisibility.Disabled, editor.VerticalScrollBarVisibility);
		Assert.IsGreaterThan(0.0, editor.MaxHeight, "A block still carries the visible-line clamp when scrolling is disabled.");
	}

	[TestMethod]
	public void CreateContent_TightListWithCodeBlock_SuppressesCodeBlockSpacing()
	{
		Border border = FindCodeBlockBorder(MarkdownRenderer.CreateContent("- one\n  ```\n  code\n  ```"));

		Assert.AreEqual(0.0, border.Margin.Top);
		Assert.AreEqual(0.0, border.Margin.Bottom);
	}

	[TestMethod]
	public void CreateContent_UnboundedSizeLimits_DoNotClampCodeBlocks()
	{
		MarkdownRenderTheme theme = MarkdownRenderTheme.Default with
		{
			MaxWidth = double.PositiveInfinity,
			MaxHeight = double.PositiveInfinity,
			CodeMaxWidth = double.PositiveInfinity,
		};

		Border border = FindCodeBlockBorder(MarkdownRenderer.CreateContent("```lua\nx = 1\n```", theme));

		Assert.IsTrue(double.IsPositiveInfinity(border.MaxWidth));
	}

	private EditorControl FindCodeBlockEditor(SharedControl content) => (EditorControl)FindCodeBlockBorder(content).Child!;
}
