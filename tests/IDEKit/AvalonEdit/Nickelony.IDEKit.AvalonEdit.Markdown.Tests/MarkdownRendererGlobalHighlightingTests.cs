using ICSharpCode.AvalonEdit.Highlighting;
using AvalonTextEditor = ICSharpCode.AvalonEdit.TextEditor;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

/// <summary>
/// Pins the resolution of host-registered highlighting definitions for Markdown code blocks.
/// </summary>
/// <remarks>
/// These tests register definitions on <see cref="HighlightingManager.Instance"/>, which is process-global
/// and has no unregister API, so a registration made here stays visible for the rest of the test process.
/// They therefore live in their own class, kept out of the parallel workload with <c>[DoNotParallelize]</c>,
/// so no suite that runs alongside them can start resolving the registered tokens. The probe names and
/// extensions are unique to this suite, so no other suite references them and the residual registration is
/// unobservable elsewhere.
/// </remarks>
[STATestClass]
[DoNotParallelize]
public sealed class MarkdownRendererGlobalHighlightingTests
{
	[TestMethod]
	public void CreateCodeBlockEditor_HostRegisteredDefinition_ResolvesByName()
	{
		IHighlightingDefinition csharpDefinition = HighlightingManager.Instance.GetDefinition("C#")!;

		// Simulate a host definition for a language AvalonEdit does not ship (for example Lua).
		HighlightingManager.Instance.RegisterHighlighting("ProbeLua", [".probelua"], csharpDefinition);

		AvalonTextEditor editor = MarkdownRenderer.CreateCodeBlockEditor("ProbeLua", "x = 1", MarkdownRenderTheme.Default);

		Assert.AreSame(csharpDefinition, editor.SyntaxHighlighting);

		// A different casing resolves through the case-insensitive definition-name scan.
		AvalonTextEditor caseVariantEditor = MarkdownRenderer.CreateCodeBlockEditor("PROBELUA", "x = 1", MarkdownRenderTheme.Default);

		Assert.AreSame(csharpDefinition, caseVariantEditor.SyntaxHighlighting);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_HostRegisteredExtension_ResolvesCaseInsensitively()
	{
		IHighlightingDefinition csharpDefinition = HighlightingManager.Instance.GetDefinition("C#")!;

		HighlightingManager.Instance.RegisterHighlighting("ProbeExtension", [".probelng"], csharpDefinition);

		// ".PROBELNG" matches neither the definition name nor its case-insensitive scan, so the token
		// reaches the extension dictionary, which AvalonEdit compares case-insensitively.
		AvalonTextEditor editor = MarkdownRenderer.CreateCodeBlockEditor(".PROBELNG", "x = 1", MarkdownRenderTheme.Default);

		Assert.AreSame(csharpDefinition, editor.SyntaxHighlighting);
	}
}
