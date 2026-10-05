namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class LineTerminatorDetectorTests
{
	[TestMethod]
	[DataRow("", LineTerminatorStyle.None, DisplayName = "Empty")]
	[DataRow("one", LineTerminatorStyle.None, DisplayName = "NoTerminator")]
	[DataRow("one\ntwo\n", LineTerminatorStyle.Lf, DisplayName = "LineFeeds")]
	[DataRow("one\r\ntwo\r\n", LineTerminatorStyle.CrLf, DisplayName = "CarriageReturnLineFeeds")]
	[DataRow("one\rtwo\r", LineTerminatorStyle.Cr, DisplayName = "CarriageReturns")]
	[DataRow("\r\n", LineTerminatorStyle.CrLf, DisplayName = "OneCrLfIsNotTwoTerminators")]
	public void Detect_OneTerminatorStyle_ReportsThatStyle(string text, LineTerminatorStyle expected) =>
		Assert.AreEqual(expected, LineTerminatorDetector.Detect(text));

	[TestMethod]
	[DataRow("one\r\ntwo\nthree", DisplayName = "CrLfThenLf")]
	[DataRow("one\ntwo\r\nthree", DisplayName = "LfThenCrLf")]
	[DataRow("one\r\ntwo\rthree", DisplayName = "CrLfThenCr")]
	[DataRow("one\rtwo\r\nthree", DisplayName = "CrThenCrLf")]
	[DataRow("one\ntwo\rthree", DisplayName = "LfThenCr")]
	[DataRow("one\rtwo\nthree", DisplayName = "CrThenLf")]
	[DataRow("\n\r", DisplayName = "LfBeforeCr")]
	[DataRow("\r\r\n", DisplayName = "CrBeforeCrLf")]
	public void Detect_MoreThanOneTerminatorStyle_ReportsMixed(string text) =>
		Assert.AreEqual(LineTerminatorStyle.Mixed, LineTerminatorDetector.Detect(text));

	[TestMethod]
	public void Detect_NullText_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => LineTerminatorDetector.Detect(null!));
}
