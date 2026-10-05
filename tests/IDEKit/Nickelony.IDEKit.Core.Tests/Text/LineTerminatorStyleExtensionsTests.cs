namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class LineTerminatorStyleExtensionsTests
{
	[TestMethod]
	[DataRow(LineTerminatorStyle.Lf, "LF")]
	[DataRow(LineTerminatorStyle.CrLf, "CRLF")]
	[DataRow(LineTerminatorStyle.Cr, "CR")]
	[DataRow(LineTerminatorStyle.Mixed, "Mixed")]
	public void GetDisplayName_OfAStyle_IsItsLabel(LineTerminatorStyle style, string expected) => Assert.AreEqual(expected, style.GetDisplayName());

	[TestMethod]
	public void GetDisplayName_OfNone_IsTheLabelOfTheTerminatorThatAnEditorInsertsForTheFirstNewLine()
	{
		LineTerminatorStyle platform = Environment.NewLine == "\r\n" ? LineTerminatorStyle.CrLf : LineTerminatorStyle.Lf;
		Assert.AreEqual(platform.GetDisplayName(), LineTerminatorStyle.None.GetDisplayName());
	}

	[TestMethod]
	public void GetDisplayName_OfAnUndefinedStyle_Throws() =>
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ((LineTerminatorStyle)42).GetDisplayName());

	[TestMethod]
	public void GetDisplayName_OfEveryDefinedStyle_IsNotBlank()
	{
		foreach (LineTerminatorStyle style in Enum.GetValues<LineTerminatorStyle>())
			Assert.IsFalse(string.IsNullOrWhiteSpace(style.GetDisplayName()), style.ToString());
	}
}
