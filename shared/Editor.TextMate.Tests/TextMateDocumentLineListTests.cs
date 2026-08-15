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
using System.Text;
using TextMateSharp.Grammars;
using TextMateSharp.Model;
using TextMateSharp.Registry;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests;
#else
namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;
#endif

[TestClass]
public sealed class TextMateDocumentLineListTests
{
	[TestMethod]
	[DataRow("a\r\nb", 2, 1, null, "a\r|b", false, DisplayName = "RemoveLfFromCrLfPair")]
	[DataRow("a\r\nb", 1, 1, null, "a\n|b", false, DisplayName = "RemoveCrFromCrLfPair")]
	[DataRow("a\nb", 1, 0, "\r", "a\r\n|b", false, DisplayName = "InsertCrBeforeLf")]
	[DataRow("a\r\nb", 2, 1, "\r", "a\r|\r|b", false, DisplayName = "ReplaceLfWithCr")]
	[DataRow("a\nb", 1, 1, null, "ab", false, DisplayName = "RemoveWholeTerminator")]
	[DataRow("alpha\r\nbeta", 5, 2, "\n", "alpha\n|beta", false, DisplayName = "CollapseCrLfToLf")]
	[DataRow("alpha\nbeta", 5, 1, "\r\n", "alpha\r\n|beta", false, DisplayName = "ExpandLfToCrLf")]
	[DataRow("ab", 2, 0, "\r\n", "ab\r\n|", false, DisplayName = "AppendCrLfAtEnd")]
	[DataRow("a\rb", 2, 0, "\n", "a\r\n|b", false, DisplayName = "InsertLfAfterLoneCr")]
	[DataRow("\r\na", 0, 1, null, "\n|a", false, DisplayName = "RemoveLeadingCrOfCrLfPair")]
	[DataRow("a\n\r\nb", 2, 1, null, "a\n|\n|b", false, DisplayName = "RemoveCrOfLineStartingCrLfPair")]
	[DataRow("a\r\r\nb", 2, 1, null, "a\r\n|b", false, DisplayName = "RemoveCrBeforeCrLfPair")]
	[DataRow("\r\r\nb", 1, 1, "\n", "\r\n|\n|b", false, DisplayName = "ReplaceCrWithLfBeforeLf")]
	[DataRow("a\rb", 1, 1, "\n", "a\n|b", false, DisplayName = "ReplaceCrWithLf")]
	[DataRow("a\r\nb", 2, 1, null, "a\r|b", true, DisplayName = "RemoveLfFromCrLfPairWithModel")]
	[DataRow("a\r\nb", 1, 1, null, "a\n|b", true, DisplayName = "RemoveCrFromCrLfPairWithModel")]
	[DataRow("a\nb", 1, 0, "\r", "a\r\n|b", true, DisplayName = "InsertCrBeforeLfWithModel")]
	[DataRow("a\r\nb", 2, 1, "\r", "a\r|\r|b", true, DisplayName = "ReplaceLfWithCrWithModel")]
	[DataRow("a\rb", 2, 0, "\n", "a\r\n|b", true, DisplayName = "InsertLfAfterLoneCrWithModel")]
	[DataRow("\r\na", 0, 1, null, "\n|a", true, DisplayName = "RemoveLeadingCrOfCrLfPairWithModel")]
	public void DocumentLineList_NewlineBoundaryEdits_KeepSnapshotInSync(
		string originalText,
		int offset,
		int removalLength,
		string? insertedText,
		string expectedLines,
		bool withModel)
	{
		var document = new TextDocument(originalText);
		var lineList = new TextMateDocumentLineList(document);

		// The model variant additionally exercises the slot reconciliation against the model's
		// line-state list; both variants must produce the same snapshot.
		TMModel? model = withModel ? new TMModel(lineList) : null;

		try
		{
			if (removalLength == 0)
				document.Insert(offset, insertedText ?? string.Empty);
			else if (insertedText is null)
				document.Remove(offset, removalLength);
			else
				document.Replace(offset, removalLength, insertedText);

			Assert.AreEqual(document.LineCount, lineList.GetNumberOfLines());
			CollectionAssert.AreEqual(expectedLines.Split('|'), GetSnapshotLines(lineList), DescribeSnapshot(lineList));
		}
		finally
		{
			model?.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_CrLfPairEdit_InvalidatesOnlyTheChangedLine()
	{
		var document = new TextDocument("a\r\nb");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			for (int i = 0; i < lineList.GetNumberOfLines(); i++)
				lineList.Get(i).IsInvalid = false;

			// Remove the line feed of the CRLF pair: only the first line's terminator changes, so only
			// the first line must be re-tokenized.
			document.Remove(2, 1);

			Assert.IsTrue(lineList.Get(0).IsInvalid);
			Assert.IsFalse(lineList.Get(1).IsInvalid);
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_InsertLfAfterLoneCr_InvalidatesTheLineWhoseTerminatorChanged()
	{
		var document = new TextDocument("a\rb");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			for (int i = 0; i < lineList.GetNumberOfLines(); i++)
				lineList.Get(i).IsInvalid = false;

			// Inserting the line feed after the lone carriage return turns it into a CRLF pair, changing
			// the first line's terminator even though the change starts at the second line's boundary, so
			// the first line must be re-tokenized and the still-separate second line must not be.
			document.Insert(2, "\n");

			Assert.IsTrue(lineList.Get(0).IsInvalid);
			Assert.IsFalse(lineList.Get(1).IsInvalid);
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_EditAfterCrLfPairEdit_DoesNotPoisonLaterEdits()
	{
		var document = new TextDocument("a\r\nb");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			// Removing the carriage return of the CRLF pair leaves a lone line feed between the first
			// line's text and the second line; a following whole-document replacement must still succeed.
			document.Remove(1, 1);
			document.Text = "x";

			Assert.AreEqual(document.LineCount, lineList.GetNumberOfLines());
			CollectionAssert.AreEqual(new[] { "x" }, GetSnapshotLines(lineList));
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_WholeTextAssignment_KeepsSnapshotInSync()
	{
		var document = new TextDocument("a\r\nb\r\nc");
		var lineList = new TextMateDocumentLineList(document);

		try
		{
			document.Text = "alpha\nbeta";
			Assert.AreEqual(document.LineCount, lineList.GetNumberOfLines());
			CollectionAssert.AreEqual(new[] { "alpha\n", "beta" }, GetSnapshotLines(lineList));

			document.Text = document.Text.Replace("\n", "\r\n", StringComparison.Ordinal);
			Assert.AreEqual(document.LineCount, lineList.GetNumberOfLines());
			CollectionAssert.AreEqual(new[] { "alpha\r\n", "beta" }, GetSnapshotLines(lineList));

			document.Text = "x\r\n";
			Assert.AreEqual(document.LineCount, lineList.GetNumberOfLines());
			CollectionAssert.AreEqual(new[] { "x\r\n", "" }, GetSnapshotLines(lineList));

			document.Text = string.Empty;
			Assert.AreEqual(1, lineList.GetNumberOfLines());
			CollectionAssert.AreEqual(new[] { "" }, GetSnapshotLines(lineList));
		}
		finally
		{
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_MultiLineRemoval_KeepsSnapshotInSync()
	{
		var document = new TextDocument("alpha\r\nbeta\r\ngamma");
		var lineList = new TextMateDocumentLineList(document);

		try
		{
			document.Remove(7, "beta\r\ngamma".Length);

			Assert.AreEqual(document.LineCount, lineList.GetNumberOfLines());
			CollectionAssert.AreEqual(new[] { "alpha\r\n", "" }, GetSnapshotLines(lineList));
		}
		finally
		{
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_TracksInsertedAndReplacedLines()
	{
		var document = new TextDocument("alpha\r\nbeta");
		var lineList = new TextMateDocumentLineList(document);

		try
		{
			CollectionAssert.AreEqual(new[] { "alpha\r\n", "beta" }, GetSnapshotLines(lineList));

			document.Insert(document.TextLength, "\r\ngamma");

			CollectionAssert.AreEqual(new[] { "alpha\r\n", "beta\r\n", "gamma" }, GetSnapshotLines(lineList));
			Assert.AreEqual(3, lineList.GetNumberOfLines());

			int replacementOffset = document.Text.IndexOf("beta\r\ngamma", StringComparison.Ordinal);
			document.Replace(replacementOffset, "beta\r\ngamma".Length, "delta");

			CollectionAssert.AreEqual(new[] { "alpha\r\n", "delta" }, GetSnapshotLines(lineList));
			Assert.AreEqual(2, lineList.GetNumberOfLines());
		}
		finally
		{
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_MultiLineReplacement_InvalidatesWholeChangedRegion()
	{
		var document = new TextDocument("a\nb\nc\nd\ne");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			// Simulate a fully tokenized document, then replace two lines with three.
			for (int i = 0; i < lineList.GetNumberOfLines(); i++)
				lineList.Get(i).IsInvalid = false;

			document.Replace(2, "b\nc".Length, "B\nC\nD");

			// The whole inserted region must be marked for re-tokenization: a new line can land on the
			// slot of a removed line, and when its predecessor's end state coincides with the stale end
			// state, the model's forward walk alone would keep that line's stale tokens.
			Assert.IsTrue(lineList.Get(1).IsInvalid);
			Assert.IsTrue(lineList.Get(2).IsInvalid);
			Assert.IsTrue(lineList.Get(3).IsInvalid);

			// Lines outside the changed region keep their tokenization state.
			Assert.IsFalse(lineList.Get(0).IsInvalid);
			Assert.IsFalse(lineList.Get(4).IsInvalid);
			Assert.IsFalse(lineList.Get(5).IsInvalid);
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_MultiLineReplacement_RetokenizesReplacementLine()
	{
		var document = new TextDocument("x = 1\ny = 2\n-- old comment\nz = 3\ne = 4");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
			model.SetGrammar(new Registry(registryOptions).LoadGrammar(registryOptions.GetScopeByLanguageId("lua")));

			// Wait for the model's background pass; the test must not drive TextMateSharp's tokenizer
			// from this thread while the model's tokenizer thread runs.
			WaitForTokenization(model, lineList);

			// Replace two lines with three. The critical slot is the third replacement line: it lands on
			// the slot that previously described the removed comment line, and the preceding line ends in
			// the same state, so a model that only invalidates the first changed line can leave the stale
			// comment tokens in place. (The invalid-flag regression test pins the same defect without
			// depending on tokenizer timing.)
			document.Replace(6, "y = 2\n-- old comment".Length, "Y = 2\nC = 3\nlocal d = 1");

			WaitForTokenization(model, lineList);

			List<string> scopes = GetLineScopes(model, 3);

			Assert.IsTrue(
				scopes.Any(scope => scope.Contains("keyword", StringComparison.Ordinal)),
				"Expected the third replacement line to be tokenized as code.");
			Assert.IsFalse(
				scopes.Any(scope => scope.Contains("comment", StringComparison.Ordinal)),
				"The third replacement line must not keep tokens from the removed comment line.");
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_Undo_RestoresSnapshot()
	{
		var document = new TextDocument("alpha\r\nbeta");
		var lineList = new TextMateDocumentLineList(document);

		try
		{
			document.Insert(document.TextLength, "\r\ngamma");
			Assert.AreEqual(3, lineList.GetNumberOfLines());

			document.UndoStack.Undo();

			CollectionAssert.AreEqual(new[] { "alpha\r\n", "beta" }, GetSnapshotLines(lineList));
			Assert.AreEqual(2, lineList.GetNumberOfLines());
		}
		finally
		{
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_LeadingCrLfSplit_FollowUpEdit_KeepsSnapshotInSync()
	{
		var document = new TextDocument();
		var lineList = new TextMateDocumentLineList(document);

		try
		{
			// The inserted text ends with a CRLF pair; the second edit removes the two line feeds and
			// the carriage return that precede the pair's line feed, leaving that line feed alone at
			// the start of the document.
			document.Insert(0, "\n\n\r\na");
			document.Remove(0, 3);

			Assert.AreEqual(document.LineCount, lineList.GetNumberOfLines());
			CollectionAssert.AreEqual(new[] { "\n", "a" }, GetSnapshotLines(lineList), DescribeSnapshot(lineList));
		}
		finally
		{
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_OutOfRangeReads_ReturnEmptyTextAndZeroLength()
	{
		var document = new TextDocument("alpha\nbeta");
		var lineList = new TextMateDocumentLineList(document);

		try
		{
			// The tokenizer can probe an index that a concurrent document update has already removed;
			// the reads tolerate it so the failed line is re-tokenized by a later pass.
			int lineCount = lineList.GetNumberOfLines();

			Assert.AreEqual(string.Empty, lineList.GetLineTextIncludingTerminators(-1).ToString());
			Assert.AreEqual(string.Empty, lineList.GetLineTextIncludingTerminators(lineCount).ToString());
			Assert.AreEqual(0, lineList.GetLineLength(-1));
			Assert.AreEqual(0, lineList.GetLineLength(lineCount));
		}
		finally
		{
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_RandomizedLineBoundaryEdits_KeepSnapshotInSync()
	{
		foreach (int seed in new[] { 20260920, 7, 1234, 987654 })
		{
			var random = new Random(seed);
			var document = new TextDocument(RandomDocumentText(random, 0, 24));
			var lineList = new TextMateDocumentLineList(document);
			TMModel? model = (seed & 1) == 0 ? new TMModel(lineList) : null;

			try
			{
				for (int edit = 0; edit < 200; edit++)
				{
					ApplyRandomEdit(document, random);
					AssertSnapshotInSync(document, lineList, $"seed {seed}, edit {edit}");
				}
			}
			finally
			{
				model?.Dispose();
				lineList.Dispose();
			}
		}
	}

	[TestMethod]
	public void DocumentLineList_UpdateLine_MarksTheLineInvalidWithoutRereadingIt()
	{
		var document = new TextDocument("alpha\nbeta");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			for (int i = 0; i < lineList.GetNumberOfLines(); i++)
				lineList.Get(i).IsInvalid = false;

			// The snapshot is always current, so the base contract's update only marks the line for
			// re-tokenization; it never re-reads the document.
			lineList.UpdateLine(1);

			Assert.IsTrue(lineList.Get(1).IsInvalid);
			Assert.IsFalse(lineList.Get(0).IsInvalid);
			Assert.AreEqual("alpha\n", lineList.GetLineTextIncludingTerminators(0).ToString());
			Assert.AreEqual("beta", lineList.GetLineTextIncludingTerminators(1).ToString());
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_Dispose_IsIdempotentAndStopsTracking()
	{
		var document = new TextDocument("alpha\nbeta");
		var lineList = new TextMateDocumentLineList(document);

		lineList.Dispose();
		lineList.Dispose();

		document.Insert(0, "x\n");

		// Disposing only detaches the change handler: the snapshot keeps the state it tracked before.
		Assert.AreEqual(2, lineList.GetNumberOfLines());
		CollectionAssert.AreEqual(new[] { "alpha\n", "beta" }, GetSnapshotLines(lineList));
	}

	private static List<string> GetLineScopes(TMModel model, int lineIndex)
	{
		List<TMToken> tokens = model.GetLineTokens(lineIndex)
			?? throw new AssertFailedException($"Line {lineIndex} has no tokens.");

		return [.. tokens.SelectMany(token => token.Scopes).Distinct(StringComparer.Ordinal)];
	}

	internal static string[] GetSnapshotLines(TextMateDocumentLineList lineList)
		=> [.. Enumerable.Range(0, lineList.GetNumberOfLines())
			.Select(index => lineList.GetLineTextIncludingTerminators(index).ToString())];

	internal static string DescribeSnapshot(TextMateDocumentLineList lineList)
		=> "snapshot=[" + string.Join(
			", ",
			GetSnapshotLines(lineList).Select(static line => "'" + line.Replace("\r", "\\r").Replace("\n", "\\n") + "'")) + "]";

	private static void ApplyRandomEdit(TextDocument document, Random random)
	{
		int length = document.TextLength;
		int offset = random.Next(length + 1);
		int removalLength = Math.Min(random.Next(4), length - offset);
		string insertedText = random.Next(5) == 0 ? string.Empty : RandomDocumentText(random, 0, 4);

		if (removalLength == 0 && insertedText.Length == 0)
			insertedText = "z";

		document.Replace(offset, removalLength, insertedText);
	}

	private static string RandomDocumentText(Random random, int minPieces, int maxPieces)
	{
		string[] pieces = ["a", "b", " ", "\r", "\n", "\r\n", "x", ""];
		var builder = new StringBuilder();

		for (int i = random.Next(minPieces, maxPieces + 1); i > 0; i--)
			builder.Append(pieces[random.Next(pieces.Length)]);

		return builder.ToString();
	}

	private static void AssertSnapshotInSync(TextDocument document, TextMateDocumentLineList lineList, string context)
	{
		Assert.AreEqual(document.LineCount, lineList.GetNumberOfLines(), $"{context}: line count");

		for (int i = 0; i < document.LineCount; i++)
		{
			DocumentLine line = document.GetLineByNumber(i + 1);

			Assert.AreEqual(
				document.GetText(line.Offset, line.TotalLength),
				lineList.GetLineTextIncludingTerminators(i).ToString(),
				$"{context}: line {i} text");
			Assert.AreEqual(line.TotalLength, lineList.GetLineLength(i), $"{context}: line {i} length");
		}
	}
}
