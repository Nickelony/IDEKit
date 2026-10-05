using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>Covers the payloads of the server requests and notifications that reach a host hook.</summary>
[TestClass]
public sealed class ServerRequestPayloadScenarioTests
{
	[TestMethod]
	public void Deserialize_ShowMessageRequestParams_ReadsTheTypeTheMessageAndTheActions()
	{
		ShowMessageRequestParams parameters = WireJsonTestHelpers.Deserialize<ShowMessageRequestParams>(
			"""{ "type": 3, "message": "Reload?", "actions": [ { "title": "Yes" }, { "title": "No" } ] }"""
		);

		Assert.AreEqual(MessageType.Information, parameters.Type);
		Assert.AreEqual("Reload?", parameters.Message);
		Assert.AreSequenceEqual(["Yes", "No"], parameters.Actions!.Select(static action => action.Title));
	}

	[TestMethod]
	public void Deserialize_ShowMessageRequestParamsWithoutActions_LeavesThemNull() =>
		Assert.IsNull(WireJsonTestHelpers.Deserialize<ShowMessageRequestParams>("""{ "type": 2, "message": "x" }""").Actions);

	[TestMethod]
	public void Serialize_MessageActionItem_WritesItsTitle() =>
		Assert.AreEqual("""{"title":"Yes"}""", WireJsonTestHelpers.Serialize(new MessageActionItemPayload("Yes")));

	[TestMethod]
	public void Deserialize_ApplyWorkspaceEditParams_ReadsTheLabelAndTheEdit()
	{
		ApplyWorkspaceEditParams parameters = WireJsonTestHelpers.Deserialize<ApplyWorkspaceEditParams>(
			"""
			{
			  "label": "Remove trailing spaces",
			  "edit": {
			    "documentChanges": [
			      {
			        "textDocument": { "uri": "file:///C:/Workspace/a.lua", "version": 3 },
			        "edits": [ { "range": { "start": { "line": 0, "character": 3 }, "end": { "line": 0, "character": 4 } }, "newText": "" } ]
			      }
			    ]
			  }
			}
			"""
		);

		Assert.AreEqual("Remove trailing spaces", parameters.Label);
		Assert.HasCount(1, parameters.Edit.DocumentChanges!);
		Assert.AreEqual("file:///C:/Workspace/a.lua", parameters.Edit.DocumentChanges[0].TextDocument?.Uri);
	}

	[TestMethod]
	public void Serialize_ApplyWorkspaceEditResponse_OmitsAnAbsentFailureReason()
	{
		Assert.AreEqual("""{"applied":true}""", WireJsonTestHelpers.Serialize(new ApplyWorkspaceEditResponse(Applied: true)));

		Assert.AreEqual(
			"""{"applied":false,"failureReason":"busy"}""",
			WireJsonTestHelpers.Serialize(new ApplyWorkspaceEditResponse(Applied: false, "busy"))
		);
	}

	[TestMethod]
	public void Serialize_ExecuteCommandParams_WritesTheCommandAndItsArgumentsInOrder()
	{
		using var first = JsonDocument.Parse("""{ "uri": "file:///a.lua" }""");
		using var second = JsonDocument.Parse("7");

		string json = WireJsonTestHelpers.Serialize(new ExecuteCommandParams("lua.removeSpace", [first.RootElement, second.RootElement]));

		Assert.AreEqual("""{"command":"lua.removeSpace","arguments":[{"uri":"file:///a.lua"},7]}""", json);
		Assert.AreEqual("""{"command":"lua.removeSpace"}""", WireJsonTestHelpers.Serialize(new ExecuteCommandParams("lua.removeSpace")));
	}

	[TestMethod]
	public void Serialize_CodeActionContextPayload_WritesTheKindFilterOnlyWhenGiven()
	{
		Assert.AreEqual("""{"diagnostics":[]}""", WireJsonTestHelpers.Serialize(new CodeActionContextPayload([])));

		Assert.AreEqual(
			"""{"diagnostics":[],"only":["quickfix","refactor"]}""",
			WireJsonTestHelpers.Serialize(new CodeActionContextPayload([], ["quickfix", "refactor"]))
		);
	}

	[TestMethod]
	public void Serialize_CodeActionPayload_WritesOnlyTheMembersItCarries()
	{
		using var data = JsonDocument.Parse("""{ "id": 12 }""");

		string json = WireJsonTestHelpers.Serialize(
			new CodeActionPayload
			{
				Title = "Add using",
				Kind = "quickfix",
				Data = data.RootElement,
			}
		);

		Assert.AreEqual("""{"title":"Add using","kind":"quickfix","data":{"id":12}}""", json);
	}

	[TestMethod]
	public void Serialize_CodeActionPayload_ReadsBackAsTheSameAction()
	{
		using var data = JsonDocument.Parse("""{ "id": 12 }""");

		var action = new CodeActionPayload
		{
			Title = "Add using",
			Kind = "quickfix",
			IsPreferred = true,
			Data = data.RootElement,
		};

		CodeActionPayload roundTripped = WireJsonTestHelpers.Deserialize<CodeActionPayload>(WireJsonTestHelpers.Serialize(action));

		Assert.AreEqual("Add using", roundTripped.Title);
		Assert.IsTrue(roundTripped.IsPreferred);
		Assert.AreEqual(12, roundTripped.Data?.GetProperty("id").GetInt32());
		Assert.IsNull(roundTripped.Edit);
	}
}
