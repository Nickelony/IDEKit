using System.Text.Json;
using Nickelony.LanguageServer.Client.Protocol;
using static Nickelony.LanguageServer.Provider.Tests.PayloadTestHelpers;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class CodeActionResponseParserTests
{
	private static IReadOnlyList<CodeAction> Parse(CodeActionResponse? response, NegotiatedServerCapabilities? capabilities = null)
	{
		return CodeActionResponseParser.Parse(
			response,
			request: new CodeActionRequest(TestPaths.Script("test.lua"), "local a = 1", new TextRange(0, 0)),
			capabilities: capabilities ?? NegotiatedServerCapabilities.None,
			pathComparison: TestPaths.PathComparison
		);
	}

	[TestMethod]
	public void Parse_MapsEditActionsToTitlesKindsAndWorkspaceEdits()
	{
		string targetPath = TestPaths.Script("test.lua");

		IReadOnlyList<CodeAction> actions = Parse(
			DeserializeCodeActionResponse(
				new object[]
				{
					new
					{
						title = "Add semicolon",
						kind = "quickfix",
						isPreferred = true,

						edit = new
						{
							changes = new Dictionary<string, object[]>
							{
								[new Uri(targetPath).AbsoluteUri] =
								[
									new
									{
										range = new { start = new { line = 4, character = 0 }, end = new { line = 4, character = 0 } },
										newText = ";",
									},
								],
							},
						},
					},

					new
					{
						title = "Swap parameters",
						kind = "refactor.rewrite",

						edit = new
						{
							documentChanges = new object[]
							{
								new
								{
									textDocument = new { uri = new Uri(targetPath).AbsoluteUri },

									edits = new object[]
									{
										new
										{
											range = new { start = new { line = 1, character = 10 }, end = new { line = 1, character = 14 } },
											newText = "second",
										},
									},
								},
							},
						},
					},
				}
			)
		);

		Assert.HasCount(2, actions);

		CodeAction quickFix = actions[0];

		Assert.AreEqual("Add semicolon", quickFix.Title);
		Assert.AreEqual("quickfix", quickFix.Kind);
		Assert.IsTrue(quickFix.IsPreferred);
		Assert.HasCount(1, quickFix.Edit.DocumentEdits);
		Assert.AreEqual(targetPath, quickFix.Edit.DocumentEdits[0].DocumentId);
		Assert.AreEqual(new TextPositionRange(new TextPosition(4, 0), new TextPosition(4, 0)), quickFix.Edit.DocumentEdits[0].TextEdits[0].Range);
		Assert.AreEqual(";", quickFix.Edit.DocumentEdits[0].TextEdits[0].NewText);

		CodeAction refactor = actions[1];

		Assert.AreEqual("Swap parameters", refactor.Title);
		Assert.AreEqual("refactor.rewrite", refactor.Kind);
		Assert.IsFalse(refactor.IsPreferred);
		Assert.AreEqual("second", refactor.Edit.DocumentEdits[0].TextEdits[0].NewText);
	}

	[TestMethod]
	public void Parse_UnrepresentableEdits_DropOnlyThatAction()
	{
		string targetPath = TestPaths.Script("test.lua");
		string otherPath = TestPaths.Script("other.lua");

		IReadOnlyList<CodeAction> actions = Parse(
			DeserializeCodeActionResponse(
				new object[]
				{
					new
					{
						title = "Rename file",
						kind = "quickfix",

						edit = new
						{
							documentChanges = new object[]
							{
								new
								{
									kind = "rename",
									oldUri = new Uri(targetPath).AbsoluteUri,
									newUri = new Uri(otherPath).AbsoluteUri,
								},
							},
						},
					},

					new
					{
						title = "Unresolvable target",
						kind = "quickfix",

						edit = new
						{
							changes = new Dictionary<string, object[]>
							{
								["https://example.com/not-a-file.lua"] =
								[
									new
									{
										range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 1 } },
										newText = "x",
									},
								],
							},
						},
					},

					new
					{
						title = "Usable",
						kind = "quickfix",

						edit = new
						{
							changes = new Dictionary<string, object[]>
							{
								[new Uri(targetPath).AbsoluteUri] =
								[
									new
									{
										range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 1 } },
										newText = "x",
									},
								],
							},
						},
					},
				}
			)
		);

		// One action's unrepresentable edit drops that action only; the independent usable action stays.
		Assert.HasCount(1, actions);
		Assert.AreEqual("Usable", actions[0].Title);
	}

	[TestMethod]
	public void Parse_UnusablePayloads_AreSkipped()
	{
		var response = new CodeActionResponse([
			new CodeActionPayload { Title = "noEdit", Kind = "quickfix" },

			new CodeActionPayload
			{
				Title = "   ",
				Kind = "quickfix",

				Edit = new WorkspaceEditResponse(
					changes: new Dictionary<string, IReadOnlyList<TextEditPayload>?>
					{
						[new Uri(TestPaths.Script("test.lua")).AbsoluteUri] =
						[
							new TextEditPayload(new RangePayload(new PositionPayload(0, 0), new PositionPayload(0, 1)), "x"),
						],
					},

					documentChanges: null
				),
			},
		]);

		IReadOnlyList<CodeAction> actions = Parse(response);

		Assert.IsEmpty(actions);
	}

	[TestMethod]
	public void Parse_NullOrEmptyResponse_ReturnsEmptyList()
	{
		Assert.IsEmpty(Parse(null));
		Assert.IsEmpty(Parse(new CodeActionResponse()));
	}

	private static readonly NegotiatedServerCapabilities s_resolvingServer = new()
	{
		SupportsCodeActionResolve = true,
		ExecuteCommands = ["lua.setConfig"],
	};

	private static JsonElement Element(string json)
	{
		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}

	[TestMethod]
	public void Parse_ActionWithResolveData_IsKeptOnlyForAServerThatResolves()
	{
		var response = new CodeActionResponse([new CodeActionPayload { Title = "Add using", Data = Element("""{ "id": 1 }""") }]);

		Assert.IsEmpty(Parse(response));
		Assert.IsEmpty(Parse(response, new NegotiatedServerCapabilities { ExecuteCommands = ["lua.setConfig"] }));
		Assert.AreEqual("Add using", Parse(response, s_resolvingServer)[0].Title);
	}

	[TestMethod]
	public void Parse_EveryAction_CarriesTheOfferThatNamesTheRequestedDocument()
	{
		var response = new CodeActionResponse([new CodeActionPayload { Title = "Add using", Data = Element("""{ "id": 1 }""") }]);

		CodeAction action = Parse(response, s_resolvingServer)[0];

		var offer = (CodeActionOffer)action.Data!;

		Assert.AreEqual(TestPaths.Script("test.lua"), offer.DocumentId);
		Assert.AreEqual("local a = 1", offer.DocumentText);
		Assert.AreEqual("Add using", offer.Payload.Title);
		Assert.AreEqual(1, offer.Payload.Data?.GetProperty("id").GetInt32());
	}

	[TestMethod]
	public void Parse_CommandThatTheServerExecutes_IsMappedWithItsTitleAndArguments()
	{
		var response = new CodeActionResponse([
			new CodeActionPayload
			{
				Title = "Mark as defined global",
				Command = new CommandPayload("Set config", "lua.setConfig", [Element("""{ "key": "Lua.diagnostics.globals" }"""), Element("7")]),
			},
		]);

		CodeAction action = Parse(response, s_resolvingServer)[0];

		Assert.IsNull(action.Edit);
		Assert.AreEqual("Set config", action.Command!.Title);
		Assert.AreEqual("lua.setConfig", action.Command.Name);
		Assert.HasCount(2, action.Command.Arguments);
		Assert.AreEqual("Lua.diagnostics.globals", ((JsonElement)action.Command.Arguments[0]!).GetProperty("key").GetString());
		Assert.AreEqual(7, ((JsonElement)action.Command.Arguments[1]!).GetInt32());
	}

	[TestMethod]
	public void Parse_CommandWithoutATitle_TakesTheTitleOfTheAction()
	{
		var response = new CodeActionResponse([
			new CodeActionPayload { Title = "Mark as defined global", Command = new CommandPayload(null, "lua.setConfig", null) },
		]);

		CodeAction action = Parse(response, s_resolvingServer)[0];

		Assert.AreEqual("Mark as defined global", action.Command!.Title);
		Assert.IsEmpty(action.Command.Arguments);
	}

	[TestMethod]
	public void Parse_CommandThatTheServerDoesNotExecute_DropsTheWholeAction()
	{
		var response = new CodeActionResponse([
			new CodeActionPayload { Title = "Nested", Command = new CommandPayload("Nested", "roslyn.client.nestedCodeAction", [Element("{}")]) },

			new CodeActionPayload
			{
				Title = "Fix",
				Data = Element("{}"),
				Command = new CommandPayload("Fix", "roslyn.client.fixAllCodeAction", null),
			},

			new CodeActionPayload { Title = "Mark as defined global", Command = new CommandPayload("Set config", "lua.setConfig", null) },
		]);

		IReadOnlyList<CodeAction> actions = Parse(response, s_resolvingServer);

		Assert.HasCount(1, actions);
		Assert.AreEqual("Mark as defined global", actions[0].Title);
	}

	[TestMethod]
	public void Parse_ActionWithAnEditAndACommand_KeepsBoth()
	{
		string targetPath = TestPaths.Script("test.lua");

		var response = new CodeActionResponse([
			new CodeActionPayload
			{
				Title = "Fix and configure",
				Command = new CommandPayload("Set config", "lua.setConfig", null),

				Edit = new WorkspaceEditResponse(
					changes: new Dictionary<string, IReadOnlyList<TextEditPayload>?>
					{
						[new Uri(targetPath).AbsoluteUri] =
						[
							new TextEditPayload(new RangePayload(new PositionPayload(0, 0), new PositionPayload(0, 1)), "x"),
						],
					},

					documentChanges: null
				),
			},
		]);

		CodeAction action = Parse(response, s_resolvingServer)[0];

		Assert.IsNotNull(action.Edit);
		Assert.AreEqual("lua.setConfig", action.Command!.Name);
	}

	[TestMethod]
	public void ParseResolved_ReturnsTheActionWithItsEditAndKeepsTheOffer()
	{
		string targetPath = TestPaths.Script("test.lua");

		var offered = new CodeActionPayload
		{
			Title = "Add using",
			Kind = "quickfix",
			IsPreferred = true,
			Data = Element("""{ "id": 1 }"""),
		};

		var offer = new CodeActionOffer(offered, targetPath, "local a = 1");

		CodeActionPayload resolved = offered with
		{
			Edit = new WorkspaceEditResponse(
				changes: new Dictionary<string, IReadOnlyList<TextEditPayload>?>
				{
					[new Uri(targetPath).AbsoluteUri] =
					[
						new TextEditPayload(new RangePayload(new PositionPayload(0, 0), new PositionPayload(0, 0)), "using X;\n"),
					],
				},

				documentChanges: null
			),
		};

		CodeAction? action = CodeActionResponseParser.ParseResolved(
			resolved,
			offer,
			capabilities: s_resolvingServer,
			pathComparison: TestPaths.PathComparison
		);

		Assert.IsNotNull(action);
		Assert.AreEqual("using X;\n", action.Edit!.DocumentEdits[0].TextEdits[0].NewText);
		Assert.AreSame(offer, action.Data);
	}

	[TestMethod]
	public void ParseResolved_TakesTheTitleTheKindAndThePreferredFlagTheAnswerOmitsFromTheOffer()
	{
		string targetPath = TestPaths.Script("test.lua");

		var offered = new CodeActionPayload
		{
			Title = "Add using",
			Kind = "quickfix",
			IsPreferred = true,
			Data = Element("{}"),
		};

		var resolved = new CodeActionPayload
		{
			Edit = new WorkspaceEditResponse(
				changes: new Dictionary<string, IReadOnlyList<TextEditPayload>?>
				{
					[new Uri(targetPath).AbsoluteUri] =
					[
						new TextEditPayload(new RangePayload(new PositionPayload(0, 0), new PositionPayload(0, 0)), "x"),
					],
				},

				documentChanges: null
			),
		};

		CodeAction? action = CodeActionResponseParser.ParseResolved(
			resolved,
			offer: new CodeActionOffer(offered, targetPath, "text"),
			capabilities: s_resolvingServer,
			pathComparison: TestPaths.PathComparison
		);

		Assert.AreEqual("Add using", action?.Title);
		Assert.AreEqual("quickfix", action?.Kind);
		Assert.IsTrue(action?.IsPreferred);
	}

	[TestMethod]
	public void ParseResolved_AnswerWithoutAnEditOrACommandTheServerExecutes_ReturnsNull()
	{
		var offered = new CodeActionPayload { Title = "Add using", Data = Element("{}") };
		var offer = new CodeActionOffer(offered, TestPaths.Script("test.lua"), "text");

		Assert.IsNull(
			CodeActionResponseParser.ParseResolved(
				resolved: offered,
				offer,
				capabilities: s_resolvingServer,
				pathComparison: TestPaths.PathComparison
			)
		);

		CodeActionPayload clientSide = offered with { Command = new CommandPayload("Nested", "roslyn.client.nestedCodeAction", null) };

		Assert.IsNull(
			CodeActionResponseParser.ParseResolved(
				resolved: clientSide,
				offer,
				capabilities: s_resolvingServer,
				pathComparison: TestPaths.PathComparison
			)
		);
	}
}
