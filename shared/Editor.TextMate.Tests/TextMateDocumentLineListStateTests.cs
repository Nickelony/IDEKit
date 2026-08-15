#if AVALONIAEDIT
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.TextMate.Highlighting;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using static Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests.TextMateThemeTestHelpers;
#else
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
using static Nickelony.IDEKit.AvalonEdit.TextMate.Tests.TextMateThemeTestHelpers;
#endif
using TextMateSharp.Model;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests;
#else
namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;
#endif

/// <summary>
/// Pins the tokenization-state invariants of whole-line deletions: the first preserved tail line must be
/// re-tokenized from the state that entered the deleted block, because nothing else in the changed region
/// is re-tokenized for a pure deletion.
/// </summary>
[TestClass]
public sealed class TextMateDocumentLineListStateTests
{
	[TestMethod]
	public void DocumentLineList_RemovingLeadingWholeLine_ReTokenizesFromTheInitialState()
	{
		// The first line opens a block comment, so the second line is tokenized inside it. Removing the
		// whole first line must re-tokenize the new first line from the grammar's initial state; keeping
		// the deleted line's end state would leave it marked as a comment.
		var document = new TextDocument("--[[\nx = 1\ny = 2");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			model.SetGrammar(CreateGrammar("lua"));
			WaitForTokenization(model, lineList);

			Assert.IsTrue(HasCommentScope(model, 1),
				"The precondition expects line 1 to be tokenized inside the block comment opened by line 0.");

			document.Remove(0, 5);

			WaitForTokenization(model, lineList);

			Assert.AreEqual("x = 1\n", TextMateDocumentLineListTests.GetSnapshotLines(lineList)[0], "The snapshot must follow the document geometry.");
			Assert.IsFalse(HasCommentScope(model, 0),
				"The new first line must be re-tokenized from the initial state after the whole leading line is removed.");
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_RemovingMiddleWholeLine_ReTokenizesTheTailFromTheKeptPrefix()
	{
		// Line 1 opens a block comment that makes line 2 a comment line. Removing the whole comment-opening
		// line must re-tokenize the new line 1 from the state after line 0, not from the state that followed
		// the deleted block.
		var document = new TextDocument("a = 1\n--[[\nb = 2\nc = 3");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			model.SetGrammar(CreateGrammar("lua"));
			WaitForTokenization(model, lineList);

			Assert.IsTrue(HasCommentScope(model, 2),
				"The precondition expects line 2 to be tokenized inside the block comment opened by line 1.");

			document.Remove(6, 5);

			WaitForTokenization(model, lineList);

			CollectionAssert.AreEqual(
				new[] { "a = 1\n", "b = 2\n", "c = 3" },
				TextMateDocumentLineListTests.GetSnapshotLines(lineList),
				TextMateDocumentLineListTests.DescribeSnapshot(lineList));
			Assert.IsFalse(HasCommentScope(model, 1),
				"The first preserved tail line must be re-tokenized from the state after the kept prefix.");
			Assert.IsFalse(HasCommentScope(model, 2),
				"The tail line after the re-tokenized line must follow its corrected end state.");
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	/// <summary>
	/// Determines whether any token of a line carries a comment scope.
	/// </summary>
	/// <param name="model">The model whose line is inspected.</param>
	/// <param name="lineIndex">The zero-based line index.</param>
	/// <returns><see langword="true"/> when a token scope starts with <c>comment</c>.</returns>
	private static bool HasCommentScope(TMModel model, int lineIndex)
	{
		List<TMToken>? tokens = model.GetLineTokens(lineIndex);

		return tokens is not null
			&& tokens.Any(token => token.Scopes.Any(scope => scope.StartsWith("comment", StringComparison.Ordinal)));
	}
}
