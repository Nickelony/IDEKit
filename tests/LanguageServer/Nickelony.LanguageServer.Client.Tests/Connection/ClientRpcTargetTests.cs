using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class ClientRpcTargetTests
{
	private const long Generation = 3;

	private static readonly JsonObject s_settings = new() { ["editor"] = new JsonObject { ["fontSize"] = 14 } };

	/// <summary>A target over the real collaborators whose server-message gate the test flips.</summary>
	private sealed class TestHost : IDisposable
	{
		private bool _gateOpen = true;

		internal TestHost(LogLevel logLevel = LogLevel.Debug)
		{
			Logger = new RecordingLogger(logLevel);

			Host = new ClientCollaboratorTestHost(
				Logger,
				canAcceptServerMessagesForGeneration: generation => _gateOpen && generation == Generation,
				settingsProvider: static () => s_settings.DeepClone()
			);

			Target = new ClientRpcTarget(
				context: Host.Context,
				serverMessageRouter: Host.Router,
				settingsSnapshotCache: Host.SettingsCache,
				connectionGeneration: Generation
			);

			Host.SettingsCache.RefreshFromProvider();
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

	[TestMethod]
	public void WorkspaceConfiguration_WhenTheGateIsOpen_AnswersEachItemFromTheSettingsSnapshot()
	{
		using var fixture = new TestHost();

		JsonElement?[] response = fixture.Target.WorkspaceConfiguration(
			new WorkspaceConfigurationParams([
				new WorkspaceConfigurationItemPayload("editor"),
				new WorkspaceConfigurationItemPayload("editor.fontSize"),
			])
		);

		Assert.HasCount(2, response);
		Assert.AreEqual(14, response[0]!.Value.GetProperty("fontSize").GetInt32());
		Assert.AreEqual(14, response[1]!.Value.GetInt32());
	}

	[TestMethod]
	public void WorkspaceConfiguration_MalformedItem_KeepsItsSlotAsNull()
	{
		using var fixture = new TestHost();

		JsonElement?[] response = fixture.Target.WorkspaceConfiguration(
			new WorkspaceConfigurationParams([
				new WorkspaceConfigurationItemPayload(null, IsMalformed: true),
				new WorkspaceConfigurationItemPayload("editor"),
			])
		);

		Assert.HasCount(2, response);
		Assert.IsNull(response[0]);
		Assert.IsNotNull(response[1]);
	}

	[TestMethod]
	public void WorkspaceConfiguration_WhenTheGateIsClosed_AnswersAnArrayOfNullsSizedToTheRequest()
	{
		using var fixture = new TestHost();
		fixture.CloseGate();

		JsonElement?[] response = fixture.Target.WorkspaceConfiguration(
			new WorkspaceConfigurationParams([new WorkspaceConfigurationItemPayload("editor"), new WorkspaceConfigurationItemPayload("editor")])
		);

		// The result is positional, so a stale answer keeps one slot per requested item instead of an empty array.
		Assert.HasCount(2, response);
		Assert.IsTrue(response.All(static element => element is null));
	}

	[TestMethod]
	public void WorkspaceConfiguration_WhenTheGateIsClosedAndTheRequestHasNoItems_AnswersAnEmptyArray()
	{
		using var fixture = new TestHost();
		fixture.CloseGate();

		Assert.IsEmpty(fixture.Target.WorkspaceConfiguration(new WorkspaceConfigurationParams(Items: null)));
	}

	[TestMethod]
	public void WorkspaceFolders_WhenTheGateIsOpen_ReturnsACopyOfTheAdvertisedFolders()
	{
		using var fixture = new TestHost();

		WorkspaceFolder[] first = fixture.Target.WorkspaceFolders();
		WorkspaceFolder[] second = fixture.Target.WorkspaceFolders();

		Assert.HasCount(1, first);
		Assert.AreEqual("Workspace", first[0].Name);
		Assert.AreNotSame(first, second, "A caller may mutate its copy.");
	}

	[TestMethod]
	public void WorkspaceFolders_WhenTheGateIsClosed_ReturnsNoFolders()
	{
		using var fixture = new TestHost();
		fixture.CloseGate();

		Assert.IsEmpty(fixture.Target.WorkspaceFolders());
	}

	[TestMethod]
	public async Task RefreshSemanticTokensAsync_WhenTheGateIsOpen_QueuesTheRefreshForTheSubscribers()
	{
		using var fixture = new TestHost();
		int refreshes = 0;

		fixture.Host.Router.AddSemanticTokensRefreshSubscriber((_, _) => Interlocked.Increment(ref refreshes));
		fixture.Host.Router.EnsureServerMessageAndDiagnosticsLoopsRunning();

		Assert.IsNull(await fixture.Target.RefreshSemanticTokensAsync().ConfigureAwait(false));

		await TestPolling.UntilAsync(() => Volatile.Read(ref refreshes) == 1, TestPolling.DefaultTimeout).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task RefreshSemanticTokensAsync_WhenTheGateIsClosed_QueuesNothing()
	{
		using var fixture = new TestHost();
		int refreshes = 0;

		fixture.Host.Router.AddSemanticTokensRefreshSubscriber((_, _) => Interlocked.Increment(ref refreshes));
		fixture.Host.Router.EnsureServerMessageAndDiagnosticsLoopsRunning();
		fixture.CloseGate();

		Assert.IsNull(await fixture.Target.RefreshSemanticTokensAsync().ConfigureAwait(false));

		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(0, Volatile.Read(ref refreshes));
	}

	[TestMethod]
	public async Task RefreshDiagnosticsAsync_WhenTheGateIsOpen_QueuesTheRefreshForTheSubscribers()
	{
		using var fixture = new TestHost();
		int refreshes = 0;

		fixture.Host.Router.AddDiagnosticRefreshSubscriber((_, _) => Interlocked.Increment(ref refreshes));
		fixture.Host.Router.EnsureServerMessageAndDiagnosticsLoopsRunning();

		Assert.IsNull(await fixture.Target.RefreshDiagnosticsAsync().ConfigureAwait(false));

		await TestPolling.UntilAsync(() => Volatile.Read(ref refreshes) == 1, TestPolling.DefaultTimeout).ConfigureAwait(false);
	}

	[TestMethod]
	public async Task RefreshDiagnosticsAsync_WhenTheGateIsClosed_QueuesNothing()
	{
		using var fixture = new TestHost();
		int refreshes = 0;

		fixture.Host.Router.AddDiagnosticRefreshSubscriber((_, _) => Interlocked.Increment(ref refreshes));
		fixture.Host.Router.EnsureServerMessageAndDiagnosticsLoopsRunning();
		fixture.CloseGate();

		Assert.IsNull(await fixture.Target.RefreshDiagnosticsAsync().ConfigureAwait(false));

		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(0, Volatile.Read(ref refreshes));
	}

	[TestMethod]
	public async Task PublishDiagnostics_WhenTheGateIsOpen_QueuesThePayloadForTheSubscribers()
	{
		using var fixture = new TestHost();
		var received = new ConcurrentQueue<string?>();

		fixture.Host.Router.AddDiagnosticsSubscriber((_, args) => received.Enqueue(ServerMessageTestHelpers.FirstMessage(args.Parameters)));
		fixture.Host.Router.EnsureServerMessageAndDiagnosticsLoopsRunning();

		fixture.Target.PublishDiagnostics(ServerMessageTestHelpers.CreateDiagnostics("untitled:First", "first"));

		await TestPolling.UntilAsync(() => received.Count == 1, TestPolling.DefaultTimeout).ConfigureAwait(false);

		Assert.AreEqual("first", received.Single());
	}

	[TestMethod]
	public async Task PublishDiagnostics_WhenTheGateIsClosed_DropsThePayload()
	{
		using var fixture = new TestHost();
		int received = 0;

		fixture.Host.Router.AddDiagnosticsSubscriber((_, _) => Interlocked.Increment(ref received));
		fixture.Host.Router.EnsureServerMessageAndDiagnosticsLoopsRunning();
		fixture.CloseGate();

		fixture.Target.PublishDiagnostics(ServerMessageTestHelpers.CreateDiagnostics("untitled:First", "stale"));

		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(0, Volatile.Read(ref received));
	}

	[TestMethod]
	public async Task PublishDiagnostics_DegradedPayload_IsDroppedWithAWarningSoItCannotClearStoredDiagnostics()
	{
		using var fixture = new TestHost(LogLevel.Warning);
		int received = 0;

		fixture.Host.Router.AddDiagnosticsSubscriber((_, _) => Interlocked.Increment(ref received));
		fixture.Host.Router.EnsureServerMessageAndDiagnosticsLoopsRunning();

		fixture.Target.PublishDiagnostics(new PublishDiagnosticsParams("untitled:First", 1, []) { IsDegraded = true });

		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(0, Volatile.Read(ref received));

		Assert.Contains(
			static log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("malformed", StringComparison.Ordinal),
			fixture.Logger.Logs
		);
	}

	[TestMethod]
	public void PublishDiagnostics_NullPayload_IsDroppedWithADebugLog()
	{
		using var fixture = new TestHost();

		fixture.Target.PublishDiagnostics(null!);

		Assert.Contains(
			static log => log.StartsWith("Debug|", StringComparison.Ordinal) && log.Contains("null payload", StringComparison.Ordinal),
			fixture.Logger.Logs
		);
	}

	[TestMethod]
	public void RegisterCapability_WhenTheGateIsOpen_WarnsThatDynamicRegistrationIsUnsupported()
	{
		using var fixture = new TestHost();

		object? result = fixture.Target.RegisterCapability(
			new CapabilityRegistrationParams([
				new CapabilityRegistrationPayload("1", "textDocument/hover"),
				new CapabilityRegistrationPayload("2", " "),
			])
		);

		Assert.IsNull(result);

		Assert.Contains(
			static log =>
				log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("textDocument/hover, <unknown>", StringComparison.Ordinal),

			fixture.Logger.Logs,
			string.Join(Environment.NewLine, fixture.Logger.Logs)
		);
	}

	[TestMethod]
	public void RegisterCapability_WhenTheGateIsClosed_LogsAtDebugInstead()
	{
		using var fixture = new TestHost();
		fixture.CloseGate();

		fixture.Target.RegisterCapability(new CapabilityRegistrationParams([new CapabilityRegistrationPayload("1", "textDocument/hover")]));

		Assert.DoesNotContain(static log => log.StartsWith("Warn|", StringComparison.Ordinal), fixture.Logger.Logs);

		Assert.Contains(
			static log => log.StartsWith("Debug|", StringComparison.Ordinal) && log.Contains("stale", StringComparison.Ordinal),
			fixture.Logger.Logs
		);
	}

	[TestMethod]
	public void RegisterCapability_WithoutRegistrations_LogsNothing()
	{
		using var fixture = new TestHost();

		Assert.IsNull(fixture.Target.RegisterCapability(new CapabilityRegistrationParams(Registrations: null)));
		Assert.IsNull(fixture.Target.RegisterCapability(new CapabilityRegistrationParams(Registrations: [])));
		Assert.IsEmpty(fixture.Logger.Logs);
	}

	[TestMethod]
	public void UnregisterCapability_WhenTheGateIsOpen_WarnsThatDynamicRegistrationIsUnsupported()
	{
		using var fixture = new TestHost();

		object? result = fixture.Target.UnregisterCapability(
			new CapabilityUnregistrationParams([new CapabilityUnregistrationPayload("1", "textDocument/hover")])
		);

		Assert.IsNull(result);

		Assert.Contains(
			static log => log.StartsWith("Warn|", StringComparison.Ordinal) && log.Contains("textDocument/hover", StringComparison.Ordinal),
			fixture.Logger.Logs
		);
	}

	[TestMethod]
	public void UnregisterCapability_WithoutUnregistrations_LogsNothing()
	{
		using var fixture = new TestHost();

		Assert.IsNull(fixture.Target.UnregisterCapability(new CapabilityUnregistrationParams(Unregistrations: [])));
		Assert.IsEmpty(fixture.Logger.Logs);
	}

	[TestMethod]
	[DataRow(1, "Error")]
	[DataRow(2, "Warn")]
	[DataRow(3, "Info")]
	[DataRow(4, "Debug")]
	[DataRow(99, "Debug")]
	public void ShowMessage_LogsAtTheLevelOfTheMessageType(int type, string expectedLevel)
	{
		using var fixture = new TestHost();

		fixture.Target.ShowMessage(new WindowMessageParams((MessageType)type, "from the server"));

		Assert.Contains($"{expectedLevel}|[LS window/showMessage] from the server|", fixture.Logger.Logs);
	}

	[TestMethod]
	public void LogMessage_WithoutAType_LogsAtDebug()
	{
		using var fixture = new TestHost();

		fixture.Target.LogMessage(new WindowMessageParams(Type: null, "from the server"));

		Assert.Contains("Debug|[LS window/logMessage] from the server|", fixture.Logger.Logs);
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow("   ")]
	public void ShowMessage_WithoutText_LogsNothing(string? message)
	{
		using var fixture = new TestHost();

		fixture.Target.ShowMessage(new WindowMessageParams((MessageType)1, message));

		Assert.IsEmpty(fixture.Logger.Logs);
	}

	[TestMethod]
	public void IgnoredServerMessages_LogOnlyAtDebugAndNameTheReason()
	{
		using var fixture = new TestHost();
		using var payload = JsonDocument.Parse("{}");

		Assert.IsNull(fixture.Target.CreateWorkDoneProgress(payload.RootElement));
		fixture.Target.TelemetryEvent(payload.RootElement);
		fixture.Target.Progress(payload.RootElement);
		fixture.Target.Hello(payload.RootElement);

		Assert.HasCount(4, fixture.Logger.Logs);

		Assert.IsTrue(
			fixture.Logger.Logs.All(static log =>
				log.StartsWith("Debug|", StringComparison.Ordinal) && log.Contains("Reason:", StringComparison.Ordinal)
			)
		);
	}

	[TestMethod]
	public void IgnoredServerMessages_FromAStaleGeneration_SayTheyAreStale()
	{
		using var fixture = new TestHost();
		fixture.CloseGate();
		using var payload = JsonDocument.Parse("{}");

		fixture.Target.Hello(payload.RootElement);

		Assert.Contains(static log => log.Contains("stale", StringComparison.Ordinal), fixture.Logger.Logs);
	}

	public TestContext TestContext { get; set; } = null!;
}
