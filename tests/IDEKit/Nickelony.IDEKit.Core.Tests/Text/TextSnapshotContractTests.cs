namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class TextSnapshotContractTests
{
	[TestMethod]
	[DataRow("", DisplayName = "Empty")]
	[DataRow("plain", DisplayName = "SingleLineWithoutTerminator")]
	[DataRow("one\r\ntwo\nthree\rfour", DisplayName = "MixedTerminators")]
	[DataRow("a\n", DisplayName = "TrailingTerminator")]
	[DataRow("\r\n\r\n", DisplayName = "BlankLinesOnly")]
	[DataRow("A\U0001F600B\n\uD83D\n", DisplayName = "Surrogates")]
	public void Lines_ExposeTheDocumentedLineMetadata(string text)
	{
		ITextSnapshot snapshot = new StringTextSnapshot(text);

		Assert.AreEqual(text.Length, snapshot.TextLength);
		Assert.IsTrue(snapshot.LineCount >= 1);

		int nextLineStart = 0;

		for (int index = 0; index < snapshot.LineCount; index++)
		{
			ITextLine line = snapshot.GetLineByNumber(index + 1);
			ITextLine viewLine = snapshot.Lines[index];

			Assert.AreEqual(index + 1, line.LineNumber, $"Line {index + 1} number.");
			Assert.AreEqual(line.Offset + line.Length, line.EndOffset, $"Line {index + 1} end offset.");
			Assert.AreEqual(viewLine.LineNumber, line.LineNumber, $"Line {index + 1} view number.");
			Assert.AreEqual(viewLine.Offset, line.Offset, $"Line {index + 1} view offset.");
			Assert.AreEqual(viewLine.Length, line.Length, $"Line {index + 1} view length.");

			// Line lengths exclude terminators, so each line starts exactly where the previous
			// line's terminator ended and the line text must match the source slice.
			Assert.AreEqual(nextLineStart, line.Offset, $"Line {index + 1} start offset.");
			Assert.AreEqual(text.Substring(line.Offset, line.Length), snapshot.GetText(line.Offset, line.Length), $"Line {index + 1} text.");

			nextLineStart = line.EndOffset;

			if (nextLineStart < text.Length && text[nextLineStart] == '\r' && nextLineStart + 1 < text.Length && text[nextLineStart + 1] == '\n')
				nextLineStart += 2;
			else if (nextLineStart < text.Length && text[nextLineStart] is '\r' or '\n')
				nextLineStart++;
		}

		Assert.AreEqual(text.Length, nextLineStart, "The lines must reconstruct the whole text.");
		Assert.AreEqual(text, snapshot.GetText(0, snapshot.TextLength));
	}

	[TestMethod]
	[DataRow("", DisplayName = "Empty")]
	[DataRow("plain", DisplayName = "SingleLineWithoutTerminator")]
	[DataRow("one\r\ntwo\nthree\rfour", DisplayName = "MixedTerminators")]
	[DataRow("a\n", DisplayName = "TrailingTerminator")]
	[DataRow("\r\n\r\n", DisplayName = "BlankLinesOnly")]
	[DataRow("A\U0001F600B\n\uD83D\n", DisplayName = "Surrogates")]
	public void GetLineByOffset_MapsEveryOffsetToTheDocumentedLine(string text)
	{
		ITextSnapshot snapshot = new StringTextSnapshot(text);
		List<int> expectedLineStarts = GetExpectedLineStarts(text);

		// The spec: a line starts after a terminator, an offset on a terminator belongs to the
		// preceding line, and the end-of-text offset belongs to the final line. The expected starts
		// are derived from the input text, so the assertion cannot inherit a defect from the
		// snapshot's own line table.
		Assert.AreEqual(expectedLineStarts.Count, snapshot.LineCount, "Line count.");

		for (int offset = 0; offset <= text.Length; offset++)
		{
			int expectedLineNumber = 1;

			for (int index = 0; index < expectedLineStarts.Count; index++)
			{
				if (expectedLineStarts[index] <= offset)
					expectedLineNumber = index + 1;
			}

			Assert.AreEqual(expectedLineNumber, snapshot.GetLineByOffset(offset).LineNumber, $"Offset {offset}.");
		}
	}

	[TestMethod]
	public void GetCharAt_EveryOffset_ReturnsTheDocumentedCharacter()
	{
		const string text = "one\r\ntwo\nthree\rfour";
		ITextSnapshot snapshot = new StringTextSnapshot(text);

		for (int offset = 0; offset < text.Length; offset++)
			Assert.AreEqual(text[offset], snapshot.GetCharAt(offset), $"Offset {offset}.");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetCharAt(text.Length));
	}

	/// <summary>
	/// Derives the document's line start offsets from the raw text, so the expected line of an offset
	/// is computed independently of the snapshot's own line table.
	/// </summary>
	private static List<int> GetExpectedLineStarts(string text)
	{
		var lineStarts = new List<int> { 0 };

		for (int index = 0; index < text.Length; index++)
		{
			if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
				index++;

			if (text[index] is '\r' or '\n')
				lineStarts.Add(index + 1);
		}

		return lineStarts;
	}
}
