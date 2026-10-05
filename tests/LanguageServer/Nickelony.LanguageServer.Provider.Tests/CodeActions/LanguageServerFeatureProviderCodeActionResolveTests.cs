using System.Text.Json;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers the code actions that a server offers without an edit: the data-only action that <c>codeAction/resolve</c> resolves, and the command action
/// that <c>workspace/executeCommand</c> runs.
/// </summary>
[TestClass]
public sealed class LanguageServerFeatureProviderCodeActionResolveTests
{
	private const string Content = "local value = List<int>\nprint(value)\n";

	private static readonly string s_filePath = TestPaths.Script("test.test");

	private static string FileUri => new Uri(s_filePath).AbsoluteUri;

	private static object UsingAction()
	{
		return new
		{
			title = "using System.Collections.Generic;",
			kind = "quickfix",
			isPreferred = true,
			data = new { id = 17, path = "x" },
		};
	}

	private static JsonElement ResolvedUsingAction(string newText = "using System.Collections.Generic;\n")
	{
		return JsonSerializer.SerializeToElement(
			new
			{
				title = "using System.Collections.Generic;",
				kind = "quickfix",
				data = new { id = 17, path = "x" },

				edit = new
				{
					documentChanges = new object[]
					{
						new
						{
							textDocument = new { uri = FileUri, version = 1 },

							edits = new object[]
							{
								new { range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 0 } }, newText },
							},
						},
					},
				},
			}
		);
	}

	private async Task<(
		FakeStatefulLanguageServerClient Client,
		LanguageServerFeatureProvider Provider,
		IReadOnlyList<CodeAction> Actions
	)> OfferAsync(
		object[] serverActions,
		bool supportsResolve = true,
		string[]? commands = null,
		LanguageServerFeatureProviderOptions? options = null
	)
	{
		var client = new FakeStatefulLanguageServerClient
		{
			SupportsCodeActionResolve = supportsResolve,
			ExecuteCommands = commands ?? [],
			CodeActionResponse = JsonSerializer.SerializeToElement(serverActions),
		};

		LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client, options);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		IReadOnlyList<CodeAction> actions = await provider.GetCodeActionsAsync(
			new CodeActionRequest(s_filePath, Content, new TextRange(14, 0)),
			TestContext.CancellationToken
		);

		return (client, provider, actions);
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_KeepsAnActionWithResolveDataAndNoEdit()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider, IReadOnlyList<CodeAction> actions) = await OfferAsync([
			UsingAction(),
		]);

		using (client)
		{
			using (provider)
			{
				Assert.HasCount(1, actions);
				Assert.AreEqual("using System.Collections.Generic;", actions[0].Title);
				Assert.IsTrue(actions[0].IsPreferred);
				Assert.IsNull(actions[0].Edit);
				Assert.IsNull(actions[0].Command);
				Assert.IsNotNull(actions[0].Data);
			}
		}
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_DropsAnActionWithResolveDataWhenTheServerCannotResolve()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider, IReadOnlyList<CodeAction> actions) = await OfferAsync(
			[UsingAction()],
			supportsResolve: false
		);

		using (client)
		{
			using (provider)
				Assert.IsEmpty(actions);
		}
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_ListsTheActionsWithoutSendingAResolveRequest()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider, IReadOnlyList<CodeAction> actions) = await OfferAsync([
			UsingAction(),
			new { title = "Other", data = new { id = 18 } },
		]);

		using (client)
		{
			using (provider)
			{
				Assert.HasCount(2, actions);
				Assert.AreEqual(0, client.GetSentMethodCount("codeAction/resolve"));
			}
		}
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_KeepsACommandActionWhoseCommandTheServerExecutes()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider, IReadOnlyList<CodeAction> actions) = await OfferAsync(
			[
				new
				{
					title = "Mark as defined global",
					kind = "quickfix",

					command = new
					{
						title = "Set config",
						command = "lua.setConfig",
						arguments = new object[] { new { key = "Lua.diagnostics.globals", value = "prnt" } },
					},
				},
			],

			commands: ["lua.setConfig"]
		);

		using (client)
		{
			using (provider)
			{
				Assert.HasCount(1, actions);
				Assert.IsNull(actions[0].Edit);

				CodeActionCommand command = actions[0].Command!;

				Assert.AreEqual("Set config", command.Title);
				Assert.AreEqual("lua.setConfig", command.Name);
				Assert.HasCount(1, command.Arguments);
				Assert.AreEqual("prnt", ((JsonElement)command.Arguments[0]!).GetProperty("value").GetString());
			}
		}
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_DropsAnActionWhoseCommandTheServerDoesNotExecute()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider, IReadOnlyList<CodeAction> actions) = await OfferAsync(
			[
				new
				{
					title = "Suppress or Configure issues",

					command = new
					{
						title = "Nested",
						command = "roslyn.client.nestedCodeAction",
						arguments = new object[] { new { id = 1 } },
					},
				},

				UsingAction(),
			],

			commands: ["lua.setConfig"]
		);

		using (client)
		{
			using (provider)
			{
				Assert.HasCount(1, actions);
				Assert.AreEqual("using System.Collections.Generic;", actions[0].Title);
			}
		}
	}

	[TestMethod]
	public async Task ResolveAsync_SendsTheOfferedActionAndReturnsItWithItsEdit()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider, IReadOnlyList<CodeAction> actions) = await OfferAsync([
			UsingAction(),
		]);

		using (client)
		{
			using (provider)
			{
				client.CodeActionResolveResponse = ResolvedUsingAction();

				CodeAction resolved = await ((ICodeActionResolver)provider).ResolveAsync(actions[0], TestContext.CancellationToken);

				JsonElement sent = client.GetLastRequestParameters("codeAction/resolve");

				Assert.AreEqual("using System.Collections.Generic;", sent.GetProperty("title").GetString());
				Assert.AreEqual(17, sent.GetProperty("data").GetProperty("id").GetInt32());
				Assert.IsFalse(sent.TryGetProperty("edit", out _));

				Assert.AreEqual("using System.Collections.Generic;", resolved.Title);
				Assert.IsTrue(resolved.IsPreferred);
				Assert.AreEqual("quickfix", resolved.Kind);
				Assert.AreEqual("using System.Collections.Generic;\n", resolved.Edit!.DocumentEdits[0].TextEdits[0].NewText);
				Assert.AreEqual(s_filePath, resolved.Edit.DocumentEdits[0].DocumentId);
			}
		}
	}

	[TestMethod]
	public async Task ResolveAsync_AfterTheDocumentChanged_SendsNothingAndReturnsTheActionUnchanged()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider, IReadOnlyList<CodeAction> actions) = await OfferAsync([
			UsingAction(),
		]);

		using (client)
		{
			using (provider)
			{
				client.CodeActionResolveResponse = ResolvedUsingAction();

				provider.UpdateDocument(s_filePath, Content + "-- typed after the offer\n");
				Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, TestPolling.DefaultTimeout));

				CodeAction resolved = await ((ICodeActionResolver)provider).ResolveAsync(actions[0], TestContext.CancellationToken);

				Assert.AreSame(actions[0], resolved);
				Assert.AreEqual(0, client.GetSentMethodCount("codeAction/resolve"));
			}
		}
	}

	[TestMethod]
	public async Task ResolveAsync_WhenTheDocumentChangesWhileTheServerResolves_ReturnsTheActionUnchanged()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider, IReadOnlyList<CodeAction> actions) = await OfferAsync([
			UsingAction(),
		]);

		using (client)
		{
			using (provider)
			{
				var gate = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

				client.CodeActionResolveHandler = (_, _) => gate.Task;

				Task<CodeAction> resolving = ((ICodeActionResolver)provider).ResolveAsync(actions[0], TestContext.CancellationToken);

				Assert.IsTrue(await client.WaitForMethodCountAsync("codeAction/resolve", 1, TestPolling.DefaultTimeout));

				// The user types while the server computes the edit, and the edit addresses the text the action was offered for.
				provider.UpdateDocument(s_filePath, Content + "-- typed while the server resolved\n");
				Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, TestPolling.DefaultTimeout));

				gate.SetResult(ResolvedUsingAction());

				Assert.AreSame(actions[0], await resolving);
			}
		}
	}

	[TestMethod]
	public async Task ResolveAsync_ForAnActionTheProviderDidNotOffer_ReturnsItUnchangedWithoutARequest()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider, _) = await OfferAsync([UsingAction()]);

		using (client)
		{
			using (provider)
			{
				var foreign = new CodeAction("Foreign", data: "not an offer");

				Assert.AreSame(foreign, await ((ICodeActionResolver)provider).ResolveAsync(foreign, TestContext.CancellationToken));
				Assert.AreEqual(0, client.GetSentMethodCount("codeAction/resolve"));
			}
		}
	}

	[TestMethod]
	public async Task ResolveAsync_WhenTheAnswerCarriesNoEditOrCommand_ReturnsTheActionUnchanged()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider, IReadOnlyList<CodeAction> actions) = await OfferAsync([
			UsingAction(),
		]);

		using (client)
		{
			using (provider)
			{
				client.CodeActionResolveResponse = JsonSerializer.SerializeToElement(
					new { title = "using System.Collections.Generic;", data = new { id = 17 } }
				);

				Assert.AreSame(actions[0], await ((ICodeActionResolver)provider).ResolveAsync(actions[0], TestContext.CancellationToken));
			}
		}
	}

	[TestMethod]
	public async Task ResolveAsync_WhenTheServerAnswersNull_ReturnsTheActionUnchanged()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider, IReadOnlyList<CodeAction> actions) = await OfferAsync([
			UsingAction(),
		]);

		using (client)
		{
			using (provider)
			{
				client.CodeActionResolveResponse = JsonSerializer.SerializeToElement<object?>(null);
				Assert.AreSame(actions[0], await ((ICodeActionResolver)provider).ResolveAsync(actions[0], TestContext.CancellationToken));
			}
		}
	}

	[TestMethod]
	public async Task ResolveAsync_WhenTheServerFailsTheRequest_ReturnsTheActionUnchanged()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider, IReadOnlyList<CodeAction> actions) = await OfferAsync([
			UsingAction(),
		]);

		using (client)
		{
			using (provider)
			{
				client.ThrowInvalidOperationOnNextRequestMethod = "codeAction/resolve";
				Assert.AreSame(actions[0], await ((ICodeActionResolver)provider).ResolveAsync(actions[0], TestContext.CancellationToken));
			}
		}
	}

	[TestMethod]
	public async Task TryRunAsync_SendsTheCommandWithItsArgumentsAndReportsThatItRan()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider, IReadOnlyList<CodeAction> actions) = await OfferAsync(
			[
				new
				{
					title = "Remove trailing spaces",

					command = new
					{
						title = "Remove spaces",
						command = "lua.removeSpace",
						arguments = new object[] { new { uri = FileUri } },
					},
				},
			],

			commands: ["lua.removeSpace"]
		);

		using (client)
		{
			using (provider)
			{
				Assert.IsTrue(await ((ICodeActionCommandRunner)provider).TryRunAsync(actions[0].Command!, TestContext.CancellationToken));

				JsonElement sent = client.GetLastRequestParameters("workspace/executeCommand");

				Assert.AreEqual("lua.removeSpace", sent.GetProperty("command").GetString());
				Assert.AreEqual(FileUri, sent.GetProperty("arguments")[0].GetProperty("uri").GetString());
			}
		}
	}

	[TestMethod]
	public async Task TryRunAsync_ForACommandTheServerDoesNotExecute_SendsNothingAndReportsThatItDidNotRun()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider, _) = await OfferAsync(
			[UsingAction()],
			commands: ["lua.setConfig"]
		);

		using (client)
		{
			using (provider)
			{
				var foreign = new CodeActionCommand("Nested", "roslyn.client.nestedCodeAction", ["x"]);

				Assert.IsFalse(await ((ICodeActionCommandRunner)provider).TryRunAsync(foreign, TestContext.CancellationToken));
				Assert.AreEqual(0, client.GetSentMethodCount("workspace/executeCommand"));
			}
		}
	}

	[TestMethod]
	public async Task TryRunAsync_ACommandThatOutlastsTheRequestTimeout_IsWaitedForAndReportsThatItRan()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider, IReadOnlyList<CodeAction> actions) = await OfferAsync(
			[
				new
				{
					title = "Slow command",

					command = new
					{
						title = "Slow",
						command = "lua.slow",
						arguments = new object[] { new { n = 1 } },
					},
				},
			],

			commands: ["lua.slow"],

			options: new LanguageServerFeatureProviderOptions
			{
				RequestTimeout = TimeSpan.FromMilliseconds(100.0),
				RequestTimeoutRestartThreshold = 1,
			}
		);

		using (client)
		{
			using (provider)
			{
				client.ExecuteCommandHandler = async (_, cancellationToken) =>
				{
					await Task.Delay(TimeSpan.FromMilliseconds(600.0), cancellationToken);
					return JsonSerializer.SerializeToElement<object?>(null);
				};

				// The command runs six times as long as the request timeout, and one timeout is enough to restart the connection of a read.
				Assert.IsTrue(await ((ICodeActionCommandRunner)provider).TryRunAsync(actions[0].Command!, TestContext.CancellationToken));
				Assert.AreEqual(1, client.GetSentMethodCount("workspace/executeCommand"));
				Assert.AreEqual(0, client.MarkConnectionUnhealthyCallCount);
			}
		}
	}

	[TestMethod]
	public async Task TryRunAsync_WhenTheConnectionEndsBeforeTheServerAnswers_DoesNotSendTheCommandAgain()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider, IReadOnlyList<CodeAction> actions) = await OfferAsync(
			[
				new
				{
					title = "Mark as defined global",

					command = new
					{
						title = "Set config",
						command = "lua.setConfig",
						arguments = new object[] { new { n = 1 } },
					},
				},
			],

			commands: ["lua.setConfig"]
		);

		using (client)
		{
			using (provider)
			{
				client.ThrowIOExceptionOnNextRequestMethod = "workspace/executeCommand";

				Assert.IsFalse(await ((ICodeActionCommandRunner)provider).TryRunAsync(actions[0].Command!, TestContext.CancellationToken));
				Assert.AreEqual(1, client.GetSentMethodCount("workspace/executeCommand"));
			}
		}
	}

	[TestMethod]
	public async Task TryRunAsync_SerializesAHostBuiltArgumentWithTheWireCasing()
	{
		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider, _) = await OfferAsync(
			[UsingAction()],
			commands: ["lua.setConfig"]
		);

		using (client)
		{
			using (provider)
			{
				var command = new CodeActionCommand("Set config", "lua.setConfig", [new HostArgument("Lua.diagnostics.globals", ["prnt"])]);

				Assert.IsTrue(await ((ICodeActionCommandRunner)provider).TryRunAsync(command, TestContext.CancellationToken));

				JsonElement argument = client.GetLastRequestParameters("workspace/executeCommand").GetProperty("arguments")[0];

				Assert.AreEqual("Lua.diagnostics.globals", argument.GetProperty("settingKey").GetString());
				Assert.AreEqual("prnt", argument.GetProperty("settingValues")[0].GetString());
				Assert.IsFalse(argument.TryGetProperty("SettingKey", out _));
			}
		}
	}

	private sealed record HostArgument(string SettingKey, string[] SettingValues);

	[TestMethod]
	public async Task TryRunAsync_ReturnsTrueAfterTheServerFinishedACommandThatAskedForAnEdit()
	{
		var edits = new List<WorkspaceEdit>();

		LanguageServerClientHooks clientHooks = ClientHooksFactory.Create(
			new LanguageServerFeatureProviderHooks
			{
				ApplyWorkspaceEdit = (edit, _) =>
				{
					edits.Add(edit);
					return Task.FromResult(true);
				},
			},

			TestPaths.PathComparison,
			Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance
		);

		(FakeStatefulLanguageServerClient client, LanguageServerFeatureProvider provider, IReadOnlyList<CodeAction> actions) = await OfferAsync(
			[
				new
				{
					title = "Remove trailing spaces",

					command = new
					{
						title = "Remove spaces",
						command = "lua.removeSpace",
						arguments = new object[] { new { uri = FileUri } },
					},
				},
			],

			commands: ["lua.removeSpace"]
		);

		using (client)
		{
			using (provider)
			{
				bool applied = false;

				// The server answers the command by asking the host to apply an edit, and finishes the command once the host answers.
				client.ExecuteCommandHandler = async (_, cancellationToken) =>
				{
					ApplyWorkspaceEditResponse response = await clientHooks.ApplyWorkspaceEdit!(
						new ApplyWorkspaceEditParams(
							"Remove trailing spaces",

							new WorkspaceEditResponse(
								changes: new Dictionary<string, IReadOnlyList<TextEditPayload>?>
								{
									[FileUri] = [new TextEditPayload(new RangePayload(new PositionPayload(0, 5), new PositionPayload(0, 6)), "")],
								},

								documentChanges: null
							)
						),

						cancellationToken
					);

					applied = response.Applied;

					return JsonSerializer.SerializeToElement<object?>(null);
				};

				Assert.IsTrue(await ((ICodeActionCommandRunner)provider).TryRunAsync(actions[0].Command!, TestContext.CancellationToken));
				Assert.IsTrue(applied);

				Assert.HasCount(1, edits);
				Assert.AreEqual(s_filePath, edits[0].DocumentEdits[0].DocumentId);
				Assert.AreEqual(new TextPositionRange(new TextPosition(0, 5), new TextPosition(0, 6)), edits[0].DocumentEdits[0].TextEdits[0].Range);
			}
		}
	}

	public TestContext? TestContext { get; set; }
}
