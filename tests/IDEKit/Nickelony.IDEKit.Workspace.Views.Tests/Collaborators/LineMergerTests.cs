namespace Nickelony.IDEKit.Workspace.Views.Tests;

[TestClass]
public sealed class LineMergerTests
{
	[TestMethod]
	[DataRow("a\nb\nc\n", "A\nb\nC\n", "a\nB\nc\n", "A\nB\nC\n", DisplayName = "Typing at the top and the bottom around a change in the middle")]
	[DataRow("x\na\nb\nc\ny\n", "x\nA\nb\nC\ny\n", "X\na\nb\nc\ny\n", "X\nA\nb\nC\ny\n", DisplayName = "A change outside the typed span")]
	[DataRow("1\n2\n3\n", "X\n2\nV\n", "X\n2\n3\n", "X\n2\nV\n", DisplayName = "An identical change counts once")]
	[DataRow("one\ntwo\n", "one\nNEW\ntwo\n", "ONE\ntwo\n", "ONE\nNEW\ntwo\n", DisplayName = "An insertion after the changed line")]
	[DataRow("a\nb\nc\nd\n", "a\nd\n", "a\nb\nc\nD\n", "a\nD\n", DisplayName = "A deletion next to the changed line")]
	[DataRow("a\r\nb\r\nc\r\n", "a\r\nb\r\nC\r\n", "A\r\nb\r\nc\r\n", "A\r\nb\r\nC\r\n", DisplayName = "CRLF lines")]
	[DataRow("a\r\nb\r\nc\r\n", "a\nb\r\nc\r\n", "a\r\nb\r\nC\r\n", "a\nb\r\nC\r\n", DisplayName = "A changed line terminator is a changed line")]
	[DataRow(
		"L\nL\nz\n",
		"L\nL\nL\nz\n",
		"M\nL\nz\n",
		"M\nL\nL\nz\n",
		DisplayName = "A duplicated line keeps the other change on the line it changed"
	)]
	[DataRow("", "x", "", "x", DisplayName = "A store change that only moved the version")]
	public void TryMerge_ChangesThatShareNoLine_MergeBoth(string baseText, string viewText, string storeText, string expected)
	{
		Assert.IsTrue(LineMerger.TryMerge(baseText, viewText, storeText, out string? merged));
		Assert.AreEqual(expected, merged);
	}

	[TestMethod]
	[DataRow("one\ntwo\n", "one!\ntwo\n", "ONE\ntwo\n", DisplayName = "Both change one line")]
	[DataRow("a\nb\nc\n", "a\n", "a\nB\nc\n", DisplayName = "A deletion over the changed line")]
	[DataRow("base\n", "base\nA\n", "base\nB\n", DisplayName = "Two insertions at one point")]
	[DataRow("abab", "ababab", "XYab", DisplayName = "Repeated text on one line")]
	[DataRow("", "x", "y", DisplayName = "Two insertions into an empty text")]
	public void TryMerge_ChangesThatDifferOnALine_Conflict(string baseText, string viewText, string storeText)
	{
		Assert.IsFalse(LineMerger.TryMerge(baseText, viewText, storeText, out string? merged));
		Assert.IsNull(merged);
	}

	[TestMethod]
	[DataRow(true, "V1\n2\nV3\n4\nS5\n")]
	[DataRow(false, "V1\n2\nS3\n4\nS5\n")]
	public void Resolve_TakesOneSideOfTheConflictingRegion_AndKeepsTheOtherHunksOfBothSides(bool keepsViewSide, string expected)
	{
		string resolved = LineMerger.Resolve(
			baseText: "1\n2\n3\n4\n5\n",
			viewText: "V1\n2\nV3\n4\n5\n",
			storeText: "1\n2\nS3\n4\nS5\n",
			keepsViewSide
		);

		Assert.AreEqual(expected, resolved);
	}
}
