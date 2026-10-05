using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class ClientRpcTargetHooksTests
{
	private const long Generation = 3;

	private static readonly ApplyWorkspaceEditParams s_edit = new("Fix", new WorkspaceEditResponse(changes: null, documentChanges: null));

	/// <summary>A target over the real collaborators and the given hooks, whose server-message gate the test flips.</summary>
	private sealed class TestHost : IDisposable
	{
		private bool _gateOpen = true;

		internal TestHost(LanguageServerClientHooks? hooks = null)
		{
			Logger = new RecordingLogger(LogLevel.Debug);

			Host = new ClientCollaboratorTestHost(
				Logger,
				canAcceptServerMessagesForGeneration: generation => _gateOpen && generation == Generation,
				hooks: hooks ?? new LanguageServerClientHooks()
			);

			Target = new ClientRpcTarget(
				context: Host.Context,
				serverMessageRouter: Host.Router,
				settingsSnapshotCache: Host.SettingsCache,
				connectionGeneration: Generation
			);
		}

		internal RecordingLogger Logger { get; }

		internal ClientCollaboratorTestHost Host { get; }

		internal ClientRpcTarget Target { get; }

		internal void CloseGate() => _gateOpen = false;

		public void Dispose()
		{
			Host.Router.CompleteSignalChannels();
			Host.Router.CompleteSubscribers();
			Task.WhenAll(Host.Router.DiagnosticsCoalescerTask, Host.Router.ServerMessageDispatcherTask).Wait(TestPolling.DefaultTimeout);
			Logger.Dispose();
		}
	}

	private static JsonElement Parse(string json)
	{
		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}

	[TestMethod]
	public void ShowMessage_WithAHook_HandsTheMessageToTheHookInsteadOfTheLogger()
	{
		var received = new List<WindowMessageParams>();
		using var fixture = new TestHost(new LanguageServerClientHooks { ShowMessage = received.Add });

		fixture.Target.ShowMessage(new WindowMessageParams(MessageType.Warning, "from the server"));

		Assert.HasCount(1, received);
		Assert.AreEqual(new WindowMessageParams(MessageType.Warning, "from the server"), received[0]);
		Assert.IsEmpty(fixture.Logger.Logs);
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("   ")]
	public void ShowMessage_WithAHookAndNoText_CallsNothing(string? message)
	{
		int calls = 0;
		using var fixture = new TestHost(new LanguageServerClientHooks { ShowMessage = _ => calls++ });

		fixture.Target.ShowMessage(new WindowMessageParams(MessageType.Error, message));

		Assert.AreEqual(0, calls);
	}

	[TestMethod]
	public void ShowMessage_FromAStaleConnection_DropsTheMessage()
	{
		int calls = 0;
		using var fixture = new TestHost(new LanguageServerClientHooks { ShowMessage = _ => calls++ });
		fixture.CloseGate();

		fixture.Target.ShowMessage(new WindowMessageParams(MessageType.Information, "late"));

		Assert.AreEqual(0, calls);

		Assert.Contains(
			static log => log.StartsWith("Debug|", StringComparison.Ordinal) && log.Contains("stale", StringComparison.Ordinal),
			fixture.Logger.Logs
		);
	}

	[TestMethod]
	public void ShowMessage_WhenTheHookThrows_LogsAWarningAndKeepsTheConnection()
	{
		using var fixture = new TestHost(new LanguageServerClientHooks { ShowMessage = _ => throw new InvalidOperationException("hook fault") });

		fixture.Target.ShowMessage(new WindowMessageParams(MessageType.Error, "boom"));

		Assert.Contains(
			static log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("window/showMessage", StringComparison.Ordinal)
				&& log.Contains("hook fault", StringComparison.Ordinal),

			fixture.Logger.Logs
		);
	}

	[TestMethod]
	public async Task ShowMessageRequestAsync_WithAHook_AnswersWithTheChosenAction()
	{
		ShowMessageRequestParams? received = null;

		using var fixture = new TestHost(
			new LanguageServerClientHooks
			{
				ShowMessageRequest = (parameters, _) =>
				{
					received = parameters;
					return Task.FromResult<MessageActionItemPayload?>(parameters.Actions![1]);
				},
			}
		);

		var request = new ShowMessageRequestParams(
			MessageType.Information,
			"Reload?",
			[new MessageActionItemPayload("Yes"), new MessageActionItemPayload("No")]
		);

		MessageActionItemPayload? answer = await fixture.Target.ShowMessageRequestAsync(request, CancellationToken.None);

		Assert.AreEqual(new MessageActionItemPayload("No"), answer);
		Assert.AreEqual("Reload?", received?.Message);
	}

	[TestMethod]
	[DataRow("Maybe", DisplayName = "A title that names none of the actions")]
	[DataRow(null, DisplayName = "A choice without a title")]
	public async Task ShowMessageRequestAsync_WhenTheHookChoosesAnActionTheServerDidNotOffer_AnswersNull(string? title)
	{
		using var fixture = new TestHost(
			new LanguageServerClientHooks
			{
				ShowMessageRequest = (_, _) => Task.FromResult<MessageActionItemPayload?>(new MessageActionItemPayload(title)),
			}
		);

		MessageActionItemPayload? answer = await fixture.Target.ShowMessageRequestAsync(
			new ShowMessageRequestParams(
				MessageType.Information,
				"Reload?",
				[new MessageActionItemPayload("Yes"), new MessageActionItemPayload("No")]
			),

			CancellationToken.None
		);

		Assert.IsNull(answer);

		Assert.Contains(
			static log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("did not offer", StringComparison.Ordinal),
			fixture.Logger.Logs
		);
	}

	[TestMethod]
	public async Task ShowMessageRequestAsync_WhenTheServerOfferedNoActions_AnswersNullForAnyChoice()
	{
		using var fixture = new TestHost(
			new LanguageServerClientHooks
			{
				ShowMessageRequest = (_, _) => Task.FromResult<MessageActionItemPayload?>(new MessageActionItemPayload("Yes")),
			}
		);

		Assert.IsNull(
			await fixture.Target.ShowMessageRequestAsync(
				new ShowMessageRequestParams(MessageType.Information, "Reload?", null),
				CancellationToken.None
			)
		);
	}

	[TestMethod]
	public async Task ShowMessageRequestAsync_WithoutAHook_LogsTheMessageAndAnswersNull()
	{
		using var fixture = new TestHost();

		MessageActionItemPayload? answer = await fixture.Target.ShowMessageRequestAsync(
			new ShowMessageRequestParams(MessageType.Warning, "Reload?", [new MessageActionItemPayload("Yes")]),
			CancellationToken.None
		);

		Assert.IsNull(answer);
		Assert.Contains("Warn|[LS window/showMessageRequest] Reload?|", fixture.Logger.Logs);
	}

	[TestMethod]
	public async Task ShowMessageRequestAsync_WhenTheHookThrows_AnswersNull()
	{
		using var fixture = new TestHost(
			new LanguageServerClientHooks { ShowMessageRequest = (_, _) => throw new InvalidOperationException("hook fault") }
		);

		MessageActionItemPayload? answer = await fixture.Target.ShowMessageRequestAsync(
			new ShowMessageRequestParams(MessageType.Warning, "Reload?", null),
			CancellationToken.None
		);

		Assert.IsNull(answer);

		Assert.Contains(
			static log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("hook fault", StringComparison.Ordinal),
			fixture.Logger.Logs
		);
	}

	[TestMethod]
	public async Task ShowMessageRequestAsync_WhenTheServerCancelsTheRequest_CancelsTheTokenOfTheHook()
	{
		using var cancellation = new CancellationTokenSource();
		var reachedHook = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var fixture = new TestHost(
			new LanguageServerClientHooks
			{
				ShowMessageRequest = async (_, token) =>
				{
					reachedHook.SetResult(true);
					await Task.Delay(Timeout.InfiniteTimeSpan, token);
					return null;
				},
			}
		);

		Task<MessageActionItemPayload?> pending = fixture.Target.ShowMessageRequestAsync(
			new ShowMessageRequestParams(MessageType.Information, "wait", null),
			cancellation.Token
		);

		await reachedHook.Task.WaitAsync(TestPolling.DefaultTimeout);
		await cancellation.CancelAsync();

		Assert.IsNull(await pending.WaitAsync(TestPolling.DefaultTimeout));
	}

	[TestMethod]
	public void Progress_WithAHook_HandsTheWorkDoneReportToTheHook()
	{
		var received = new List<ProgressParams>();
		using var fixture = new TestHost(new LanguageServerClientHooks { Progress = received.Add });

		fixture.Target.Progress(
			Parse("""{ "token": "load-1", "value": { "kind": "begin", "title": "Loading", "message": "a.csproj", "percentage": 10 } }""")
		);

		fixture.Target.Progress(Parse("""{ "token": 7, "value": { "kind": "end" } }"""));

		Assert.HasCount(2, received);
		Assert.AreEqual("load-1", received[0].Token);
		Assert.AreEqual(new WorkDoneProgressPayload(Kind: "begin", Title: "Loading", Message: "a.csproj", Percentage: 10), received[0].Value);
		Assert.AreEqual("7", received[1].Token);
		Assert.AreEqual("end", received[1].Value.Kind);
	}

	[TestMethod]
	public void Progress_WithAHookAndAValueThatIsNoReport_CallsNothing()
	{
		int calls = 0;
		using var fixture = new TestHost(new LanguageServerClientHooks { Progress = _ => calls++ });

		fixture.Target.Progress(Parse("""{ "token": "partial", "value": [1, 2, 3] }"""));
		fixture.Target.Progress(Parse("""{ "token": "x", "value": { "kind": "unknown" } }"""));
		fixture.Target.Progress(Parse("""{ "value": { "kind": "begin" } }"""));

		Assert.AreEqual(0, calls);
	}

	[TestMethod]
	public void Progress_WithoutAHook_LogsTheNotificationAsIgnored()
	{
		using var fixture = new TestHost();

		fixture.Target.Progress(Parse("""{ "token": "x", "value": { "kind": "begin" } }"""));

		Assert.Contains(
			static log => log.StartsWith("Debug|", StringComparison.Ordinal) && log.Contains("$/progress", StringComparison.Ordinal),
			fixture.Logger.Logs
		);
	}

	[TestMethod]
	public void CreateWorkDoneProgress_WithAProgressHook_AcknowledgesWithoutLogging()
	{
		using var fixture = new TestHost(new LanguageServerClientHooks { Progress = static _ => { } });

		Assert.IsNull(fixture.Target.CreateWorkDoneProgress(Parse("""{ "token": "x" }""")));
		Assert.IsEmpty(fixture.Logger.Logs);
	}

	[TestMethod]
	public async Task ApplyWorkspaceEditAsync_WithAHook_AnswersWithTheResponseOfTheHook()
	{
		ApplyWorkspaceEditParams? received = null;

		using var fixture = new TestHost(
			new LanguageServerClientHooks
			{
				ApplyWorkspaceEdit = (parameters, _) =>
				{
					received = parameters;
					return Task.FromResult(new ApplyWorkspaceEditResponse(Applied: true));
				},
			}
		);

		ApplyWorkspaceEditResponse response = await fixture.Target.ApplyWorkspaceEditAsync(s_edit, CancellationToken.None);

		Assert.IsTrue(response.Applied);
		Assert.AreEqual("Fix", received?.Label);
	}

	[TestMethod]
	public async Task ApplyWorkspaceEditAsync_WithoutAHook_AnswersThatNothingWasApplied()
	{
		using var fixture = new TestHost();

		ApplyWorkspaceEditResponse response = await fixture.Target.ApplyWorkspaceEditAsync(s_edit, CancellationToken.None);

		Assert.IsFalse(response.Applied);
		Assert.IsFalse(string.IsNullOrWhiteSpace(response.FailureReason));
	}

	[TestMethod]
	public async Task ApplyWorkspaceEditAsync_WhenTheHookThrows_LogsItAndTellsTheServerOnlyAFixedReason()
	{
		using var fixture = new TestHost(
			new LanguageServerClientHooks
			{
				ApplyWorkspaceEdit = (_, _) => throw new InvalidOperationException(@"view is busy at C:\Users\someone\secret.lua"),
			}
		);

		ApplyWorkspaceEditResponse response = await fixture.Target.ApplyWorkspaceEditAsync(s_edit, CancellationToken.None);

		Assert.IsFalse(response.Applied);
		Assert.AreEqual("The client failed to apply the edit.", response.FailureReason);

		Assert.Contains(
			static log =>
				log.StartsWith("Warn|", StringComparison.Ordinal)
				&& log.Contains("workspace/applyEdit", StringComparison.Ordinal)
				&& log.Contains("secret.lua", StringComparison.Ordinal),

			fixture.Logger.Logs
		);
	}

	[TestMethod]
	public async Task ApplyWorkspaceEditAsync_FromAStaleConnection_DoesNotCallTheHook()
	{
		int calls = 0;

		using var fixture = new TestHost(
			new LanguageServerClientHooks
			{
				ApplyWorkspaceEdit = (_, _) =>
				{
					calls++;
					return Task.FromResult(new ApplyWorkspaceEditResponse(Applied: true));
				},
			}
		);

		fixture.CloseGate();

		ApplyWorkspaceEditResponse response = await fixture.Target.ApplyWorkspaceEditAsync(s_edit, CancellationToken.None);

		Assert.IsFalse(response.Applied);
		Assert.AreEqual(0, calls);
	}
}
