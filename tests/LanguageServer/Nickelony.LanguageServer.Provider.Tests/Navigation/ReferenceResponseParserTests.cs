using Nickelony.IDEKit.LanguageFeatures.Navigation;
using static Nickelony.LanguageServer.Provider.Tests.PayloadTestHelpers;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class ReferenceResponseParserTests
{
	[TestMethod]
	public void Parse_ParsesFileReferenceRanges()
	{
		string targetPath = TestPaths.Script("references.lua");

		IReadOnlyList<ReferenceLocation> locations = ReferenceResponseParser.Parse(
			DeserializeReferenceLocations(
				new object[]
				{
					new
					{
						uri = new Uri(targetPath).AbsoluteUri,
						range = new { start = new { line = 2, character = 4 }, end = new { line = 2, character = 9 } },
					},

					new
					{
						uri = "https://example.com/not-a-file.lua",
						range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 1 } },
					},
				}
			)
		);

		Assert.HasCount(1, locations);
		Assert.AreEqual(targetPath, locations[0].DocumentId);
		Assert.AreEqual(new TextPositionRange(new TextPosition(2, 4), new TextPosition(2, 9)), locations[0].Range);
	}

	[TestMethod]
	public void Parse_IgnoresNegativeProtocolRanges()
	{
		string targetPath = TestPaths.Script("references.lua");

		IReadOnlyList<ReferenceLocation> locations = ReferenceResponseParser.Parse(
			DeserializeReferenceLocations(
				new object[]
				{
					new
					{
						uri = new Uri(targetPath).AbsoluteUri,
						range = new { start = new { line = -1, character = 4 }, end = new { line = 2, character = 9 } },
					},
				}
			)
		);

		Assert.IsEmpty(locations);
	}
}
