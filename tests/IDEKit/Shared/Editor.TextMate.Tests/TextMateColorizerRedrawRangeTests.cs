#if AVALONIAEDIT
using AvaloniaEdit.Document;
#else
using ICSharpCode.AvalonEdit.Document;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests;

#else

namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;

#endif

[TestClass]
public sealed class TextMateColorizerRedrawRangeTests
{
	[TestMethod]
	[DataRow("abc\ndef\nghi", 0, 0, 0, 4, DisplayName = "SingleLineIncludesTerminator")]
	[DataRow("abc\ndef\nghi", 1, 2, 4, 7, DisplayName = "MultipleLinesSpanTerminators")]
	[DataRow("abc\ndef\nghi", -5, 10, 0, 11, DisplayName = "ClampsToWholeDocument")]
	[DataRow("abc\ndef", 1, 1, 4, 3, DisplayName = "LastLineHasNoTerminator")]
	[DataRow("", 0, 0, 0, 0, DisplayName = "EmptyDocument")]
	public void ComputeRedrawRange_CoveredLines_ReturnsOffsetAndLength(
		string text,
		int firstLineIndex,
		int lastLineIndex,
		int expectedOffset,
		int expectedLength
	)
	{
		var document = new TextDocument(text);

		(int Offset, int Length)? range = TextMateColorizer.ComputeRedrawRange(document, firstLineIndex, lastLineIndex);

		Assert.IsNotNull(range);
		Assert.AreEqual(expectedOffset, range.Value.Offset);
		Assert.AreEqual(expectedLength, range.Value.Length);
	}

	[TestMethod]
	public void ComputeRedrawRange_EmptyRange_ReturnsNull()
	{
		var document = new TextDocument("abc\ndef\nghi");

		// An inverted range and a range past the last line both cover no lines.
		Assert.IsNull(TextMateColorizer.ComputeRedrawRange(document, 2, 0));
		Assert.IsNull(TextMateColorizer.ComputeRedrawRange(document, 5, 6));
	}
}
