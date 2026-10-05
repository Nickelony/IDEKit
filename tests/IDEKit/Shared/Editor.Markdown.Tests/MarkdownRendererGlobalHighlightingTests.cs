#if AVALONIAEDIT
using AvaloniaEdit.Highlighting;
using EditorControl = AvaloniaEdit.TextEditor;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;

#else
using ICSharpCode.AvalonEdit.Highlighting;
using EditorControl = ICSharpCode.AvalonEdit.TextEditor;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

#endif

/// <summary>Pins the resolution of host-registered highlighting definitions for Markdown code blocks.</summary>
/// <remarks>
/// These tests register definitions on <see cref="HighlightingManager.Instance"/>, which is process-global
/// and has no unregister API, so a registration made here stays visible for the rest of the test process. They therefore live in their own class,
/// marked <c>[DoNotParallelize]</c> so that enabling assembly-level parallelization later cannot let a suite that runs alongside them resolve
/// the registered tokens.
/// </remarks>
[STATestClass]
[DoNotParallelize]
public sealed class MarkdownRendererGlobalHighlightingTests
{
	[TestMethod]
	public void CreateCodeBlockEditor_HostRegisteredDefinition_ResolvesByName()
	{
		IHighlightingDefinition csharpDefinition = HighlightingManager.Instance.GetDefinition("C#")!;

		// Simulate a host definition for a language the engine does not ship (for example Lua).
		HighlightingManager.Instance.RegisterHighlighting("ProbeLua", [".probelua"], csharpDefinition);

		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor("ProbeLua", "x = 1", MarkdownRenderTheme.Default);

		Assert.AreSame(csharpDefinition, editor.SyntaxHighlighting);

		// A different casing resolves through the case-insensitive definition-name scan.
		EditorControl caseVariantEditor = MarkdownRenderer.CreateCodeBlockEditor("PROBELUA", "x = 1", MarkdownRenderTheme.Default);

		Assert.AreSame(csharpDefinition, caseVariantEditor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_HostRegisteredExtension_ResolvesCaseInsensitively()
	{
		IHighlightingDefinition csharpDefinition = HighlightingManager.Instance.GetDefinition("C#")!;

		HighlightingManager.Instance.RegisterHighlighting("ProbeExtension", [".probelng"], csharpDefinition);

		// ".PROBELNG" matches neither the definition name nor its case-insensitive scan, so the token
		// reaches the extension dictionary, which the engine compares case-insensitively.
		EditorControl editor = MarkdownRenderer.CreateCodeBlockEditor(".PROBELNG", "x = 1", MarkdownRenderTheme.Default);

		Assert.AreSame(csharpDefinition, editor.SyntaxHighlighting);
	}
}
