using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class ProgressParamsParserTests
{
	[TestMethod]
	[DataRow(
		"""{ "token": "t", "value": { "kind": "begin", "title": "Loading", "message": "m", "percentage": 5 } }""",
		"t",
		"begin",
		"Loading",
		"m",
		5
	)]
	[DataRow("""{ "token": 42, "value": { "kind": "report", "percentage": 60 } }""", "42", "report", null, null, 60)]
	[DataRow("""{ "token": "t", "value": { "kind": "end", "message": "done" } }""", "t", "end", null, "done", null)]
	[DataRow("""{ "token": "t", "value": { "kind": "report", "percentage": "half" } }""", "t", "report", null, null, null)]
	public void TryParse_ReadsAWorkDoneReport(string json, string token, string kind, string? title, string? message, int? percentage)
	{
		using var document = JsonDocument.Parse(json);

		Assert.IsTrue(ProgressParamsParser.TryParse(document.RootElement, out ProgressParams? progress));
		Assert.AreEqual(token, progress.Token);
		Assert.AreEqual(new WorkDoneProgressPayload(kind, title, message, percentage), progress.Value);
	}

	[TestMethod]
	[DataRow("""{ "value": { "kind": "begin" } }""", DisplayName = "No token")]
	[DataRow("""{ "token": "", "value": { "kind": "begin" } }""", DisplayName = "An empty token")]
	[DataRow("""{ "token": true, "value": { "kind": "begin" } }""", DisplayName = "A token that is neither a string nor an integer")]
	[DataRow("""{ "token": "t" }""", DisplayName = "No value")]
	[DataRow("""{ "token": "t", "value": [ 1, 2 ] }""", DisplayName = "A partial-result value")]
	[DataRow("""{ "token": "t", "value": { "kind": "unknown" } }""", DisplayName = "An unknown kind")]
	[DataRow("""{ "token": "t", "value": { "title": "x" } }""", DisplayName = "No kind")]
	[DataRow("[]", DisplayName = "Parameters that are not an object")]
	public void TryParse_RejectsWhatIsNoWorkDoneReport(string json)
	{
		using var document = JsonDocument.Parse(json);

		Assert.IsFalse(ProgressParamsParser.TryParse(document.RootElement, out ProgressParams? progress));
		Assert.IsNull(progress);
	}
}
