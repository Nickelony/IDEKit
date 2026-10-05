using System.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
#if AVALONIAEDIT
using AvaloniaEdit.Document;

namespace Nickelony.Samples.Avalonia.Tests;

#else
using ICSharpCode.AvalonEdit.Document;

namespace Nickelony.Samples.Wpf.Tests;

#endif

[TestClass]
public sealed class CodeScopeTrackerTests
{
	[TestMethod]
	[DataRow("local name = pri", CompletionScopes.Code)]
	[DataRow("-- a comment pri", CompletionScopes.Comment)]
	[DataRow("print(\"a string pri", CompletionScopes.StringLiteral)]
	[DataRow("--[[ a block comment\nover two lines pri", CompletionScopes.Comment)]
	[DataRow("--[[ a closed block comment ]]\nlocal name = pri", CompletionScopes.Code)]
	[DataRow("local text = [[a long string\nover two lines pri", CompletionScopes.StringLiteral)]
	[DataRow("local text = [[a long string]] -- and a comment\nlocal name = pri", CompletionScopes.Code)]
	public void ScopeAt_TheEndOfTheText_TellsTheScope(string text, CompletionScopes expected)
	{
		using CodeScopeTracker tracker = CreateLua(text, out TextDocument document);
		Assert.AreEqual(expected, tracker.ScopeAt(document.TextLength));
	}

	[TestMethod]
	public void ScopeAt_ABlockCommentOpenedFarAbove_IsKnownOnTheLastLine()
	{
		var text = new StringBuilder("--[[ opened here\n");

		for (int line = 0; line < 2000; line++)
			text.Append("still inside the comment\n");

		using CodeScopeTracker tracker = CreateLua(text + "pri", out TextDocument document);

		Assert.AreEqual(CompletionScopes.Comment, tracker.ScopeAt(document.TextLength));
	}

	[TestMethod]
	public void ScopeAt_AfterALongBlockCommentCloses_IsCode()
	{
		var text = new StringBuilder("--[[ opened here\n");

		for (int line = 0; line < 2000; line++)
			text.Append("still inside the comment\n");

		using CodeScopeTracker tracker = CreateLua(text + "]]\nlocal name = pri", out TextDocument document);

		Assert.AreEqual(CompletionScopes.Code, tracker.ScopeAt(document.TextLength));
	}

	[TestMethod]
	public void ScopeAt_AfterAnEditOpensACommentAbove_FollowsTheEdit()
	{
		using CodeScopeTracker tracker = CreateLua("local a = 1\nlocal b = 2\nlocal name = pri", out TextDocument document);

		Assert.AreEqual(CompletionScopes.Code, tracker.ScopeAt(document.TextLength));

		document.Insert(0, "--[[ ");

		Assert.AreEqual(CompletionScopes.Comment, tracker.ScopeAt(document.TextLength));
	}

	private static CodeScopeTracker CreateLua(string text, out TextDocument document)
	{
		document = new TextDocument(text);

		return new CodeScopeTracker(
			document,
			SampleEditingFeatures.CodeSyntaxOf(SampleDocumentKind.Lua) ?? throw new InvalidOperationException("Lua has a code syntax.")
		);
	}
}
