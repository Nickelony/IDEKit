using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using System.Text.Json;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers the framework-level behavior of <see cref="LanguageServerIntelliSenseProviderBase"/>
/// through a minimal test provider: lazy startup, document synchronization, dispatch, diagnostics, rename rekeying,
/// and workspace change forwarding. Language-specific wiring remains covered by each language's own test suite.
/// </summary>
[TestClass]
public sealed class LanguageServerIntelliSenseProviderBaseTests
{
	// Temp-derived paths keep the suite platform-neutral; the root intentionally does not exist, so watcher
	// startup reports a missing workspace instead of creating OS watchers during the tests.
	private static readonly string WorkspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-base-tests-" + Guid.NewGuid().ToString("N"));
	private static readonly string FilePath = Path.Combine(WorkspaceRoot, "Scripts", "test.test");
	private const string Content = "line one";
	private const string UpdatedContent = "line two";
	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(2);

	[TestMethod]
	public async Task OpenDocument_StartsLazilyAndSendsDidOpenFromHooks()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, SupportsReferences = true };
		using var provider = new TestLanguageServerProvider([WorkspaceRoot], client);
		int capabilitiesChangedCount = 0;

		provider.CapabilitiesChanged += (_, _) => capabilitiesChangedCount++;

		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);
		Assert.IsFalse(provider.IsAvailable);
		Assert.IsFalse(provider.SupportsReferences);

		provider.OpenDocument(FilePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		JsonElement parameters = client.GetLastNotificationParameters("textDocument/didOpen");
		JsonElement textDocument = parameters.GetProperty("textDocument");

		Assert.AreEqual(new Uri(FilePath).AbsoluteUri, textDocument.GetProperty("uri").GetString());
		Assert.AreEqual("test", textDocument.GetProperty("languageId").GetString());
		Assert.AreEqual(1, textDocument.GetProperty("version").GetInt32());
		Assert.AreEqual(Content, textDocument.GetProperty("text").GetString());
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
		Assert.IsTrue(provider.IsAvailable);
		Assert.IsTrue(provider.SupportsReferences);
		Assert.AreEqual(1, capabilitiesChangedCount);
	}

	[TestMethod]
	public async Task UpdateDocument_ForwardsIncrementalChangeAndRunsSynchronizedHook()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([WorkspaceRoot], client);

		provider.OpenDocument(FilePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		provider.UpdateDocument(FilePath, UpdatedContent);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, s_waitTimeout));

		JsonElement parameters = client.GetLastNotificationParameters("textDocument/didChange");

		Assert.AreEqual(2, parameters.GetProperty("textDocument").GetProperty("version").GetInt32());

		JsonElement change = parameters.GetProperty("contentChanges")[0];

		// Incremental synchronization sends only the changed fragment; the range carries the replaced span.
		Assert.AreEqual("two", change.GetProperty("text").GetString());
		Assert.IsTrue(change.TryGetProperty("range", out _), "The incremental synchronization mode must carry a change range.");

		// The post-synchronization hook runs for the open (version 1) and the change (version 2).
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => provider.SynchronizedDocumentVersions.Count == 2, s_waitTimeout));
		CollectionAssert.AreEqual(new[] { 1, 2 }, provider.SynchronizedDocumentVersions.ToArray());
	}

	[TestMethod]
	public async Task DocumentOperations_WhenNoSynchronizationModeWasNegotiated_SkipOnlyTheChange()
	{
		// A ready session is a started session: the transport generation starts at 1, mirroring the real client.
		using var client = new FakeLanguageServerClient { IsReady = true, TransportGeneration = 1, TextDocumentSyncKind = TextDocumentSyncKind.None };
		using var provider = new TestLanguageServerProvider([WorkspaceRoot], client);

		// Opening is expressible without a negotiated synchronization mode: the server receives the document as it
		// was opened, and requests are served against that state.
		provider.OpenDocument(FilePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		// A change is not expressible. The provider skips the send with a warning (pinned by the Lua provider
		// suite), keeps the tracked content committed, and does not run the post-synchronization refresh for a
		// change the server never saw.
		provider.UpdateDocument(FilePath, UpdatedContent);

		// A request for the same document queues behind the skipped change, so its completion proves the
		// skipped change's slot finished.
		Assert.IsNull(await provider.GetHoverAsync(FilePath, UpdatedContent, new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.AreEqual(0, client.GetSentMethodCount("textDocument/didChange"));
		Assert.AreEqual(UpdatedContent, provider.GetTrackedSnapshot(FilePath)?.Content);
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);

		// Only the open advanced the server's copy; the skipped change must not have refreshed language state.
		CollectionAssert.AreEqual(new[] { 1 }, provider.SynchronizedDocumentVersions.ToArray());
	}

	[TestMethod]
	public async Task DocumentOperations_WhenNoSynchronizationModeWasNegotiated_WarnsOncePerGeneration()
	{
		using var logScope = new TestLoggerScope(LogLevel.Debug);

		// A ready session is a started session: the transport generation starts at 1, mirroring the real client.
		using var client = new FakeLanguageServerClient { IsReady = true, TransportGeneration = 1, TextDocumentSyncKind = TextDocumentSyncKind.None };
		using var provider = new TestLanguageServerProvider([WorkspaceRoot], client, logger: logScope.CreateLogger<TestLanguageServerProvider>());

		provider.OpenDocument(FilePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		// Two skipped changes on the same transport generation. The hover queues behind each change, so its
		// completion proves the skipped change's slot finished before the next one is queued.
		provider.UpdateDocument(FilePath, UpdatedContent);
		Assert.IsNull(await provider.GetHoverAsync(FilePath, UpdatedContent, new TextPosition(0, 0)).ConfigureAwait(false));

		provider.UpdateDocument(FilePath, UpdatedContent + "2");
		Assert.IsNull(await provider.GetHoverAsync(FilePath, UpdatedContent, new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.AreEqual(0, client.GetSentMethodCount("textDocument/didChange"));

		// The first skip warns; later skips on the same generation drop to debug, so a session that negotiated
		// no synchronization mode cannot warn once per keystroke. The wording match is deliberate: both entries
		// share one message and the level prefix is the only thing that distinguishes them.
		Assert.AreEqual(1, logScope.Logs.Count(static entry => entry.StartsWith("Warn|", StringComparison.Ordinal) && entry.Contains("skipped a document change", StringComparison.Ordinal)));
		Assert.IsTrue(logScope.Logs.Any(static entry => entry.StartsWith("Debug|", StringComparison.Ordinal) && entry.Contains("skipped a document change", StringComparison.Ordinal)));
	}

	[TestMethod]
	public async Task CloseDocument_ForwardsDidCloseAndInvalidatesTrackedDocument()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([WorkspaceRoot], client);

		provider.OpenDocument(FilePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		provider.CloseDocument(FilePath);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, s_waitTimeout));

		Assert.AreEqual(new Uri(FilePath).AbsoluteUri, client.GetLastNotificationParameters("textDocument/didClose")
			.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => provider.InvalidatedPaths.Contains(FilePath), s_waitTimeout));
	}

	[TestMethod]
	public async Task StartupFailure_IsReportedTransientlyThenPersistentlyAtThreshold()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, StartResult = false };
		using var provider = new TestLanguageServerProvider([WorkspaceRoot], client,
			new LanguageServerProviderOptions { HardStartupFailureThreshold = 2 });
		var failures = new List<LanguageServerStartupFailure>();

		provider.StartupFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

		Assert.IsNull(await provider.GetHoverAsync(FilePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);

		Assert.IsNull(await provider.GetHoverAsync(FilePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));
		Assert.AreEqual(LanguageServerProviderState.Failed, provider.State);

		// The failed state stops further startup attempts until the provider is recreated.
		Assert.IsNull(await provider.GetHoverAsync(FilePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.AreEqual(2, client.StartCallCount);
		Assert.AreEqual(2, failures.Count);
		Assert.IsFalse(failures[0].IsPersistent);
		Assert.IsTrue(failures[1].IsPersistent);
	}

	[TestMethod]
	public async Task MissingClient_IsReportedOnceAsPersistentFailure()
	{
		using var provider = new TestLanguageServerProvider([WorkspaceRoot], client: null);
		var failures = new List<LanguageServerStartupFailure>();

		provider.StartupFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

		Assert.IsNull(await provider.GetHoverAsync(FilePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));
		Assert.IsNull(await provider.GetHoverAsync(FilePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.AreEqual(LanguageServerProviderState.Failed, provider.State);
		Assert.AreEqual(1, failures.Count);
		Assert.IsTrue(failures[0].IsPersistent);

		// The hook's own unique payload must reach the failure: an exact match proves propagation of the hook
		// result, not a coincidence where a framework string happens to contain the same substring.
		Assert.AreEqual(provider.MissingClientFailureMessage, failures[0].Message);

		// A missing client never tracks a document, so no diagnostics are attributed to the requested path.
		Assert.AreEqual(0, provider.GetDiagnostics(FilePath).Count);
	}

	[TestMethod]
	public async Task ClientDiagnosticsPublished_RaisesProviderDiagnosticsForTrackedDocumentsOnly()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([WorkspaceRoot], client);
		var updates = new List<(string FilePath, int DiagnosticCount)>();

		// A distinctive fabricated payload proves the hook result is passed through, instead of asserting the test
		// double's own default single diagnostic.
		provider.DiagnosticsFactory = _ =>
		[
			new TextDiagnostic(TextDiagnosticSeverity.Warning, "one", 0, 0),
			new TextDiagnostic(TextDiagnosticSeverity.Error, "two", 1, 1),
			new TextDiagnostic(TextDiagnosticSeverity.Hint, "three", 2, 2)
		];

		provider.DiagnosticsUpdated += (_, eventArgs) => updates.Add((eventArgs.FilePath, eventArgs.Diagnostics.Count));

		provider.OpenDocument(FilePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		string untrackedFilePath = Path.Combine(WorkspaceRoot, "Scripts", "other.test");
		client.PublishDiagnostics(new PublishDiagnosticsParams(new Uri(untrackedFilePath).AbsoluteUri, null, []));

		Assert.AreEqual(0, updates.Count, "Diagnostics for untracked documents must be dropped.");

		client.PublishDiagnostics(new PublishDiagnosticsParams(new Uri(FilePath).AbsoluteUri, null, []));

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => updates.Count == 1, s_waitTimeout));
		Assert.AreEqual(LanguageServerPaths.NormalizeLocalPath(FilePath), updates[0].FilePath);
		Assert.AreEqual(3, updates[0].DiagnosticCount);
		Assert.AreEqual(3, provider.GetDiagnostics(FilePath).Count);

		client.PublishDiagnostics(new PublishDiagnosticsParams(new Uri(untrackedFilePath).AbsoluteUri, null, []));

		Assert.AreEqual(0, provider.GetDiagnostics(untrackedFilePath).Count);
	}

	[TestMethod]
	public async Task ClientDiagnosticsPublished_WhenTheHookSuppressesThePayload_RaisesNothing()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([WorkspaceRoot], client);
		int updateCount = 0;

		provider.SuppressDiagnosticsPayload = true;
		provider.DiagnosticsUpdated += (_, _) => updateCount++;

		provider.OpenDocument(FilePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		client.PublishDiagnostics(new PublishDiagnosticsParams(new Uri(FilePath).AbsoluteUri, null, []));

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.AreEqual(0, updateCount);
		Assert.AreEqual(0, provider.GetDiagnostics(FilePath).Count);
	}

	[TestMethod]
	public async Task ClientDiagnosticsPublished_ForANonFileUri_IsDroppedWithoutThrowing()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([WorkspaceRoot], client);
		int updateCount = 0;

		provider.DiagnosticsUpdated += (_, _) => updateCount++;

		provider.OpenDocument(FilePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		client.PublishDiagnostics(new PublishDiagnosticsParams("untitled:Untitled-1", null, []));

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.AreEqual(0, updateCount);
	}

	[TestMethod]
	public async Task MoveDocument_RekeysTrackedDocumentAndRunsRenameHook()
	{
		string renamedFilePath = Path.Combine(WorkspaceRoot, "Scripts", "renamed.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([WorkspaceRoot], client);

		provider.OpenDocument(FilePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		provider.MoveDocument(FilePath, renamedFilePath, Content);

		// The server-open document is closed under its old identity and reopened under the new one.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, s_waitTimeout));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout));
		Assert.AreEqual(new Uri(renamedFilePath).AbsoluteUri, client.GetLastNotificationParameters("textDocument/didOpen")
			.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => provider.MovedPaths.Contains(renamedFilePath), s_waitTimeout));

		// The move also runs the post-synchronization hook for the reopened document, so a provider that overrides
		// it observes the move as a synchronization in addition to the move hook.
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => provider.SynchronizedDocumentVersions.Count >= 2, s_waitTimeout));
	}

	[TestMethod]
	public async Task WorkspaceFileChanges_ForwardWatchedFilesAndRefreshConfiguration()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(workspaceRoot);

		try
		{
			var watcherCapture = new TestWorkspaceWatcherCapture();
			using var client = new FakeLanguageServerClient { IsReady = false };
			using var provider = new TestLanguageServerProvider([workspaceRoot], client, workspaceFileWatcherFactory: watcherCapture.Create);

			await provider.GetHoverAsync(Path.Combine(workspaceRoot, "boot.test"), "return 1", new TextPosition(0, 0)).ConfigureAwait(false);

			Func<FileChangeBatch, CancellationToken, Task> dispatch = watcherCapture.Dispatch
				?? throw new AssertFailedException("Expected the framework to create and start a workspace file watcher during startup.");

			string scriptFilePath = Path.Combine(workspaceRoot, "script.test");
			string configurationFilePath = Path.Combine(workspaceRoot, ".testconfig");

			await dispatch(new FileChangeBatch(
			[
				new WorkspaceFileChange(scriptFilePath, FileChangeKind.Changed),
				new WorkspaceFileChange(configurationFilePath, FileChangeKind.Created)
			]), CancellationToken.None).ConfigureAwait(false);

			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, s_waitTimeout));

			JsonElement changes = client.GetLastNotificationParameters("workspace/didChangeWatchedFiles").GetProperty("changes");

			Assert.AreEqual(new Uri(scriptFilePath).AbsoluteUri, changes[0].GetProperty("uri").GetString());
			Assert.AreEqual((int)FileChangeKind.Changed, changes[0].GetProperty("type").GetInt32());
			Assert.AreEqual(new Uri(configurationFilePath).AbsoluteUri, changes[1].GetProperty("uri").GetString());

			// The configuration file in the batch refreshes the settings through the provider's settings hook.
			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeConfiguration", 1, s_waitTimeout));
			Assert.IsTrue(client.GetLastNotificationParameters("workspace/didChangeConfiguration").GetProperty("settings").GetProperty("Enabled").GetBoolean());
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task HookFailure_IsContainedAndDoesNotBreakSynchronizationOrRequests()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([WorkspaceRoot], client)
		{
			ThrowOnSynchronizedHook = true,
			ThrowOnTrackedDiagnostics = true
		};

		provider.OpenDocument(FilePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		// The throwing language hooks are contained: requests still complete and diagnostics reads fall back.
		Assert.IsNull(await provider.GetHoverAsync(FilePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));
		Assert.AreEqual(0, provider.GetDiagnostics(FilePath).Count);
	}

	[TestMethod]
	public async Task MissingClientFailureHook_WhenThrowing_IsContainedAndRequestsStillFallBack()
	{
		using var provider = new TestLanguageServerProvider([WorkspaceRoot], client: null)
		{
			ThrowOnMissingClientFailureHook = true
		};
		int startupFailures = 0;

		provider.StartupFailed += (_, _) => startupFailures++;

		// The failure hook throws while the provider reports a missing client; the request must still complete
		// with its documented fallback value instead of surfacing the hook fault.
		Assert.IsNull(await provider.GetHoverAsync(FilePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => provider.State == LanguageServerProviderState.Failed, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(0, startupFailures);
	}

	[TestMethod]
	public async Task StartupFailureHook_WhenThrowing_IsContainedAndTheProviderStillFallsBack()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, StartResult = false };
		using var provider = new TestLanguageServerProvider([WorkspaceRoot], client)
		{
			ThrowOnStartupFailureHook = true
		};
		int startupFailures = 0;

		provider.StartupFailed += (_, _) => startupFailures++;

		// The startup-failure hook throws while the start fails; the failure is contained and the request still
		// completes with its documented fallback value under the failed state.
		Assert.IsNull(await provider.GetHoverAsync(FilePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.AreEqual(1, client.StartCallCount);
		Assert.AreEqual(LanguageServerProviderState.Unavailable, provider.State);
		Assert.AreEqual(0, startupFailures);
	}

	[TestMethod]
	public async Task RenameHooks_WhenThrowing_AreContainedAndTheRenameStillCompletes()
	{
		string renamedFilePath = Path.Combine(WorkspaceRoot, "Scripts", "renamed.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([WorkspaceRoot], client)
		{
			ThrowOnTrackedDiagnostics = true,
			ThrowOnMovedHook = true
		};

		provider.OpenDocument(FilePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		// The diagnostics read and the rename hook throw; both are contained, so the rename still rekeys the
		// record and the rename hook still runs.
		provider.MoveDocument(FilePath, renamedFilePath, Content);

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => provider.MovedPaths.Count == 1, s_waitTimeout).ConfigureAwait(false));
		Assert.IsNotNull(provider.GetTrackedSnapshot(renamedFilePath));
		Assert.IsNull(provider.GetTrackedSnapshot(FilePath));
	}

	[TestMethod]
	public async Task DiagnosticsUpdated_WhenAHandlerThrows_LaterHandlersStillRun()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([WorkspaceRoot], client);
		int observedCount = 0;

		provider.DiagnosticsUpdated += (_, _) => throw new InvalidOperationException("Simulated subscriber failure.");
		provider.DiagnosticsUpdated += (_, _) => observedCount++;

		provider.OpenDocument(FilePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		client.PublishDiagnostics(new PublishDiagnosticsParams(new Uri(FilePath).AbsoluteUri, null, []));

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => observedCount == 1, s_waitTimeout));
	}

	[TestMethod]
	public async Task CapabilityFlags_FollowAvailabilityAndNegotiatedCapabilities()
	{
		using var client = new FakeLanguageServerClient
		{
			IsReady = false,
			SupportsDocumentSymbols = true,
			SupportsCodeActions = true,
			SupportsReferences = true,
			SupportsRename = true,
			SupportsFormatting = true
		};
		using var provider = new TestLanguageServerProvider([WorkspaceRoot], client);

		Assert.IsFalse(provider.SupportsDocumentSymbols);
		Assert.IsFalse(provider.SupportsCodeActions);
		Assert.IsFalse(provider.SupportsReferences);
		Assert.IsFalse(provider.SupportsRename);
		Assert.IsFalse(provider.SupportsFormatting);

		provider.OpenDocument(FilePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		Assert.IsTrue(provider.SupportsDocumentSymbols);
		Assert.IsTrue(provider.SupportsCodeActions);
		Assert.IsTrue(provider.SupportsReferences);
		Assert.IsTrue(provider.SupportsRename);
		Assert.IsTrue(provider.SupportsFormatting);

		client.RaiseTransportUnavailable(client.TransportGeneration);

		Assert.IsFalse(provider.SupportsDocumentSymbols);
		Assert.IsFalse(provider.SupportsCodeActions);
		Assert.IsFalse(provider.SupportsReferences);
		Assert.IsFalse(provider.SupportsRename);
		Assert.IsFalse(provider.SupportsFormatting);
	}

	[TestMethod]
	public async Task CapabilitiesChanged_WhenAHandlerThrows_LaterHandlersStillRun()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([WorkspaceRoot], client);
		int observedCount = 0;

		provider.CapabilitiesChanged += (_, _) => throw new InvalidOperationException("Simulated subscriber failure.");
		provider.CapabilitiesChanged += (_, _) => observedCount++;

		provider.OpenDocument(FilePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => observedCount >= 1, s_waitTimeout));

		// A throwing first handler must not make the framework raise the event twice for the benign one.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);
		Assert.AreEqual(1, observedCount);
	}

	[TestMethod]
	public async Task StartupFailed_WhenAHandlerThrows_LaterHandlersStillRun()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, StartResult = false };
		using var provider = new TestLanguageServerProvider([WorkspaceRoot], client,
			new LanguageServerProviderOptions { HardStartupFailureThreshold = 1 });
		int observedCount = 0;

		provider.StartupFailed += (_, _) => throw new InvalidOperationException("Simulated subscriber failure.");
		provider.StartupFailed += (_, _) => observedCount++;

		Assert.IsNull(await provider.GetHoverAsync(FilePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => observedCount == 1, s_waitTimeout));
	}

	[TestMethod]
	public async Task DisposedProvider_OpenCloseAndDiagnosticsAreNoOps()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		var provider = new TestLanguageServerProvider([WorkspaceRoot], client);

		provider.Dispose();
		provider.OpenDocument(FilePath, Content);
		provider.CloseDocument(FilePath);

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.AreEqual(0, client.GetSentMethodNames().Length);
		Assert.AreEqual(0, provider.GetDiagnostics(FilePath).Count);
	}
}
