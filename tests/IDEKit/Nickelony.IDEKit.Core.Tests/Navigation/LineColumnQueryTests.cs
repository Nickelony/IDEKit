namespace Nickelony.IDEKit.Core.Tests;

/// <summary>Covers how the text a user types for a line, or a line and a column, is read.</summary>
[TestClass]
public sealed class LineColumnQueryTests
{
	[TestMethod]
	public void TryParse_ALine_HasNoColumn()
	{
		Assert.IsTrue(LineColumnQuery.TryParse("12", out LineColumnQuery query));
		Assert.AreEqual(new LineColumnQuery(12, null), query);
	}

	[TestMethod]
	public void TryParse_ALineAndAColumn_HasBoth()
	{
		Assert.IsTrue(LineColumnQuery.TryParse("12:5", out LineColumnQuery query));
		Assert.AreEqual(new LineColumnQuery(12, 5), query);
	}

	[TestMethod]
	public void TryParse_WhitespaceAroundTheNumbers_IsIgnored()
	{
		Assert.IsTrue(LineColumnQuery.TryParse("  12 : 5  ", out LineColumnQuery query));
		Assert.AreEqual(new LineColumnQuery(12, 5), query);
	}

	[TestMethod]
	public void TryParse_AnEmptyColumn_IsNoColumn()
	{
		Assert.IsTrue(LineColumnQuery.TryParse("12:", out LineColumnQuery withColon));
		Assert.IsTrue(LineColumnQuery.TryParse("12: ", out LineColumnQuery withSpace));
		Assert.AreEqual(new LineColumnQuery(12, null), withColon);
		Assert.AreEqual(new LineColumnQuery(12, null), withSpace);
	}

	[TestMethod]
	public void TryParse_ANumberTooLargeForAnInteger_CountsAsTheLargestInteger()
	{
		Assert.IsTrue(LineColumnQuery.TryParse("99999999999999999999:99999999999999999999", out LineColumnQuery query));
		Assert.AreEqual(new LineColumnQuery(int.MaxValue, int.MaxValue), query);
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow("   ")]
	[DataRow("abc")]
	[DataRow("12x")]
	[DataRow("1:2:3")]
	[DataRow(":5")]
	[DataRow("12:x")]
	[DataRow("-3")]
	[DataRow("+3")]
	[DataRow("1.5")]
	[DataRow("1 2")]
	public void TryParse_TextThatIsNoPosition_ReturnsFalse(string? text)
	{
		Assert.IsFalse(LineColumnQuery.TryParse(text, out LineColumnQuery query));
		Assert.AreEqual(default, query);
	}
}
