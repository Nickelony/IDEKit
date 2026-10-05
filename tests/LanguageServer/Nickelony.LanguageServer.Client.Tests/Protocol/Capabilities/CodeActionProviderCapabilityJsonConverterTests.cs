namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class CodeActionProviderCapabilityJsonConverterTests
{
	[TestMethod]
	[DataRow("true", true, null, DisplayName = "A boolean true")]
	[DataRow("false", false, null, DisplayName = "A boolean false")]
	[DataRow("{}", true, null, DisplayName = "An empty options object")]
	[DataRow("""{ "resolveProvider": true }""", true, true, DisplayName = "An options object that resolves")]
	[DataRow("""{ "resolveProvider": false }""", true, false, DisplayName = "An options object that declines to resolve")]
	[DataRow("""{ "resolveProvider": "yes" }""", true, null, DisplayName = "An options object with a non-boolean resolve flag")]
	[DataRow("""{ "codeActionKinds": ["quickfix"] }""", true, null, DisplayName = "An options object with other members")]
	[DataRow("null", false, null, DisplayName = "A JSON null")]
	[DataRow("5", false, null, DisplayName = "A number")]
	[DataRow("\"quickfix\"", false, null, DisplayName = "A string")]
	[DataRow("[true]", false, null, DisplayName = "An array")]
	public void Read_ReportsSupportAndTheResolveFlag(string json, bool expectedSupport, bool? expectedResolveProvider)
	{
		CodeActionProviderCapability capability = WireJsonTestHelpers.Deserialize<CodeActionProviderCapability>(json);

		Assert.AreEqual(expectedSupport, capability.IsSupported);
		Assert.AreEqual(expectedResolveProvider, capability.ResolveProvider);
	}

	[TestMethod]
	[DataRow(true, "true")]
	[DataRow(false, "false")]
	public void Write_NormalizesTheCapabilityToABoolean(bool isSupported, string expectedJson) =>
		Assert.AreEqual(expectedJson, WireJsonTestHelpers.Serialize(new CodeActionProviderCapability(isSupported, ResolveProvider: true)));

	[TestMethod]
	public void Read_LeavesTheReaderAfterTheValue_SoLaterMembersStillBind()
	{
		ServerCapabilities capabilities = WireJsonTestHelpers.Deserialize<ServerCapabilities>(
			"""{ "codeActionProvider": { "resolveProvider": true, "codeActionKinds": ["refactor"] }, "hoverProvider": true }"""
		);

		Assert.AreEqual(new CodeActionProviderCapability(IsSupported: true, ResolveProvider: true), capabilities.CodeActionProvider);
		Assert.IsTrue(capabilities.HoverProvider?.IsSupported);
	}
}
