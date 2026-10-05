namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class TextDocumentSyncCapabilityJsonConverterTests
{
	[TestMethod]
	[DataRow("0", TextDocumentSyncKind.None, DisplayName = "The number 0")]
	[DataRow("1", TextDocumentSyncKind.Full, DisplayName = "The number 1")]
	[DataRow("2", TextDocumentSyncKind.Incremental, DisplayName = "The number 2")]
	[DataRow("3", TextDocumentSyncKind.None, DisplayName = "A number past the known kinds")]
	[DataRow("-1", TextDocumentSyncKind.None, DisplayName = "A negative number")]
	[DataRow("1.5", TextDocumentSyncKind.None, DisplayName = "A number that is not an integer")]
	[DataRow("""{ "change": 1 }""", TextDocumentSyncKind.Full, DisplayName = "An options object with full sync")]
	[DataRow("""{ "change": 2, "openClose": true }""", TextDocumentSyncKind.Incremental, DisplayName = "An options object with incremental sync")]
	[DataRow("""{ "change": 7 }""", TextDocumentSyncKind.None, DisplayName = "An options object with an unknown kind")]
	[DataRow("""{ "change": "invalid" }""", TextDocumentSyncKind.None, DisplayName = "An options object with a non-numeric change")]
	[DataRow("{}", TextDocumentSyncKind.None, DisplayName = "An options object without a change member")]
	[DataRow("null", TextDocumentSyncKind.None, DisplayName = "A JSON null")]
	[DataRow("true", TextDocumentSyncKind.None, DisplayName = "A boolean")]
	[DataRow("\"full\"", TextDocumentSyncKind.None, DisplayName = "A string")]
	[DataRow("[1]", TextDocumentSyncKind.None, DisplayName = "An array")]
	public void Read_MapsTheAdvertisedShapeToASyncKind(string json, TextDocumentSyncKind expectedKind) =>
		Assert.AreEqual(expectedKind, WireJsonTestHelpers.Deserialize<TextDocumentSyncCapability>(json).Kind);

	[TestMethod]
	[DataRow(TextDocumentSyncKind.None, "0")]
	[DataRow(TextDocumentSyncKind.Full, "1")]
	[DataRow(TextDocumentSyncKind.Incremental, "2")]
	public void Write_NormalizesTheCapabilityToTheSyncKindNumber(TextDocumentSyncKind kind, string expectedJson) =>
		Assert.AreEqual(expectedJson, WireJsonTestHelpers.Serialize(new TextDocumentSyncCapability(kind)));

	[TestMethod]
	public void Read_LeavesTheReaderAfterTheValue_SoLaterMembersStillBind()
	{
		ServerCapabilities capabilities = WireJsonTestHelpers.Deserialize<ServerCapabilities>(
			"""{ "textDocumentSync": [1, 2], "hoverProvider": true }"""
		);

		Assert.AreEqual(TextDocumentSyncKind.None, capabilities.TextDocumentSync?.Kind);
		Assert.IsTrue(capabilities.HoverProvider?.IsSupported);
	}
}
