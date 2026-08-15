namespace Nickelony.LanguageServer.Lua.Tests;

public sealed partial class ResponseParserTests
{
	[TestMethod]
	public void ParseCodeActions_MapsEditActionsToTitlesKindsAndWorkspaceEdits()
	{
		string targetPath = TestPaths.Script("test.lua");

		IReadOnlyList<TextCodeAction> actions = ResponseParser.ParseCodeActions(
			DeserializeCodeActionsResponse(new object[]
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
									range = new
									{
										start = new { line = 4, character = 0 },
										end = new { line = 4, character = 0 }
									},
									newText = ";"
								}
							]
						}
					}
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
										range = new
										{
											start = new { line = 1, character = 10 },
											end = new { line = 1, character = 14 }
										},
										newText = "second"
									}
								}
							}
						}
					}
				}
			}));

		Assert.AreEqual(2, actions.Count);

		TextCodeAction quickFix = actions[0];

		Assert.AreEqual("Add semicolon", quickFix.Title);
		Assert.AreEqual("quickfix", quickFix.Kind);
		Assert.IsTrue(quickFix.IsPreferred);
		Assert.AreEqual(1, quickFix.Edit.DocumentEdits.Count);
		Assert.AreEqual(targetPath, quickFix.Edit.DocumentEdits[0].FilePath);
		Assert.AreEqual(new TextPositionRange(new TextPosition(4, 0), new TextPosition(4, 0)), quickFix.Edit.DocumentEdits[0].TextEdits[0].Range);
		Assert.AreEqual(";", quickFix.Edit.DocumentEdits[0].TextEdits[0].NewText);

		TextCodeAction refactor = actions[1];

		Assert.AreEqual("Swap parameters", refactor.Title);
		Assert.AreEqual("refactor.rewrite", refactor.Kind);
		Assert.IsFalse(refactor.IsPreferred);
		Assert.AreEqual("second", refactor.Edit.DocumentEdits[0].TextEdits[0].NewText);
	}

	[TestMethod]
	public void ParseCodeActions_UnrepresentableEdits_DropOnlyThatAction()
	{
		string targetPath = TestPaths.Script("test.lua");
		string otherPath = TestPaths.Script("other.lua");

		IReadOnlyList<TextCodeAction> actions = ResponseParser.ParseCodeActions(
			DeserializeCodeActionsResponse(new object[]
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
								newUri = new Uri(otherPath).AbsoluteUri
							}
						}
					}
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
									range = new
									{
										start = new { line = 0, character = 0 },
										end = new { line = 0, character = 1 }
									},
									newText = "x"
								}
							]
						}
					}
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
									range = new
									{
										start = new { line = 0, character = 0 },
										end = new { line = 0, character = 1 }
									},
									newText = "x"
								}
							]
						}
					}
				}
			}));

		// One action's unrepresentable edit drops that action only; the independent usable action stays.
		Assert.AreEqual(1, actions.Count);
		Assert.AreEqual("Usable", actions[0].Title);
	}

	[TestMethod]
	public void ParseCodeActions_UnusablePayloads_AreSkipped()
	{
		var response = new CodeActionsResponse(
		[
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
							new TextEditPayload(new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 1)), "x")
						]
					},
					documentChanges: null)
			}
		]);

		IReadOnlyList<TextCodeAction> actions = ResponseParser.ParseCodeActions(response);

		Assert.AreEqual(0, actions.Count);
	}

	[TestMethod]
	public void ParseCodeActions_NullOrEmptyResponse_ReturnsEmptyList()
	{
		Assert.AreEqual(0, ResponseParser.ParseCodeActions(null).Count);
		Assert.AreEqual(0, ResponseParser.ParseCodeActions(new CodeActionsResponse()).Count);
	}
}
