namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class SemanticTokensFullCapabilityJsonConverterTests
{
	[TestMethod]
	[DataRow("true", true, DisplayName = "A boolean true")]
	[DataRow("false", false, DisplayName = "A boolean false")]
	[DataRow("{}", true, DisplayName = "An empty options object")]
	[DataRow("""{ "delta": true }""", true, DisplayName = "An options object that supports deltas")]
	[DataRow("null", false, DisplayName = "A JSON null")]
	[DataRow("[]", false, DisplayName = "An array")]
	[DataRow("5", false, DisplayName = "A number")]
	[DataRow("\"full\"", false, DisplayName = "A string")]
	public void Read_ReportsWhetherFullRequestsAreSupported(string json, bool expectedSupport) =>
		Assert.AreEqual(expectedSupport, WireJsonTestHelpers.Deserialize<SemanticTokensFullCapability>(json).IsSupported);

	[TestMethod]
	[DataRow(true, "true")]
	[DataRow(false, "false")]
	public void Write_NormalizesTheCapabilityToABoolean(bool isSupported, string expectedJson) =>
		Assert.AreEqual(expectedJson, WireJsonTestHelpers.Serialize(new SemanticTokensFullCapability(isSupported)));

	[TestMethod]
	public void Read_InsideTheProvider_KeepsTheLegendThatFollowsAMalformedFullMember()
	{
		SemanticTokensProviderCapability provider = WireJsonTestHelpers.Deserialize<SemanticTokensProviderCapability>(
			"""{ "full": [], "legend": { "tokenTypes": ["function"], "tokenModifiers": ["declaration"] } }"""
		);

		Assert.IsFalse(provider.Full?.IsSupported);
		Assert.AreSequenceEqual(["function"], provider.Legend?.TokenTypes);
		Assert.AreSequenceEqual(["declaration"], provider.Legend?.TokenModifiers);
	}
}
