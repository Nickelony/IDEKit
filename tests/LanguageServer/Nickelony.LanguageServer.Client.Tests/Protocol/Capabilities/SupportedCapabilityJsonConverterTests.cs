namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class SupportedCapabilityJsonConverterTests
{
	[TestMethod]
	[DataRow("true", true, DisplayName = "A boolean true")]
	[DataRow("false", false, DisplayName = "A boolean false")]
	[DataRow("{}", true, DisplayName = "An empty options object")]
	[DataRow("""{ "workDoneProgress": true }""", true, DisplayName = "An options object with members")]
	[DataRow("null", false, DisplayName = "A JSON null")]
	[DataRow("1", false, DisplayName = "A number")]
	[DataRow("\"yes\"", false, DisplayName = "A string")]
	[DataRow("[true]", false, DisplayName = "An array")]
	public void Read_ReportsWhetherTheServerAdvertisesTheCapability(string json, bool expectedSupport) =>
		Assert.AreEqual(expectedSupport, WireJsonTestHelpers.Deserialize<SupportedCapability>(json).IsSupported);

	[TestMethod]
	[DataRow(true, "true")]
	[DataRow(false, "false")]
	public void Write_NormalizesTheCapabilityToABoolean(bool isSupported, string expectedJson) =>
		Assert.AreEqual(expectedJson, WireJsonTestHelpers.Serialize(new SupportedCapability(isSupported)));

	[TestMethod]
	public void Read_LeavesTheReaderAfterTheValue_SoLaterMembersStillBind()
	{
		ServerCapabilities capabilities = WireJsonTestHelpers.Deserialize<ServerCapabilities>(
			"""{ "hoverProvider": { "workDoneProgress": true }, "definitionProvider": true, "referencesProvider": [1, 2] }"""
		);

		Assert.IsTrue(capabilities.HoverProvider?.IsSupported);
		Assert.IsTrue(capabilities.DefinitionProvider?.IsSupported);
		Assert.IsFalse(capabilities.ReferencesProvider?.IsSupported);
	}
}
