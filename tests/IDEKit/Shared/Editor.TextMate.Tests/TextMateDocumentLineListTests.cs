using System.Text;
using TextMateSharp.Grammars;
using TextMateSharp.Model;
using TextMateSharp.Registry;
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

[TestClass]
public sealed class TextMateDocumentLineListTests
{
	[TestMethod]
	[DataRow("a\r\nb", 2, 1, null, "a|b", false, DisplayName = "RemoveLfFromCrLfPair")]
	[DataRow("a\r\nb", 1, 1, null, "a|b", false, DisplayName = "RemoveCrFromCrLfPair")]
	[DataRow("a\nb", 1, 0, "\r", "a|b", false, DisplayName = "InsertCrBeforeLf")]
	[DataRow("a\r\nb", 2, 1, "\r", "a||b", false, DisplayName = "ReplaceLfWithCr")]
	[DataRow("a\nb", 1, 1, null, "ab", false, DisplayName = "RemoveWholeTerminator")]
	[DataRow("alpha\r\nbeta", 5, 2, "\n", "alpha|beta", false, DisplayName = "CollapseCrLfToLf")]
	[DataRow("alpha\nbeta", 5, 1, "\r\n", "alpha|beta", false, DisplayName = "ExpandLfToCrLf")]
	[DataRow("ab", 2, 0, "\r\n", "ab|", false, DisplayName = "AppendCrLfAtEnd")]
	[DataRow("a\rb", 2, 0, "\n", "a|b", false, DisplayName = "InsertLfAfterLoneCr")]
	[DataRow("\r\na", 0, 1, null, "|a", false, DisplayName = "RemoveLeadingCrOfCrLfPair")]
	[DataRow("a\n\r\nb", 2, 1, null, "a||b", false, DisplayName = "RemoveCrOfLineStartingCrLfPair")]
	[DataRow("a\r\r\nb", 2, 1, null, "a|b", false, DisplayName = "RemoveCrBeforeCrLfPair")]
	[DataRow("\r\r\nb", 1, 1, "\n", "||b", false, DisplayName = "ReplaceCrWithLfBeforeLf")]
	[DataRow("a\rb", 1, 1, "\n", "a|b", false, DisplayName = "ReplaceCrWithLf")]
	[DataRow("a\r\nb", 2, 1, null, "a|b", true, DisplayName = "RemoveLfFromCrLfPairWithModel")]
	[DataRow("a\r\nb", 1, 1, null, "a|b", true, DisplayName = "RemoveCrFromCrLfPairWithModel")]
	[DataRow("a\nb", 1, 0, "\r", "a|b", true, DisplayName = "InsertCrBeforeLfWithModel")]
	[DataRow("a\r\nb", 2, 1, "\r", "a||b", true, DisplayName = "ReplaceLfWithCrWithModel")]
	[DataRow("a\rb", 2, 0, "\n", "a|b", true, DisplayName = "InsertLfAfterLoneCrWithModel")]
	[DataRow("\r\na", 0, 1, null, "|a", true, DisplayName = "RemoveLeadingCrOfCrLfPairWithModel")]
	public void DocumentLineList_LineTerminatorBoundaryEdits_KeepSnapshotInSync(
		string originalText,
		int offset,
		int removalLength,
		string? insertedText,
		string expectedLineContents,
		bool withModel
	)
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

			Assert.AreSequenceEqual(
				[.. expectedLineContents.Split('|').Select(static content => content + "\n")],
				GetSnapshotLines(lineList),
				DescribeSnapshot(lineList)
			);
		}
		finally
		{
			model?.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public void DocumentLineList_BatchReplacementWithAnOffsetChangeMap_KeepsSnapshotInSync(bool withModel)
	{
		// One change over the span of three replaced indentations, as an edit target applies a batch of indentation edits.
		var document = new TextDocument("a\n    b\n        c\n    d\ne");
		var lineList = new TextMateDocumentLineList(document);

		TMModel? model = withModel ? new TMModel(lineList) : null;

		try
		{
			var map = new OffsetChangeMap
			{
				new OffsetChangeMapEntry(18, 4, 1),
				new OffsetChangeMapEntry(8, 8, 2),
				new OffsetChangeMapEntry(2, 4, 1),
			};

			document.Replace(offset: 2, length: 21, text: "\tb\n\t\tc\n\td", offsetChangeMap: map);

			Assert.AreEqual("a\n\tb\n\t\tc\n\td\ne", document.Text);
			Assert.AreEqual(document.LineCount, lineList.GetNumberOfLines());
			Assert.AreSequenceEqual(["a\n", "\tb\n", "\t\tc\n", "\td\n", "e\n"], GetSnapshotLines(lineList), DescribeSnapshot(lineList));
		}
		finally
		{
			model?.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_CrLfPairEdit_KeepsTheTokenizationOfTheLineWhoseContentIsUnchanged()
	{
		var document = new TextDocument("a\r\nb");
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			for (int i = 0; i < lineList.GetNumberOfLines(); i++)
				lineList.Get(i).IsInvalid = false;

			// Removing the line feed of the CRLF pair changes only the terminator that joins the two lines. The snapshot
			// holds content, so the first line's text is the same and it keeps its tokens.
			document.Remove(2, 1);

			Assert.IsFalse(lineList.Get(0).IsInvalid);
			Assert.AreSequenceEqual(["a\n", "b\n"], GetSnapshotLines(lineList));
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}

	[TestMethod]
	[DataRow("alpha\nbeta", "alpha\n", "beta\n", DisplayName = "LineFeed")]
	[DataRow("alpha\r\nbeta", "alpha\n", "beta\n", DisplayName = "CarriageReturnLineFeed")]
	[DataRow("alpha\rbeta", "alpha\n", "beta\n", DisplayName = "LoneCarriageReturn")]
	public void DocumentLineList_AnyTerminator_ReadsContentFollowedByOneLineFeed(string text, string firstLine, string lastLine)
	{
		var lineList = new TextMateDocumentLineList(new TextDocument(text));

		try
		{
			Assert.AreSequenceEqual([firstLine, lastLine], GetSnapshotLines(lineList));
			Assert.AreEqual(firstLine.Length, lineList.GetLineLength(0));
			Assert.AreEqual(lastLine.Length, lineList.GetLineLength(1));
		}
		finally
		{
			lineList.Dispose();
		}
	}

	[TestMethod]
	public void DocumentLineList_EmptyDocumentAndEmptyLastLine_ReadAsALineFeed()
	{
		var document = new TextDocument("a\r\n");
		var lineList = new TextMateDocumentLineList(document);

		try
		{
			Assert.AreSequenceEqual(["a\n", "\n"], GetSnapshotLines(lineList));
			Assert.AreEqual(1, lineList.GetLineLength(1));

			document.Text = string.Empty;

			Assert.AreSequenceEqual(["\n"], GetSnapshotLines(lineList));
			Assert.AreEqual(1, lineList.GetLineLength(0));
		}
		finally
		{
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
			Assert.AreSequenceEqual(["x\n"], GetSnapshotLines(lineList));
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
			Assert.AreSequenceEqual(["alpha\n", "beta\n"], GetSnapshotLines(lineList));

			document.Text = document.Text.Replace("\n", "\r\n", StringComparison.Ordinal);

			Assert.AreEqual(document.LineCount, lineList.GetNumberOfLines());
			Assert.AreSequenceEqual(["alpha\n", "beta\n"], GetSnapshotLines(lineList));

			document.Text = "x\r\n";

			Assert.AreEqual(document.LineCount, lineList.GetNumberOfLines());
			Assert.AreSequenceEqual(["x\n", "\n"], GetSnapshotLines(lineList));

			document.Text = string.Empty;

			Assert.AreEqual(1, lineList.GetNumberOfLines());
			Assert.AreSequenceEqual(["\n"], GetSnapshotLines(lineList));
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
			Assert.AreSequenceEqual(["alpha\n", "\n"], GetSnapshotLines(lineList));
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
			Assert.AreSequenceEqual(["alpha\n", "beta\n"], GetSnapshotLines(lineList));

			document.Insert(document.TextLength, "\r\ngamma");

			Assert.AreSequenceEqual(["alpha\n", "beta\n", "gamma\n"], GetSnapshotLines(lineList));
			Assert.AreEqual(3, lineList.GetNumberOfLines());

			int replacementOffset = document.Text.IndexOf("beta\r\ngamma", StringComparison.Ordinal);
			document.Replace(replacementOffset, "beta\r\ngamma".Length, "delta");

			Assert.AreSequenceEqual(["alpha\n", "delta\n"], GetSnapshotLines(lineList));
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
			// the slot that held the removed comment line, and the preceding line ends in
			// the same state, so a model that only invalidates the first changed line can leave the stale
			// comment tokens in place.
			document.Replace(6, "y = 2\n-- old comment".Length, "Y = 2\nC = 3\nlocal d = 1");

			WaitForTokenization(model, lineList);

			List<string> scopes = GetLineScopes(model, 3);

			Assert.IsTrue(
				scopes.Any(scope => scope.Contains("keyword", StringComparison.Ordinal)),
				"Expected the third replacement line to be tokenized as code."
			);

			Assert.IsFalse(
				scopes.Any(scope => scope.Contains("comment", StringComparison.Ordinal)),
				"The third replacement line must not keep tokens from the removed comment line."
			);
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

			Assert.AreSequenceEqual(["alpha\n", "beta\n"], GetSnapshotLines(lineList));
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
			Assert.AreSequenceEqual(["\n", "a\n"], GetSnapshotLines(lineList), DescribeSnapshot(lineList));
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
			Assert.AreEqual("beta\n", lineList.GetLineTextIncludingTerminators(1).ToString());
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
		Assert.AreSequenceEqual(["alpha\n", "beta\n"], GetSnapshotLines(lineList));
	}

	private static List<string> GetLineScopes(TMModel model, int lineIndex)
	{
		List<TMToken> tokens = model.GetLineTokens(lineIndex) ?? throw new AssertFailedException($"Line {lineIndex} has no tokens.");
		return [.. tokens.SelectMany(token => token.Scopes).Distinct(StringComparer.Ordinal)];
	}

	internal static string[] GetSnapshotLines(TextMateDocumentLineList lineList) =>
		[.. Enumerable.Range(0, lineList.GetNumberOfLines()).Select(index => lineList.GetLineTextIncludingTerminators(index).ToString())];

	internal static string DescribeSnapshot(TextMateDocumentLineList lineList)
	{
		return "snapshot=["
			+ string.Join(", ", GetSnapshotLines(lineList).Select(static line => "'" + line.Replace("\r", "\\r").Replace("\n", "\\n") + "'"))
			+ "]";
	}

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
				document.GetText(line.Offset, line.Length) + "\n",
				lineList.GetLineTextIncludingTerminators(i).ToString(),
				$"{context}: line {i} text"
			);

			Assert.AreEqual(line.Length + 1, lineList.GetLineLength(i), $"{context}: line {i} length");
		}
	}
}
