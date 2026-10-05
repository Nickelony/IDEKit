using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Editing;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class TextEditConversionTests
{
	private static TextEdit Edit(int startLine, int startCharacter, int endLine, int endCharacter, string newText) =>
		new(new TextPositionRange(new TextPosition(startLine, startCharacter), new TextPosition(endLine, endCharacter)), newText);

	[TestMethod]
	public void Convert_EditsInsideTheText_ReturnsTheValidatedBatch()
	{
		TextEditConversionOutcome outcome = TextEditConversion.Convert(
			"ab\ncd",

			[
				Edit(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 1, newText: "X"),
				Edit(startLine: 1, startCharacter: 1, endLine: 1, endCharacter: 2, newText: "YY"),
			],

			out PreparedTextEdits prepared
		);

		Assert.AreEqual(TextEditConversionOutcome.Converted, outcome);
		Assert.AreEqual("Xb\ncYY", prepared.ApplyTo("ab\ncd"));
	}

	[TestMethod]
	public void Convert_NoEdits_ReturnsAnEmptyBatch()
	{
		TextEditConversionOutcome outcome = TextEditConversion.Convert("text", [], out PreparedTextEdits prepared);

		Assert.AreEqual(TextEditConversionOutcome.Converted, outcome);
		Assert.IsEmpty(prepared.Operations);
	}

	[TestMethod]
	public void Convert_ReversedRange_ReportsAnInvalidRange()
	{
		TextEditConversionOutcome outcome = TextEditConversion.Convert(
			"abcd",
			[Edit(startLine: 0, startCharacter: 3, endLine: 0, endCharacter: 1, newText: "X")],
			out PreparedTextEdits prepared
		);

		Assert.AreEqual(TextEditConversionOutcome.InvalidRange, outcome);
		Assert.IsEmpty(prepared.Operations);
	}

	[TestMethod]
	public void Convert_OverlappingEdits_ReportsAConflict()
	{
		TextEditConversionOutcome outcome = TextEditConversion.Convert(
			"abcdef",

			[
				Edit(startLine: 0, startCharacter: 0, endLine: 0, endCharacter: 4, newText: "X"),
				Edit(startLine: 0, startCharacter: 2, endLine: 0, endCharacter: 5, newText: "Y"),
			],

			out PreparedTextEdits prepared
		);

		Assert.AreEqual(TextEditConversionOutcome.Conflict, outcome);
		Assert.IsEmpty(prepared.Operations);
	}

	[TestMethod]
	public void Convert_NullArguments_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => TextEditConversion.Convert(null!, [], out _));
		Assert.ThrowsExactly<ArgumentNullException>(() => TextEditConversion.Convert("text", null!, out _));
	}
}
