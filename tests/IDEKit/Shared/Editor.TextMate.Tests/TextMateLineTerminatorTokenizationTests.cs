using TextMateSharp.Model;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
using static Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests.TextMateRunStyleTestHelpers;
#else
using ICSharpCode.AvalonEdit.Document;
using static Nickelony.IDEKit.AvalonEdit.TextMate.Tests.TextMateRunStyleTestHelpers;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests;

#else

namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;

#endif

/// <summary>
/// Pins the runs the colorizer paints for documents with each line terminator: the model's tokens clamped to each line's content,
/// which is what <see cref="TextMateColorizer"/> applies to the line.
/// </summary>
[TestClass]
public sealed class TextMateLineTerminatorTokenizationTests
{
	[TestMethod]
	[DataRow(
		"lua",
		"local x = 1 -- note\n--[[ block\nstill ]] y = 2\nlocal s = \"a b\"\n[[ long\nstring ]]\nreturn x",
		DisplayName = "LuaCommentsAndLongString"
	)]
	[DataRow("json", "{\n  \"a\": [1, 2],\n  \"b\": \"x\"\n}", DisplayName = "JsonStructure")]
	[DataRow("csharp", "/* a\n b */ int x = 1; // t\nstring s = \"q\";", DisplayName = "CSharpBlockCommentAndStrings")]
	[DataRow("cpp", "#define X 1 \\\n  + 2\nint y;\n", DisplayName = "CppMacroContinuation")]
	[DataRow("lua", "local s = \"abc\\\ndef\"\nlocal t = 1", DisplayName = "LuaStringContinuation")]
	public void DescribeRuns_AnyLineTerminator_ColorsTheLinesAsLineFeedsDo(string language, string lineFeedText)
	{
		IReadOnlyList<string> expected = DescribeRuns(lineFeedText, language);

		Assert.IsNotEmpty(expected);
		Assert.AreSequenceEqual(expected, DescribeRuns(lineFeedText.Replace("\n", "\r\n", StringComparison.Ordinal), language), "CRLF terminators");
		Assert.AreSequenceEqual(expected, DescribeRuns(lineFeedText.Replace("\n", "\r", StringComparison.Ordinal), language), "lone CR terminators");
	}

	[TestMethod]
	[DataRow("\n", DisplayName = "LineFeed")]
	[DataRow("\r\n", DisplayName = "CarriageReturnLineFeed")]
	[DataRow("\r", DisplayName = "LoneCarriageReturn")]
	public void DescribeRuns_CppMacroLineContinuation_KeepsTheNextLineInsideTheMacro(string terminator)
	{
		IReadOnlyList<string> runs = DescribeRuns("#define X 1 \\" + terminator + "  + 2" + terminator + "int y;" + terminator, "cpp");

		Assert.AreSequenceEqual(
			[
				"0:[0,1) keyword.control.directive.define.cpp > punctuation.definition.directive.cpp",
				"0:[1,7) meta.preprocessor.macro.cpp > keyword.control.directive.define.cpp",
				"0:[7,8) source.cpp > meta.preprocessor.macro.cpp",
				"0:[8,9) meta.preprocessor.macro.cpp > entity.name.function.preprocessor.cpp",
				"0:[9,10) source.cpp > meta.preprocessor.macro.cpp",
				"0:[10,11) meta.preprocessor.macro.cpp > constant.numeric.decimal.cpp",
				"0:[11,12) source.cpp > meta.preprocessor.macro.cpp",
				"0:[12,13) meta.preprocessor.macro.cpp > constant.character.escape.line-continuation.cpp",
				"1:[0,2) source.cpp > meta.preprocessor.macro.cpp",
				"1:[2,3) meta.preprocessor.macro.cpp > keyword.operator.arithmetic.cpp",
				"1:[3,4) source.cpp > meta.preprocessor.macro.cpp",
				"1:[4,5) meta.preprocessor.macro.cpp > constant.numeric.decimal.cpp",
				"2:[0,3) storage.type.primitive.cpp > storage.type.built-in.primitive.cpp",
				"2:[3,5) source.cpp",
				"2:[5,6) source.cpp > punctuation.terminator.statement.cpp",
			],

			runs
		);
	}

	[TestMethod]
	[DataRow("\n", DisplayName = "LineFeed")]
	[DataRow("\r\n", DisplayName = "CarriageReturnLineFeed")]
	[DataRow("\r", DisplayName = "LoneCarriageReturn")]
	public void DescribeRuns_LuaStringContinuation_KeepsTheNextLineInsideTheString(string terminator)
	{
		IReadOnlyList<string> runs = DescribeRuns("local s = \"abc\\" + terminator + "def\"" + terminator + "local t = 1", "lua");

		Assert.AreSequenceEqual(
			[
				"0:[0,5) source.lua > keyword.local.lua",
				"0:[5,6) source.lua",
				"0:[6,7) source.lua > variable.other.lua",
				"0:[7,8) source.lua",
				"0:[8,9) source.lua > keyword.operator.lua",
				"0:[9,10) source.lua",
				"0:[10,11) string.quoted.double.lua > punctuation.definition.string.begin.lua",
				"0:[11,14) source.lua > string.quoted.double.lua",
				"0:[14,15) string.quoted.double.lua > constant.character.escape.lua",
				"1:[0,3) source.lua > string.quoted.double.lua",
				"1:[3,4) string.quoted.double.lua > punctuation.definition.string.end.lua",
				"2:[0,5) source.lua > keyword.local.lua",
				"2:[5,6) source.lua",
				"2:[6,7) source.lua > variable.other.lua",
				"2:[7,8) source.lua",
				"2:[8,9) source.lua > keyword.operator.lua",
				"2:[9,10) source.lua",
				"2:[10,11) source.lua > constant.numeric.float.lua",
			],

			runs
		);
	}

	[TestMethod]
	public void DescribeRuns_CrLfDocumentWithBlockCommentAndLongString_PinsTheRuns()
	{
		IReadOnlyList<string> runs = DescribeRuns(
			"local x = 1 -- note\r\n--[[ block\r\nstill ]] y = 2\r\nlocal s = \"a b\"\r\n[[ long\r\nstring ]]\r\nreturn x",
			"lua"
		);

		Assert.AreSequenceEqual(
			[
				"0:[0,5) source.lua > keyword.local.lua",
				"0:[5,6) source.lua",
				"0:[6,7) source.lua > variable.other.lua",
				"0:[7,8) source.lua",
				"0:[8,9) source.lua > keyword.operator.lua",
				"0:[9,10) source.lua",
				"0:[10,11) source.lua > constant.numeric.float.lua",
				"0:[11,12) source.lua",
				"0:[12,14) comment.line.double-dash.lua > punctuation.definition.comment.lua",
				"0:[14,19) source.lua > comment.line.double-dash.lua",
				"1:[0,4) comment.block.lua > punctuation.definition.comment.begin.lua",
				"1:[4,10) source.lua > comment.block.lua",
				"2:[0,6) source.lua > comment.block.lua",
				"2:[6,8) comment.block.lua > punctuation.definition.comment.end.lua",
				"2:[8,9) source.lua",
				"2:[9,10) source.lua > variable.other.lua",
				"2:[10,11) source.lua",
				"2:[11,12) source.lua > keyword.operator.lua",
				"2:[12,13) source.lua",
				"2:[13,14) source.lua > constant.numeric.float.lua",
				"3:[0,5) source.lua > keyword.local.lua",
				"3:[5,6) source.lua",
				"3:[6,7) source.lua > variable.other.lua",
				"3:[7,8) source.lua",
				"3:[8,9) source.lua > keyword.operator.lua",
				"3:[9,10) source.lua",
				"3:[10,11) string.quoted.double.lua > punctuation.definition.string.begin.lua",
				"3:[11,14) source.lua > string.quoted.double.lua",
				"3:[14,15) string.quoted.double.lua > punctuation.definition.string.end.lua",
				"4:[0,2) string.quoted.other.multiline.lua > punctuation.definition.string.begin.lua",
				"4:[2,7) source.lua > string.quoted.other.multiline.lua",
				"5:[0,7) source.lua > string.quoted.other.multiline.lua",
				"5:[7,9) string.quoted.other.multiline.lua > punctuation.definition.string.end.lua",
				"6:[0,6) source.lua > keyword.control.lua",
				"6:[6,7) source.lua",
				"6:[7,8) source.lua > variable.other.lua",
			],

			runs
		);
	}

	[TestMethod]
	public void DescribeRuns_MixedTerminatorDocument_PinsTheRuns()
	{
		IReadOnlyList<string> runs = DescribeRuns("local a = 1\nlocal b = 2 -- c\r\nlocal c = [[x\ry]]\rlocal d = \"q\"\r\n--[[ k\nl ]] z", "lua");

		Assert.AreSequenceEqual(
			[
				"0:[0,5) source.lua > keyword.local.lua",
				"0:[5,6) source.lua",
				"0:[6,7) source.lua > variable.other.lua",
				"0:[7,8) source.lua",
				"0:[8,9) source.lua > keyword.operator.lua",
				"0:[9,10) source.lua",
				"0:[10,11) source.lua > constant.numeric.float.lua",
				"1:[0,5) source.lua > keyword.local.lua",
				"1:[5,6) source.lua",
				"1:[6,7) source.lua > variable.other.lua",
				"1:[7,8) source.lua",
				"1:[8,9) source.lua > keyword.operator.lua",
				"1:[9,10) source.lua",
				"1:[10,11) source.lua > constant.numeric.float.lua",
				"1:[11,12) source.lua",
				"1:[12,14) comment.line.double-dash.lua > punctuation.definition.comment.lua",
				"1:[14,16) source.lua > comment.line.double-dash.lua",
				"2:[0,5) source.lua > keyword.local.lua",
				"2:[5,6) source.lua",
				"2:[6,7) source.lua > variable.other.lua",
				"2:[7,8) source.lua",
				"2:[8,9) source.lua > keyword.operator.lua",
				"2:[9,10) source.lua",
				"2:[10,12) string.quoted.other.multiline.lua > punctuation.definition.string.begin.lua",
				"2:[12,13) source.lua > string.quoted.other.multiline.lua",
				"3:[0,1) source.lua > string.quoted.other.multiline.lua",
				"3:[1,3) string.quoted.other.multiline.lua > punctuation.definition.string.end.lua",
				"4:[0,5) source.lua > keyword.local.lua",
				"4:[5,6) source.lua",
				"4:[6,7) source.lua > variable.other.lua",
				"4:[7,8) source.lua",
				"4:[8,9) source.lua > keyword.operator.lua",
				"4:[9,10) source.lua",
				"4:[10,11) string.quoted.double.lua > punctuation.definition.string.begin.lua",
				"4:[11,12) source.lua > string.quoted.double.lua",
				"4:[12,13) string.quoted.double.lua > punctuation.definition.string.end.lua",
				"5:[0,4) comment.block.lua > punctuation.definition.comment.begin.lua",
				"5:[4,6) source.lua > comment.block.lua",
				"6:[0,2) source.lua > comment.block.lua",
				"6:[2,4) comment.block.lua > punctuation.definition.comment.end.lua",
				"6:[4,5) source.lua",
				"6:[5,6) source.lua > variable.other.lua",
			],

			runs
		);
	}

	internal static IReadOnlyList<string> DescribeRuns(string text, string languageId)
	{
		var document = new TextDocument(text);
		var lineList = new TextMateDocumentLineList(document);
		var model = new TMModel(lineList);

		try
		{
			model.SetGrammar(CreateGrammar(languageId));
			WaitForTokenization(model, lineList);

			var runs = new List<string>();

			for (int lineIndex = 0; lineIndex < document.LineCount; lineIndex++)
			{
				int lineLength = document.GetLineByNumber(lineIndex + 1).Length;
				List<TMToken> tokens = model.GetLineTokens(lineIndex);

				for (int i = 0; i < tokens.Count; i++)
				{
					int start = Math.Clamp(tokens[i].StartIndex, 0, lineLength);
					int end = i + 1 < tokens.Count ? Math.Clamp(tokens[i + 1].StartIndex, 0, lineLength) : lineLength;

					if (end <= start)
						continue;

					string scopes = string.Join(" > ", tokens[i].Scopes.TakeLast(2));
					runs.Add($"{lineIndex}:[{start},{end}) {scopes}");
				}
			}

			return runs;
		}
		finally
		{
			model.Dispose();
			lineList.Dispose();
		}
	}
}
