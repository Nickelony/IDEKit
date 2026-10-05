#if AVALONIAEDIT
using AvaloniaEdit.Highlighting;
using EditorControl = AvaloniaEdit.TextEditor;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;

#else
using ICSharpCode.AvalonEdit.Highlighting;
using EditorControl = ICSharpCode.AvalonEdit.TextEditor;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

#endif

public abstract partial class MarkdownRendererTestsBase
{
	[TestMethod]
	public void CreateContent_LanguageTokens_ArePassedToTheHighlightingHook()
	{
		var languages = new List<string?>();

		MarkdownRenderer.CreateContent(
			"```\ncode\n```\n\n    indented code",
			MarkdownRenderTheme.Default,

			customHighlightingInstaller: (_, language) =>
			{
				languages.Add(language);
				return false;
			}
		);

		// A bare fence carries an empty language token, while an indented block carries none at all.
		Assert.AreSequenceEqual([string.Empty, null], languages);
	}

	[TestMethod]
	public void CreateContent_CustomHighlightingHook_IsInvokedForFencedCode()
	{
		bool invoked = false;

		MarkdownRenderer.CreateContent(
			"```lua\nx = 1\n```",
			MarkdownRenderTheme.Default,

			customHighlightingInstaller: (editor, language) =>
			{
				invoked = true;
				return false;
			}
		);

		Assert.IsTrue(invoked);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_AliasMapWithOrdinalComparer_ResolvesCaseInsensitively()
	{
		var options = new MarkdownRenderOptions
		{
			HighlightingAliases = new Dictionary<string, string>(StringComparer.Ordinal) { ["MyLang"] = ".cs" },
		};

		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor(language: "mylang", code: "x = 1", theme: MarkdownRenderTheme.Default, options);

		// The options copy normalizes the map to case-insensitive keys, so a fence whose casing differs
		// from the assigned key still resolves.
		Assert.IsNotNull(editor.SyntaxHighlighting);
		Assert.AreSame(HighlightingManager.Instance.GetDefinitionByExtension(".cs"), editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_TypeScriptLanguage_ResolvesNoHighlighting()
	{
		// The editors ship no TypeScript definition, and the default aliases add no lookup that could
		// succeed; hosts must register one through the custom highlighting hook.
		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor("ts", "let x = 1", MarkdownRenderTheme.Default);
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
		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor(language, "x = 1", MarkdownRenderTheme.Default);

		Assert.IsNotNull(editor.SyntaxHighlighting, $"The language '{language}' did not resolve.");

		Assert.AreSame(
			HighlightingManager.Instance.GetDefinitionByExtension(extension),
			editor.SyntaxHighlighting,
			$"The language '{language}' resolved to an unexpected definition."
		);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_UnresolvedLanguage_ReturnsNullHighlighting()
	{
		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor("definitely-not-a-language", "x = 1", MarkdownRenderTheme.Default);
		Assert.IsNull(editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_CustomHighlightingHookHandled_SkipsBuiltInResolution()
	{
		IHighlightingDefinition jsonDefinition = HighlightingManager.Instance.GetDefinition("Json")!;

		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor(
			language: "csharp",
			code: "x = 1",
			theme: MarkdownRenderTheme.Default,

			customHighlightingInstaller: (installed, _) =>
			{
				installed.SyntaxHighlighting = jsonDefinition;
				return true;
			}
		);

		Assert.AreSame(jsonDefinition, editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateContent_FenceInfoWithAttributes_ResolvesLanguageToken()
	{
		EditorControl editor = FindCodeBlockEditor(MarkdownRenderer.CreateContent("```cs title=\"sample\"\nvar x = 1;\n```"));
		Assert.IsNotNull(editor.SyntaxHighlighting, "The first info-string token must resolve the language.");
	}

	[TestMethod]
	public void CreateCodeBlockEditor_HookDeclines_UsesBuiltInResolution()
	{
		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor(
			language: "cs",
			code: "x = 1",
			theme: MarkdownRenderTheme.Default,
			customHighlightingInstaller: (_, _) => false
		);

		Assert.AreSame(HighlightingManager.Instance.GetDefinitionByExtension(".cs"), editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_HookThrows_PropagatesToTheCaller()
	{
		Assert.ThrowsExactly<InvalidOperationException>(() =>
			MarkdownRenderer.CreateCodeBlockEditor(
				language: "lua",
				code: "x = 1",
				theme: MarkdownRenderTheme.Default,
				customHighlightingInstaller: (_, _) => throw new InvalidOperationException("highlighting failure")
			)
		);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_NullLanguage_ReturnsEditorWithoutHighlightingOrDiagnostics()
	{
		var logger = new RecordingLogger();
		var options = new MarkdownRenderOptions { Logger = logger };

		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor(language: null, code: "x = 1", theme: MarkdownRenderTheme.Default, options);

		Assert.IsNull(editor.SyntaxHighlighting);
		Assert.IsEmpty(logger.Entries, "A block without a language is not an unresolved language.");
	}

	[TestMethod]
	public void CreateCodeBlockEditor_BlankAliasValue_IsIgnored()
	{
		var options = new MarkdownRenderOptions { HighlightingAliases = new Dictionary<string, string> { ["cs"] = "   " } };

		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor(language: "cs", code: "x = 1", theme: MarkdownRenderTheme.Default, options);

		// The blank alias is ignored, so the language falls back to the built-in extension resolution;
		// were it used, the blank alias would resolve nothing.
		Assert.AreSame(HighlightingManager.Instance.GetDefinitionByExtension(".cs"), editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_AliasWithoutExtension_ResolvesDefinitionName()
	{
		var options = new MarkdownRenderOptions { HighlightingAliases = new Dictionary<string, string> { ["probelang"] = "C#" } };

		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor(
			language: "probelang",
			code: "x = 1",
			theme: MarkdownRenderTheme.Default,
			options
		);

		Assert.AreSame(HighlightingManager.Instance.GetDefinition("C#"), editor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateContent_HostAlias_ResolvesFencedLanguage()
	{
		var options = new MarkdownRenderOptions { HighlightingAliases = new Dictionary<string, string> { ["probelang"] = ".cs" } };

		EditorControl editor = FindCodeBlockEditor(
			MarkdownRenderer.CreateContent("```probelang\nvar x = 1;\n```", MarkdownRenderTheme.Default, options)
		);

		Assert.AreSame(HighlightingManager.Instance.GetDefinitionByExtension(".cs"), editor.SyntaxHighlighting);
	}
}
