using System.Collections;

namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class StringTextSnapshotCharacterizationTests
{
	[TestMethod]
	public void TextLength_CharactersAndRanges_UseUtf16Offsets()
	{
		var snapshot = new StringTextSnapshot("A\U0001F600B", "script.lua");

		Assert.AreEqual("script.lua", snapshot.FileName);
		Assert.AreEqual(4, snapshot.TextLength);
		Assert.AreEqual('A', snapshot.GetCharAt(0));
		Assert.AreEqual('\uD83D', snapshot.GetCharAt(1));
		Assert.AreEqual('\uDE00', snapshot.GetCharAt(2));
		Assert.AreEqual("\U0001F600", snapshot.GetText(1, 2));
		Assert.AreEqual("B", snapshot.GetText(3, 1));
	}

	[TestMethod]
	public void Constructor_NullTextWithFileName_TreatsContentAsEmptyAndKeepsTheFileName()
	{
		var snapshot = new StringTextSnapshot(null, "script.lua");

		Assert.AreEqual(0, snapshot.TextLength);
		Assert.AreEqual(1, snapshot.LineCount);
		Assert.AreEqual("script.lua", snapshot.FileName);
		Assert.AreEqual(string.Empty, snapshot.GetText(0, 0));
	}

	[TestMethod]
	public void MixedLineEndings_PreserveLineOffsetsAndLengths()
	{
		var snapshot = new StringTextSnapshot("one\r\ntwo\nthree\rfour");
		ITextLine[] lines = snapshot.Lines.ToArray();

		Assert.AreEqual(4, snapshot.LineCount);
		AssertLine(lines[0], 1, 0, 3);
		AssertLine(lines[1], 2, 5, 3);
		AssertLine(lines[2], 3, 9, 5);
		AssertLine(lines[3], 4, 15, 4);
		Assert.AreEqual(9, snapshot.GetLineByOffset(14).Offset);
		Assert.AreEqual(15, snapshot.GetLineByOffset(15).Offset);
		Assert.AreEqual(4, snapshot.GetLineByOffset(snapshot.TextLength).LineNumber);
	}

	[TestMethod]
	public void EmptyAndTrailingLines_ExposeStableMetadata()
	{
		var emptySnapshot = new StringTextSnapshot(null);
		var trailingSnapshot = new StringTextSnapshot("one\n");

		Assert.AreEqual(1, emptySnapshot.LineCount);
		AssertLine(emptySnapshot.GetLineByNumber(1), 1, 0, 0);
		Assert.AreEqual(2, trailingSnapshot.LineCount);
		AssertLine(trailingSnapshot.GetLineByNumber(2), 2, 4, 0);
		Assert.AreEqual(2, trailingSnapshot.GetLineByOffset(trailingSnapshot.TextLength).LineNumber);
	}

	[TestMethod]
	public void InvalidOffsetsAndLineNumbers_Throw()
	{
		var snapshot = new StringTextSnapshot("text");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetCharAt(-1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetCharAt(snapshot.TextLength));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetText(3, 2));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetLineByOffset(snapshot.TextLength + 1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetLineByNumber(0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetLineByNumber(snapshot.LineCount + 1));
	}

	[TestMethod]
	public void Lines_CannotBeMutatedThroughTheListInterface()
	{
		var snapshot = new StringTextSnapshot("one\ntwo");
		var lines = (IList)snapshot.Lines;

		// The snapshot exposes a read-only view, so replacing an entry must be rejected.
		Assert.ThrowsExactly<NotSupportedException>(() => lines[0] = lines[1]);
	}

	[TestMethod]
	public void GetText_InvalidLength_ReportsLengthAsParameterName()
	{
		var snapshot = new StringTextSnapshot("text");

		var exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetText(3, 2));

		Assert.AreEqual("length", exception.ParamName);
	}

	[TestMethod]
	public void GetTextAndGetLineByOffset_NegativeValues_Throw()
	{
		var snapshot = new StringTextSnapshot("text");

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetText(-1, 1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetText(0, -1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetLineByOffset(-1));
	}

	[TestMethod]
	public void Lines_AreCachedPerSnapshotInstance()
	{
		var snapshot = new StringTextSnapshot("one\ntwo");

		Assert.IsTrue(ReferenceEquals(snapshot.Lines, snapshot.Lines));
	}

	[TestMethod]
	public void GetText_EmptyRangeAtEnd_IsValid()
	{
		var snapshot = new StringTextSnapshot("text");

		Assert.AreEqual(string.Empty, snapshot.GetText(snapshot.TextLength, 0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetText(snapshot.TextLength, 1));
	}

	[TestMethod]
	public void GetText_FullRange_ReturnsTheBackingInstance()
	{
		// Build the text at runtime so the pin cannot pass by string interning.
		string source = string.Concat("line one", '\n', "line two");
		var snapshot = new StringTextSnapshot(source);

		_ = snapshot.GetText(0, snapshot.TextLength); // warm up any lazy paths

		string full = snapshot.GetText(0, source.Length);

		// The identity check is the no-copy proof: a full-range read returns the backing instance
		// instead of a copy. Allocation counts are not asserted because the interface documents the
		// optimization as optional.
		Assert.IsTrue(ReferenceEquals(source, full));
	}

	[TestMethod]
	[TestCategory("Performance")]
	public void LargeDocument_ExposesCompleteLineMetadata()
	{
		const int LineCount = 40_000;
		string text = string.Concat(Enumerable.Repeat("local value = 1\n", LineCount));
		string largerText = string.Concat(Enumerable.Repeat("local value = 1\n", LineCount * 4));

		// The snapshot materializes its line table on first line access; the scaling probe samples
		// that first access at both sizes, so a quadratic build fails while a linear one stays near the
		// quadrupling factor. Every sample builds a fresh snapshot, and the shared helper re-samples a
		// breach, so a scheduler hiccup does not decide the assertion.
		// Warm both shapes once so JIT and first-use allocations stay out of the sampled runs.
		_ = new StringTextSnapshot(text).GetLineByOffset(text.Length);
		_ = new StringTextSnapshot(largerText).GetLineByOffset(largerText.Length);

		TestMeasurements.AssertScalingBound(
			() =>
			{
				var measured = new StringTextSnapshot(text);
				_ = measured.GetLineByOffset(text.Length);
			},
			() =>
			{
				var measured = new StringTextSnapshot(largerText);
				_ = measured.GetLineByOffset(largerText.Length);
			},
			"Building four times the line count took {0} versus {1}; the build is quadratic.");

		var snapshot = new StringTextSnapshot(text);
		ITextLine firstLine = snapshot.GetLineByNumber(1);
		ITextLine lastLine = snapshot.GetLineByNumber(snapshot.LineCount);

		Assert.AreEqual(text.Length, snapshot.TextLength);
		Assert.AreEqual(LineCount + 1, snapshot.LineCount);
		Assert.AreEqual(0, firstLine.Offset);
		Assert.AreEqual("local value = 1", snapshot.GetText(firstLine.Offset, firstLine.Length));
		Assert.AreEqual(text.Length, lastLine.Offset);
		Assert.AreEqual(lastLine.LineNumber, snapshot.GetLineByOffset(text.Length).LineNumber);
	}

	private static void AssertLine(ITextLine line, int lineNumber, int offset, int length)
	{
		Assert.AreEqual(lineNumber, line.LineNumber);
		Assert.AreEqual(offset, line.Offset);
		Assert.AreEqual(length, line.Length);
		Assert.AreEqual(offset + length, line.EndOffset);
	}
}
