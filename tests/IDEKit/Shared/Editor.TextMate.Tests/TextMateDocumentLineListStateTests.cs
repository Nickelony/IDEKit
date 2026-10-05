using System.Text;
using TextMateSharp.Model;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
using static Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests.TextMateRunStyleTestHelpers;
#else
using ICSharpCode.AvalonEdit.Document;
using static Nickelony.IDEKit.AvalonEdit.TextMate.Tests.TextMateRunStyleTestHelpers;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests;

#else

namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;

#endif

/// <summary>
/// Pins the tokenization-state invariants of whole-line deletions: the first preserved tail line must be re-tokenized from the state that entered
/// the deleted block, because nothing else in the changed region is re-tokenized for a pure deletion.
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

			Assert.IsTrue(HasCommentScope(model, 1), "The precondition expects line 1 to be tokenized inside the block comment opened by line 0.");

			document.Remove(0, 5);

			WaitForTokenization(model, lineList);

			Assert.AreEqual(
				"x = 1\n",
				TextMateDocumentLineListTests.GetSnapshotLines(lineList)[0],
				"The snapshot must follow the document geometry."
			);

			Assert.IsFalse(
				HasCommentScope(model, 0),
				"The new first line must be re-tokenized from the initial state after the whole leading line is removed."
			);
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	private static readonly string[] expected = ["a = 1\n", "b = 2\n", "c = 3\n"];

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

			Assert.IsTrue(HasCommentScope(model, 2), "The precondition expects line 2 to be tokenized inside the block comment opened by line 1.");

			document.Remove(6, 5);

			WaitForTokenization(model, lineList);

			Assert.AreSequenceEqual(
				expected,
				TextMateDocumentLineListTests.GetSnapshotLines(lineList),
				TextMateDocumentLineListTests.DescribeSnapshot(lineList)
			);

			Assert.IsFalse(HasCommentScope(model, 1), "The first preserved tail line must be re-tokenized from the state after the kept prefix.");
			Assert.IsFalse(HasCommentScope(model, 2), "The tail line after the re-tokenized line must follow its corrected end state.");
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_RandomizedEditsOfMixedTerminators_TokenizeLikeAFreshModel()
	{
		string[] pieces = ["--[[", "]]", "[[", "x = 1", "\"", "local ", "--", " ", "\r\n", "\n", "\r", "end", "", "\\"];

		foreach (int seed in Enumerable.Range(1, 8))
		{
			var random = new Random(seed);
			var document = new TextDocument(RandomLuaText(random, pieces, 12));
			var lineList = new TextMateDocumentLineList(document);
			var model = new TMModel(lineList);

			try
			{
				model.SetGrammar(CreateGrammar("lua"));

				// The wait attaches the listener that keeps the model's tokenizer running through the edits.
				WaitForTokenization(model, lineList);

				for (int edit = 0; edit < 40; edit++)
				{
					int offset = random.Next(document.TextLength + 1);

					// Half of the edits start right after a carriage return, where an insertion can land between the two halves of a CRLF pair.
					if (random.Next(2) == 0)
					{
						int[] afterCarriageReturns =
						[
							.. Enumerable.Range(0, document.TextLength).Where(i => document.GetCharAt(i) == '\r').Select(i => i + 1),
						];

						if (afterCarriageReturns.Length > 0)
							offset = afterCarriageReturns[random.Next(afterCarriageReturns.Length)];
					}

					int removalLength = Math.Min(random.Next(5), document.TextLength - offset);

					document.Replace(offset, removalLength, RandomLuaText(random, pieces, 3));

					// Each edit settles before the next, so the comparison does not read tokens while the model's tokenizer thread is still walking.
					WaitForTokenization(model, lineList);
					AssertSameTokensAsAFreshModel(document, model, $"seed {seed}, edit {edit}");
				}
			}
			finally
			{
				model.Dispose();
				lineList.Dispose();
			}
		}
	}

	[TestMethod]
	public void DocumentLineList_InsertionBetweenCrAndLf_KeepsTheStateThatEntersTheNewLine()
	{
		// The insertion adds a line without removing one, because the line the carriage return ends keeps its text.
		var document = new TextDocument("--[[\r\nb]]\r\nc");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			model.SetGrammar(CreateGrammar("lua"));
			WaitForTokenization(model, lineList);

			document.Insert(5, "x");

			WaitForTokenization(model, lineList);

			Assert.IsTrue(HasCommentScope(model, 1), "The new line follows the comment opened by the first line.");
			AssertSameTokensAsAFreshModel(document, model, "insertion between CR and LF");
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_UndoOfADeletionBetweenLoneCrAndLf_KeepsTheStateThatEntersTheRestoredLine()
	{
		var document = new TextDocument("--[[\rx\nb]] y");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			model.SetGrammar(CreateGrammar("lua"));
			WaitForTokenization(model, lineList);

			document.Remove(5, 1);
			WaitForTokenization(model, lineList);

			document.UndoStack.Undo();
			WaitForTokenization(model, lineList);

			Assert.AreEqual("--[[\rx\nb]] y", document.Text);
			AssertSameTokensAsAFreshModel(document, model, "undo of a deletion between CR and LF");
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	private static string RandomLuaText(Random random, string[] pieces, int maxPieces)
	{
		var builder = new StringBuilder();

		for (int i = random.Next(0, maxPieces + 1); i > 0; i--)
			builder.Append(pieces[random.Next(pieces.Length)]);

		return builder.ToString();
	}

	private static void AssertSameTokensAsAFreshModel(TextDocument document, TMModel editedModel, string context)
	{
		var freshDocument = new TextDocument(document.Text);
		var freshLineList = new TextMateDocumentLineList(freshDocument);
		var freshModel = new TMModel(freshLineList);

		try
		{
			freshModel.SetGrammar(CreateGrammar("lua"));
			WaitForTokenization(freshModel, freshLineList);

			for (int lineIndex = 0; lineIndex < document.LineCount; lineIndex++)
			{
				Assert.AreSequenceEqual(
					DescribeTokens(freshModel, lineIndex),
					DescribeTokens(editedModel, lineIndex),
					$"{context}: line {lineIndex} of {document.LineCount}"
				);
			}
		}
		finally
		{
			freshModel.Dispose();
			freshLineList.Dispose();
		}
	}

	private static string[] DescribeTokens(TMModel model, int lineIndex) =>
		[.. model.GetLineTokens(lineIndex).Select(token => $"{token.StartIndex}:{string.Join(' ', token.Scopes)}")];

	/// <summary>Determines whether any token of a line carries a comment scope.</summary>
	/// <param name="model">The model whose line is inspected.</param>
	/// <param name="lineIndex">The zero-based line index.</param>
	/// <returns><see langword="true"/> when a token scope starts with <c>comment</c>.</returns>
	private static bool HasCommentScope(TMModel model, int lineIndex)
	{
		List<TMToken>? tokens = model.GetLineTokens(lineIndex);
		return tokens is not null && tokens.Any(token => token.Scopes.Any(scope => scope.StartsWith("comment", StringComparison.Ordinal)));
	}
}
