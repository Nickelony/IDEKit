using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

public sealed partial class LanguageServerClientTests
{
	/// <summary>
	/// Starts a client whose JSON-RPC reads frames the test pushes and whose writes the test inspects, with the given hooks in its options.
	/// </summary>
	private static (LanguageServerClient Client, ScriptedServerFrameStream Server, RecordingStream Output, RecordingLogger Logger) StartHookedClient(
		LanguageServerClientHooks hooks
	)
	{
		var logger = new RecordingLogger(LogLevel.Debug);
		var serverFrameStream = new ScriptedServerFrameStream();
		var hostOutputStream = new RecordingStream();

		var client = new LanguageServerClient(
			workspaceRootDirectoryPaths: [TestPaths.Root],
			transport: s_exampleTransport,
			hooks,
			options: s_briefTeardownClientOptions,
			logger: logger.CreateLogger<LanguageServerClient>()
		);

		StartServerMessageDispatcher(client);

		ConnectionSession session = CreateConnectionSession(
			client,
			generation: 8,
			process: null,
			serverOutputStream: serverFrameStream,
			serverInputStream: hostOutputStream,
			startListening: true
		);

		SetActiveSession(client, session);
		SetReadyState(client, true);

		return (client, serverFrameStream, hostOutputStream, logger);
	}

	private static async Task<JsonElement> AwaitResponseAsync(RecordingStream hostOutputStream, int id)
	{
		await TestPolling
			.UntilAsync(
				() => JsonRpcFrameTestHelpers.ExtractWrittenMessages(hostOutputStream.GetWrittenText()).Any(message => message.Method is null),
				TimeSpan.FromSeconds(5),
				$"The client should answer request {id}."
			)
			.ConfigureAwait(false);

		(string? _, string bodyJson) = JsonRpcFrameTestHelpers
			.ExtractWrittenMessages(hostOutputStream.GetWrittenText())
			.Single(message => message.Method is null);

		using var document = JsonDocument.Parse(bodyJson);

		Assert.AreEqual(id, document.RootElement.GetProperty("id").GetInt32(), bodyJson);
		Assert.IsFalse(document.RootElement.TryGetProperty("error", out _), bodyJson);

		return document.RootElement.Clone();
	}

	private static async Task<int> AwaitWrittenRequestIdAsync(RecordingStream hostOutputStream, string method)
	{
		await TestPolling
			.UntilAsync(
				() => JsonRpcFrameTestHelpers.ExtractWrittenMessages(hostOutputStream.GetWrittenText()).Any(message => message.Method == method),
				TimeSpan.FromSeconds(5),
				$"The client should send {method}."
			)
			.ConfigureAwait(false);

		string bodyJson = JsonRpcFrameTestHelpers
			.ExtractWrittenMessages(hostOutputStream.GetWrittenText())
			.First(message => message.Method == method)
			.BodyJson;

		using var document = JsonDocument.Parse(bodyJson);

		return document.RootElement.GetProperty("id").GetInt32();
	}

	[TestMethod]
	public async Task ServerMessageHook_ShowMessage_ReceivesTheNotificationAndTheLoggerStaysSilent()
	{
		var received = new TaskCompletionSource<WindowMessageParams>(TaskCreationOptions.RunContinuationsAsynchronously);

		(LanguageServerClient client, ScriptedServerFrameStream server, RecordingStream output, RecordingLogger logger) = StartHookedClient(
			new LanguageServerClientHooks { ShowMessage = message => received.TrySetResult(message) }
		);

		using (server)
		{
			using (output)
			{
				using (logger)
				{
					await using (client)
					{
						server.Push(
							JsonRpcFrameTestHelpers.BuildNotification("window/showMessage", """{ "type": 2, "message": "Project load finished" }""")
						);

						WindowMessageParams message = await received
							.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken)
							.ConfigureAwait(false);

						Assert.AreEqual(MessageType.Warning, message.Type);
						Assert.AreEqual("Project load finished", message.Message);
						Assert.DoesNotContain(static log => log.Contains("[LS window/showMessage]", StringComparison.Ordinal), logger.Logs);
					}
				}
			}
		}
	}

	[TestMethod]
	public async Task ServerMessageHook_ShowMessageRequest_AnswersTheRequestWithTheChosenAction()
	{
		(LanguageServerClient client, ScriptedServerFrameStream server, RecordingStream output, RecordingLogger logger) = StartHookedClient(
			new LanguageServerClientHooks
			{
				ShowMessageRequest = (parameters, _) => Task.FromResult<MessageActionItemPayload?>(parameters.Actions![1]),
			}
		);

		using (server)
		{
			using (output)
			{
				using (logger)
				{
					await using (client)
					{
						server.Push(
							JsonRpcFrameTestHelpers.BuildRequest(
								21,
								"window/showMessageRequest",
								"""{ "type": 3, "message": "Reload the project?", "actions": [ { "title": "Yes" }, { "title": "No" } ] }"""
							)
						);

						JsonElement response = await AwaitResponseAsync(output, 21).ConfigureAwait(false);

						Assert.AreEqual("No", response.GetProperty("result").GetProperty("title").GetString());
					}
				}
			}
		}
	}

	[TestMethod]
	public async Task ServerMessageHook_ShowMessageRequestWithoutAHook_AnswersNullInsteadOfAnError()
	{
		(LanguageServerClient client, ScriptedServerFrameStream server, RecordingStream output, RecordingLogger logger) = StartHookedClient(
			new LanguageServerClientHooks()
		);

		using (server)
		{
			using (output)
			{
				using (logger)
				{
					await using (client)
					{
						server.Push(
							JsonRpcFrameTestHelpers.BuildRequest(
								22,
								"window/showMessageRequest",
								"""{ "type": 3, "message": "Reload?", "actions": [ { "title": "Yes" } ] }"""
							)
						);

						JsonElement response = await AwaitResponseAsync(output, 22).ConfigureAwait(false);

						Assert.AreEqual(JsonValueKind.Null, response.GetProperty("result").ValueKind);
					}
				}
			}
		}
	}

	[TestMethod]
	public async Task ServerMessageHook_Progress_ReceivesTheWorkDoneReportsInOrder()
	{
		var reports = new List<ProgressParams>();
		var ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		(LanguageServerClient client, ScriptedServerFrameStream server, RecordingStream output, RecordingLogger logger) = StartHookedClient(
			new LanguageServerClientHooks
			{
				Progress = progress =>
				{
					lock (reports)
						reports.Add(progress);

					if (progress.Value.Kind == "end")
						ended.TrySetResult(true);
				},
			}
		);

		using (server)
		{
			using (output)
			{
				using (logger)
				{
					await using (client)
					{
						server.Push(JsonRpcFrameTestHelpers.BuildRequest(23, "window/workDoneProgress/create", """{ "token": "load" }"""));

						server.Push(
							JsonRpcFrameTestHelpers.BuildNotification(
								"$/progress",
								"""{ "token": "load", "value": { "kind": "begin", "title": "Loading projects", "percentage": 0 } }"""
							)
						);

						server.Push(
							JsonRpcFrameTestHelpers.BuildNotification(
								"$/progress",
								"""{ "token": "load", "value": { "kind": "report", "message": "1/2", "percentage": 50 } }"""
							)
						);

						server.Push(JsonRpcFrameTestHelpers.BuildNotification("$/progress", """{ "token": "load", "value": { "kind": "end" } }"""));

						await ended.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

						lock (reports)
						{
							Assert.AreSequenceEqual(["begin", "report", "end"], [.. reports.Select(static report => report.Value.Kind)]);
							Assert.AreEqual(50, reports[1].Value.Percentage);
							Assert.AreEqual("Loading projects", reports[0].Value.Title);
						}
					}
				}
			}
		}
	}

	[TestMethod]
	public async Task ServerMessageHook_ApplyWorkspaceEdit_AnswersTheRequestWithTheAnswerOfTheHook()
	{
		ApplyWorkspaceEditParams? received = null;

		(LanguageServerClient client, ScriptedServerFrameStream server, RecordingStream output, RecordingLogger logger) = StartHookedClient(
			new LanguageServerClientHooks
			{
				ApplyWorkspaceEdit = (parameters, _) =>
				{
					received = parameters;
					return Task.FromResult(new ApplyWorkspaceEditResponse(Applied: true));
				},
			}
		);

		using (server)
		{
			using (output)
			{
				using (logger)
				{
					await using (client)
					{
						server.Push(
							JsonRpcFrameTestHelpers.BuildRequest(
								24,
								"workspace/applyEdit",

								"""
									{
									  "label": "Remove trailing spaces",
									  "edit": { "changes": { "file:///C:/Workspace/a.lua": [ { "range": { "start": { "line": 0, "character": 3 }, "end": { "line": 0, "character": 4 } }, "newText": "" } ] } }
									}
								"""
							)
						);

						JsonElement response = await AwaitResponseAsync(output, 24).ConfigureAwait(false);

						Assert.IsTrue(response.GetProperty("result").GetProperty("applied").GetBoolean());
						Assert.AreEqual("Remove trailing spaces", received?.Label);

						IReadOnlyDictionary<string, IReadOnlyList<TextEditPayload>?>? changes = received?.Edit.Changes;

						Assert.IsNotNull(changes);
						Assert.AreEqual("", changes.Values.Single()![0].NewText);
					}
				}
			}
		}
	}

	[TestMethod]
	public async Task ServerMessageHook_ApplyWorkspaceEdit_IsAnsweredWhileARequestOfTheHostIsOutstanding()
	{
		var applied = new TaskCompletionSource<ApplyWorkspaceEditParams>(TaskCreationOptions.RunContinuationsAsynchronously);

		(LanguageServerClient client, ScriptedServerFrameStream server, RecordingStream output, RecordingLogger logger) = StartHookedClient(
			new LanguageServerClientHooks
			{
				ApplyWorkspaceEdit = (parameters, _) =>
				{
					applied.TrySetResult(parameters);
					return Task.FromResult(new ApplyWorkspaceEditResponse(Applied: true));
				},
			}
		);

		using (server)
		{
			using (output)
			{
				using (logger)
				{
					await using (client)
					{
						// A language server that runs a command asks the client to apply an edit before it answers the command.
						Task<JsonElement?> command = client.SendRequestAsync<JsonElement?>(
							"workspace/executeCommand",
							new ExecuteCommandParams("lua.removeSpace", null),
							CancellationToken.None
						);

						int commandId = await AwaitWrittenRequestIdAsync(output, "workspace/executeCommand").ConfigureAwait(false);

						server.Push(
							JsonRpcFrameTestHelpers.BuildRequest(
								41,
								"workspace/applyEdit",
								"""{ "label": "Remove trailing spaces", "edit": { "changes": {} } }"""
							)
						);

						JsonElement response = await AwaitResponseAsync(output, 41).ConfigureAwait(false);

						Assert.IsTrue(response.GetProperty("result").GetProperty("applied").GetBoolean());
						Assert.AreEqual("Remove trailing spaces", (await applied.Task.ConfigureAwait(false)).Label);
						Assert.IsFalse(command.IsCompleted, "The command request is still outstanding while the client answers the edit.");

						server.Push(JsonRpcFrameTestHelpers.BuildResultResponse(commandId, "null"));

						Assert.IsNull(await command.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));
					}
				}
			}
		}
	}

	[TestMethod]
	public async Task ServerMessageHook_ShowMessage_RunsOneAtATimeOnAPoolThread_AndLaterServerMessagesWaitForIt()
	{
		var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondEnded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var release = new ManualResetEventSlim();
		var order = new List<string>();

		bool firstOnPoolThread = false;

		(LanguageServerClient client, ScriptedServerFrameStream server, RecordingStream output, RecordingLogger logger) = StartHookedClient(
			new LanguageServerClientHooks
			{
				ShowMessage = message =>
				{
					lock (order)
						order.Add("start " + message.Message);

					if (message.Message == "first")
					{
						firstOnPoolThread = Thread.CurrentThread.IsThreadPoolThread;
						firstEntered.TrySetResult(true);
						release.Wait(TimeSpan.FromSeconds(10));
					}

					lock (order)
						order.Add("end " + message.Message);

					if (message.Message == "second")
						secondEnded.TrySetResult(true);
				},
			}
		);

		using (server)
		{
			using (output)
			{
				using (logger)
				{
					await using (client)
					{
						server.Push(JsonRpcFrameTestHelpers.BuildNotification("window/showMessage", """{ "type": 3, "message": "first" }"""));
						server.Push(JsonRpcFrameTestHelpers.BuildNotification("window/showMessage", """{ "type": 3, "message": "second" }"""));

						await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);
						await Task.Delay(TimeSpan.FromMilliseconds(300.0), TestContext.CancellationToken).ConfigureAwait(false);

						// The later server message waits for the hook that is still running.
						lock (order)
							Assert.AreSequenceEqual(["start first"], order);

						// The answer to a request of the host does not wait for it.
						Task<JsonElement?> command = client.SendRequestAsync<JsonElement?>(
							"workspace/executeCommand",
							new ExecuteCommandParams("lua.removeSpace", null),
							CancellationToken.None
						);

						int commandId = await AwaitWrittenRequestIdAsync(output, "workspace/executeCommand").ConfigureAwait(false);

						server.Push(JsonRpcFrameTestHelpers.BuildResultResponse(commandId, "null"));

						Assert.IsNull(await command.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false));

						release.Set();
						await secondEnded.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken).ConfigureAwait(false);

						lock (order)
							Assert.AreSequenceEqual(["start first", "end first", "start second", "end second"], order);

						Assert.IsTrue(firstOnPoolThread);
					}
				}
			}
		}
	}

	[TestMethod]
	public async Task ServerMessageHook_ApplyWorkspaceEditWithoutAHook_AnswersThatNothingWasApplied()
	{
		(LanguageServerClient client, ScriptedServerFrameStream server, RecordingStream output, RecordingLogger logger) = StartHookedClient(
			new LanguageServerClientHooks()
		);

		using (server)
		{
			using (output)
			{
				using (logger)
				{
					await using (client)
					{
						server.Push(JsonRpcFrameTestHelpers.BuildRequest(25, "workspace/applyEdit", """{ "edit": { "changes": {} } }"""));

						JsonElement response = await AwaitResponseAsync(output, 25).ConfigureAwait(false);

						Assert.IsFalse(response.GetProperty("result").GetProperty("applied").GetBoolean());
					}
				}
			}
		}
	}
}
