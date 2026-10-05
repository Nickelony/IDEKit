using Nickelony.IDEKit.LanguageFeatures.Navigation;
using static Nickelony.LanguageServer.Provider.Tests.PayloadTestHelpers;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class DefinitionResponseParserTests
{
	[TestMethod]
	public void Parse_UsesFirstEntryFromMultiLocationResponse()
	{
		string firstPath = TestPaths.Script("first.lua");
		string secondPath = TestPaths.Script("second.lua");

		DefinitionLocation? location = DefinitionResponseParser.Parse(
			DeserializeDefinitionResponse(
				new object[]
				{
					new
					{
						uri = new Uri(firstPath).AbsoluteUri,
						range = new { start = new { line = 2, character = 4 }, end = new { line = 2, character = 10 } },
					},

					new
					{
						uri = new Uri(secondPath).AbsoluteUri,
						range = new { start = new { line = 8, character = 1 }, end = new { line = 8, character = 5 } },
					},
				}
			)
		);

		Assert.IsNotNull(location);
		Assert.AreEqual(firstPath, location.DocumentId);
		Assert.AreEqual(new TextPositionRange(new TextPosition(2, 4), new TextPosition(2, 10)), location.TargetRange);
		Assert.IsNull(location.SelectionRange);
	}

	[TestMethod]
	public void Parse_UsesTargetSelectionRangeFromLocationLink()
	{
		string targetPath = TestPaths.Script("linked.lua");

		DefinitionLocation? location = DefinitionResponseParser.Parse(
			DeserializeDefinitionResponse(
				new
				{
					targetUri = new Uri(targetPath).AbsoluteUri,
					targetSelectionRange = new { start = new { line = 4, character = 2 }, end = new { line = 4, character = 9 } },
				}
			)
		);

		Assert.IsNotNull(location);
		Assert.AreEqual(targetPath, location.DocumentId);

		// A location link without a distinct target range uses the selection range as the target.
		var expectedRange = new TextPositionRange(new TextPosition(4, 2), new TextPosition(4, 9));

		Assert.AreEqual(expectedRange, location.TargetRange);
		Assert.AreEqual(expectedRange, location.SelectionRange);
	}

	[TestMethod]
	public void Parse_KeepsDistinctTargetAndSelectionRanges()
	{
		string targetPath = TestPaths.Script("linked.lua");

		DefinitionLocation? location = DefinitionResponseParser.Parse(
			DeserializeDefinitionResponse(
				new
				{
					targetUri = new Uri(targetPath).AbsoluteUri,
					targetRange = new { start = new { line = 4, character = 0 }, end = new { line = 8, character = 3 } },
					targetSelectionRange = new { start = new { line = 4, character = 2 }, end = new { line = 4, character = 9 } },
				}
			)
		);

		Assert.IsNotNull(location);
		Assert.AreEqual(targetPath, location.DocumentId);
		Assert.AreEqual(new TextPositionRange(new TextPosition(4, 0), new TextPosition(8, 3)), location.TargetRange);
		Assert.AreEqual(new TextPositionRange(new TextPosition(4, 2), new TextPosition(4, 9)), location.SelectionRange);
	}

	[TestMethod]
	public void Parse_ReturnsNullForNegativeTargetPosition()
	{
		string targetPath = TestPaths.Script("linked.lua");

		DefinitionLocation? location = DefinitionResponseParser.Parse(
			DeserializeDefinitionResponse(
				new
				{
					targetUri = new Uri(targetPath).AbsoluteUri,
					targetSelectionRange = new { start = new { line = -1, character = 2 }, end = new { line = 4, character = 9 } },
				}
			)
		);

		Assert.IsNull(location);
	}
}
