using Nickelony.IDEKit.Markdown.Model;

namespace Nickelony.IDEKit.Markdown.Tests;

[TestClass]
public sealed class MarkdownCodeBlockTests
{
	[TestMethod]
	public void NormalizedCode_CrLfCodeWithFinalTerminator_ReturnsLineFeedsWithoutTheFinalOne() =>
		Assert.AreEqual("a\nb", new MarkdownCodeBlock("lua", "a\r\nb\r\n").NormalizedCode);

	[TestMethod]
	public void NormalizedCode_AuthoredTrailingBlankLines_AreKept() =>
		Assert.AreEqual("a\n\n", new MarkdownCodeBlock("lua", "a\n\n\n").NormalizedCode);

	[TestMethod]
	public void NormalizedCode_BlankCode_IsEmpty() => Assert.AreEqual(string.Empty, new MarkdownCodeBlock(null, "  \n\n").NormalizedCode);

	[TestMethod]
	public void NormalizedCode_BuiltFromCrLfFence_NormalizesWhileCodeKeepsTheSourceTerminators()
	{
		MarkdownDocumentModel model = MarkdownDocumentModelBuilder.Build("```lua\r\nlocal value = 1\r\nprint(value)\r\n```", new HashSet<string>());

		var codeBlock = (MarkdownCodeBlock)model.Blocks.Single();

		Assert.AreEqual("local value = 1\r\nprint(value)\r\n", codeBlock.Code);
		Assert.AreEqual("local value = 1\nprint(value)", codeBlock.NormalizedCode);
	}

	[TestMethod]
	public void NormalizedCode_BuiltFromFenceWithAnAuthoredTrailingBlankLine_KeepsTheBlankLine()
	{
		var codeBlock = (MarkdownCodeBlock)Build("```lua\nlocal value = 1\n\n```");

		Assert.AreEqual("local value = 1\n\n", codeBlock.Code);
		Assert.AreEqual("local value = 1\n", codeBlock.NormalizedCode);
	}

	[TestMethod]
	public void NormalizedCode_BuiltFromCrLfFenceWithAnAuthoredTrailingBlankLine_KeepsTheBlankLine()
	{
		var codeBlock = (MarkdownCodeBlock)Build("```lua\r\nlocal value = 1\r\n\r\n```");

		Assert.AreEqual("local value = 1\r\n\r\n", codeBlock.Code);
		Assert.AreEqual("local value = 1\n", codeBlock.NormalizedCode);
	}

	[TestMethod]
	public void NormalizedCode_BuiltFromFenceWithSeveralAuthoredTrailingBlankLines_KeepsEveryBlankLine()
	{
		var codeBlock = (MarkdownCodeBlock)Build("```lua\nlocal value = 1\n\n\n```");
		Assert.AreEqual("local value = 1\n\n", codeBlock.NormalizedCode);
	}

	[TestMethod]
	public void Code_BuiltFromTextThatEndsWithoutATerminator_IsNotGivenOne()
	{
		var fenced = (MarkdownCodeBlock)Build("```lua\nlocal value = 1");
		var indented = (MarkdownCodeBlock)Build("    local value = 1");

		Assert.AreEqual("local value = 1", fenced.Code);
		Assert.AreEqual("local value = 1", indented.Code);
	}

	[TestMethod]
	public void Code_BuiltFromAnEmptyFence_IsEmpty()
	{
		var codeBlock = (MarkdownCodeBlock)Build("```lua\n```");

		Assert.AreEqual(string.Empty, codeBlock.Code);
		Assert.AreEqual(string.Empty, codeBlock.NormalizedCode);
	}

	[TestMethod]
	public void NormalizedCode_BuiltFromAnIndentedBlock_DropsTheTerminatorOfItsLastLine()
	{
		var codeBlock = (MarkdownCodeBlock)Build("    local value = 1\n\nafter");

		Assert.AreEqual("local value = 1\n", codeBlock.Code);
		Assert.AreEqual("local value = 1", codeBlock.NormalizedCode);
	}

	private static MarkdownBlock Build(string markdown) => MarkdownDocumentModelBuilder.Build(markdown, new HashSet<string>()).Blocks[0];
}
