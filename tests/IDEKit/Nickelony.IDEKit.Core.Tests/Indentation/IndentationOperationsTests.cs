namespace Nickelony.IDEKit.Core.Tests;

[TestClass]
public sealed class IndentationOperationsTests
{
	[TestMethod]
	[DataRow(false, 4, "\t", DisplayName = "TabMode")]
	[DataRow(true, 4, "    ", DisplayName = "Spaces")]
	[DataRow(true, 0, "    ", DisplayName = "FallsBackToFour")]
	public void CreateIndentationUnit_ReturnsExpectedUnit(bool convertTabsToSpaces, int indentationSize, string expected) =>
		Assert.AreEqual(expected, IndentationOperations.CreateIndentationUnit(convertTabsToSpaces, indentationSize));

	[TestMethod]
	[DataRow("", "    ", "", DisplayName = "EmptyIndentation")]
	[DataRow("  ", "    ", "", DisplayName = "ShorterThanUnit")]
	[DataRow("\t\t", "\t", "\t", DisplayName = "TwoTabs")]
	[DataRow("        ", "    ", "    ", DisplayName = "TwoUnits")]
	public void TruncateIndentationByUnitLength_RemovesOneUnit(string indentation, string unit, string expected) =>
		Assert.AreEqual(expected, IndentationOperations.TruncateIndentationByUnitLength(indentation, unit));

	[TestMethod]
	public void TruncateIndentationByUnitLength_MixedIndentation_TruncatesByUnitLength()
	{
		// The unit is never matched, so a tab unit removes one character from space indentation.
		Assert.AreEqual("     ", IndentationOperations.TruncateIndentationByUnitLength("      ", "\t"));
		Assert.AreEqual(" ", IndentationOperations.TruncateIndentationByUnitLength("  ", "\t"));
	}

	[TestMethod]
	public void TruncateIndentationByUnitLength_EmptyUnit_RemovesOneCharacter() =>
		Assert.AreEqual("ab", IndentationOperations.TruncateIndentationByUnitLength("abc", string.Empty));

	[TestMethod]
	public void TruncateIndentationByUnitLength_UnitLongerThanIndentation_RemovesWholeIndentation() =>
		Assert.AreEqual(string.Empty, IndentationOperations.TruncateIndentationByUnitLength("  ", "    "));

	[TestMethod]
	[DataRow("  ", "    ", 0, "  ", DisplayName = "ZeroLevels")]
	[DataRow("  ", "    ", 2, "          ", DisplayName = "TwoLevels")]
	public void BuildIndentation_ComposesBaseAndLevels(string baseIndentation, string unit, int level, string expected) =>
		Assert.AreEqual(expected, IndentationOperations.BuildIndentation(baseIndentation, unit, level));

	[TestMethod]
	public void BuildIndentation_NegativeLevel_ReturnsBaseIndentation() =>
		Assert.AreEqual("  ", IndentationOperations.BuildIndentation("  ", "    ", -3));

	[TestMethod]
	public void BuildIndentation_ManyLevels_AppendsEveryUnit()
	{
		string result = IndentationOperations.BuildIndentation(string.Empty, "\t", 10_000);

		Assert.AreEqual(10_000, result.Length);
		Assert.IsTrue(result.All(character => character == '\t'));
	}
}
