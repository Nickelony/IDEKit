using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Exercises <see cref="LanguageServerFeatureProvider"/> through a minimal test provider.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderTests
{
	// Temp-derived paths keep the suite platform-neutral.
	private static readonly string s_workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-base-tests-" + Guid.NewGuid().ToString("N"));
	private static readonly string s_filePath = Path.Combine(s_workspaceRoot, "Scripts", "test.test");

	private const string Content = "line one";
	private const string UpdatedContent = "line two";

	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(2);

	[TestMethod]
	public async Task OpenDocument_StartsLazilyAndSendsDidOpenFromHooks()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, SupportsReferences = true };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		int capabilitiesChangedCount = 0;

		provider.CapabilitiesChanged += (_, _) => capabilitiesChangedCount++;

		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, provider.State);
		Assert.IsFalse(provider.IsAvailable);
		Assert.IsFalse(provider.Capabilities.SupportsReferences);

		provider.OpenDocument(s_filePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		JsonElement parameters = client.GetLastNotificationParameters("textDocument/didOpen");
		JsonElement textDocument = parameters.GetProperty("textDocument");

		Assert.AreEqual(new Uri(s_filePath).AbsoluteUri, textDocument.GetProperty("uri").GetString());
		Assert.AreEqual("test", textDocument.GetProperty("languageId").GetString());
		Assert.AreEqual(1, textDocument.GetProperty("version").GetInt32());
		Assert.AreEqual(Content, textDocument.GetProperty("text").GetString());
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);
		Assert.IsTrue(provider.IsAvailable);
		Assert.IsTrue(provider.Capabilities.SupportsReferences);
		Assert.AreEqual(1, capabilitiesChangedCount);
	}

	[TestMethod]
	public async Task OpenDocument_CaseSensitivePathComparison_TracksPathsThatDifferOnlyByCaseAsTwoDocuments()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client, TestOptions.CaseSensitive);

		string upperCaseFilePath = Path.Combine(s_workspaceRoot, "Scripts", "TEST.test");

		provider.OpenDocument(s_filePath, Content);
		provider.OpenDocument(upperCaseFilePath, UpdatedContent);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout));
		Assert.AreEqual(Content, provider.GetTrackedSnapshot(s_filePath)?.Content);
		Assert.AreEqual(UpdatedContent, provider.GetTrackedSnapshot(upperCaseFilePath)?.Content);
	}

	[TestMethod]
	public async Task UpdateDocument_ForwardsIncrementalChangeAndAnnouncesTheSynchronizedVersions()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var host = new LanguageServerFeatureProviderTestHost([s_workspaceRoot], client);

		LanguageServerFeatureProvider provider = host.Provider;

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		provider.UpdateDocument(s_filePath, UpdatedContent);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, s_waitTimeout));

		JsonElement parameters = client.GetLastNotificationParameters("textDocument/didChange");

		Assert.AreEqual(2, parameters.GetProperty("textDocument").GetProperty("version").GetInt32());

		JsonElement change = parameters.GetProperty("contentChanges")[0];

		// Incremental synchronization sends only the changed fragment; the range carries the replaced span.
		Assert.AreEqual("two", change.GetProperty("text").GetString());
		Assert.IsTrue(change.TryGetProperty("range", out _), "The incremental synchronization mode must carry a change range.");

		// The synchronized signal announces the open (version 1) and the change (version 2).
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => host.SynchronizedDocumentVersions.Count == 2, s_waitTimeout));
		Assert.AreSequenceEqual([1, 2], [.. host.SynchronizedDocumentVersions]);
	}

	[TestMethod]
	public async Task DocumentOperations_WhenNoSynchronizationModeWasNegotiated_SkipOnlyTheChange()
	{
		// A ready session is a started session: the connection generation starts at 1, mirroring the real client.
		using var client = new FakeLanguageServerClient
		{
			IsReady = true,
			ConnectionGeneration = 1,
			TextDocumentSyncKind = TextDocumentSyncKind.None,
		};

		using var host = new LanguageServerFeatureProviderTestHost([s_workspaceRoot], client);
		LanguageServerFeatureProvider provider = host.Provider;

		// Opening is expressible without a negotiated synchronization mode: the server receives the document as it
		// was opened, and requests are served against that state.
		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		// A change is not expressible. The provider skips the send with a warning, keeps the tracked content
		// committed, and does not run the post-synchronization refresh for a change the server never saw.
		provider.UpdateDocument(s_filePath, UpdatedContent);

		// A request for the same document queues behind the skipped change, so its completion proves the
		// skipped change's slot finished.
		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: UpdatedContent,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.AreEqual(0, client.GetSentMethodCount("textDocument/didChange"));
		Assert.AreEqual(UpdatedContent, provider.GetTrackedSnapshot(s_filePath)?.Content);
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);

		// Only the open advanced the server's copy; the skipped change must not have refreshed language state.
		Assert.AreSequenceEqual([1], [.. host.SynchronizedDocumentVersions]);
	}

	[TestMethod]
	public async Task DocumentOperations_WhenNoSynchronizationModeWasNegotiated_WarnsOncePerGeneration()
	{
		using var logger = new RecordingLogger(LogLevel.Debug);

		// A ready session is a started session: the connection generation starts at 1, mirroring the real client.
		using var client = new FakeLanguageServerClient
		{
			IsReady = true,
			ConnectionGeneration = 1,
			TextDocumentSyncKind = TextDocumentSyncKind.None,
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create(
			[s_workspaceRoot],
			client,
			logger: logger.CreateLogger<LanguageServerFeatureProvider>()
		);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		// Two skipped changes on the same connection generation. The hover queues behind each change, so its
		// completion proves the skipped change's slot finished before the next one is queued.
		provider.UpdateDocument(s_filePath, UpdatedContent);

		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: UpdatedContent,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		provider.UpdateDocument(s_filePath, UpdatedContent + "2");

		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: UpdatedContent,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.AreEqual(0, client.GetSentMethodCount("textDocument/didChange"));

		// The first skip warns; later skips on the same generation drop to debug, so a session that negotiated
		// no synchronization mode cannot warn once per keystroke. The wording match is deliberate: both entries
		// share one message and the level prefix is the only thing that distinguishes them.
		Assert.ContainsSingle(
			static entry =>
				entry.StartsWith("Warn|", StringComparison.Ordinal) && entry.Contains("skipped a document change", StringComparison.Ordinal),

			logger.Logs
		);

		Assert.Contains(
			static entry =>
				entry.StartsWith("Debug|", StringComparison.Ordinal) && entry.Contains("skipped a document change", StringComparison.Ordinal),

			logger.Logs
		);
	}

	[TestMethod]
	public async Task CloseDocument_ForwardsDidCloseAndInvalidatesTrackedDocument()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var host = new LanguageServerFeatureProviderTestHost([s_workspaceRoot], client);

		LanguageServerFeatureProvider provider = host.Provider;

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		provider.CloseDocument(s_filePath);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, s_waitTimeout));

		Assert.AreEqual(
			new Uri(s_filePath).AbsoluteUri,
			client.GetLastNotificationParameters("textDocument/didClose").GetProperty("textDocument").GetProperty("uri").GetString()
		);

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => host.InvalidatedPaths.Contains(s_filePath), s_waitTimeout));
	}

	[TestMethod]
	public async Task DocumentOperations_ConcurrentOpenUpdateCloseOnDistinctDocuments_StayConsistent()
	{
		const int workerCount = 8;
		const int iterations = 5;
		const int documentCount = workerCount * iterations;

		var waitTimeout = TimeSpan.FromSeconds(10);

		// A ready, started session with incremental synchronization, so every open/change/close is expressible.
		using var client = new FakeLanguageServerClient
		{
			IsReady = true,
			ConnectionGeneration = 1,
			TextDocumentSyncKind = TextDocumentSyncKind.Incremental,
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		// Each worker drives its own distinct documents so no path is reopened while its previous close is still
		// queued: a reopen of a still-tracked document is coalesced by design, which would make the send counts
		// ambiguous. Concurrency across the workers is what the scheduler must keep consistent.
		string[][] workerFilePaths =
		[
			.. Enumerable
				.Range(0, workerCount)
				.Select(worker =>
					Enumerable
						.Range(0, iterations)
						.Select(iteration => Path.Combine(s_workspaceRoot, "Scripts", $"concurrent-{worker}-{iteration}.test"))
						.ToArray()
				),
		];

		var failures = new ConcurrentQueue<Exception>();

		// The entry points return before their work reaches the scheduler, and one that ensures the connection first can be overtaken by a later
		// one: a close that arrives before the open leaves the document open, an update that arrives after the close reopens it, and a hover that
		// arrives before the update sends the change in its place. A worker therefore waits for the server to receive each step of a document
		// before it issues the next, so what is under test is the concurrency across documents.
		async Task WaitForSendAsync(string method, string filePath)
		{
			if (
				!await client
					.WaitForDocumentMethodCountAsync(method, documentUri: new Uri(filePath).AbsoluteUri, expectedCount: 1, timeout: waitTimeout)
					.ConfigureAwait(false)
			)
			{
				throw new TimeoutException($"No {method} for '{filePath}' reached the connection within {waitTimeout}.");
			}
		}

		// Each worker runs open -> change -> hover -> close on one document per iteration. The scheduler serializes the per-document work, so the
		// only way an assertion can drift is a cross-document scheduling defect. The hover holds a temporary request lease, so the close that follows
		// it may be deferred until the lease is released.
		await Task.WhenAll(
				workerFilePaths.Select(workerPaths =>
					Task.Run(
						async () =>
						{
							try
							{
								foreach (string filePath in workerPaths)
								{
									provider.OpenDocument(filePath, Content);
									await WaitForSendAsync("textDocument/didOpen", filePath).ConfigureAwait(false);

									provider.UpdateDocument(filePath, UpdatedContent);
									await WaitForSendAsync("textDocument/didChange", filePath).ConfigureAwait(false);

									await provider
										.GetHoverAsync(
											filePath,
											documentText: UpdatedContent,
											position: new TextPosition(0, 0),
											cancellationToken: TestContext.CancellationToken
										)
										.ConfigureAwait(false);

									provider.CloseDocument(filePath);
									await WaitForSendAsync("textDocument/didClose", filePath).ConfigureAwait(false);
								}
							}
							catch (Exception exception)
							{
								failures.Enqueue(exception);
							}
						},

						TestContext.CancellationToken
					)
				)
			)
			.ConfigureAwait(false);

		Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures.Select(failure => failure.ToString())));

		// Each document went through one open, one change and one close, so every count is exact.
		Assert.AreEqual(documentCount, client.GetSentMethodCount("textDocument/didOpen"));
		Assert.AreEqual(documentCount, client.GetSentMethodCount("textDocument/didChange"));
		Assert.AreEqual(documentCount, client.GetSentMethodCount("textDocument/didClose"));

		// Closing every document must leave no tracked document behind.
		foreach (string[] workerPaths in workerFilePaths)
		{
			foreach (string filePath in workerPaths)
				Assert.IsNull(provider.GetTrackedSnapshot(filePath));
		}

		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);
	}

	[TestMethod]
	public async Task StartupFailure_IsReportedTransientlyThenPersistentlyAtThreshold()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, StartResult = false };

		using LanguageServerFeatureProvider provider = TestProviders.Create(
			[s_workspaceRoot],
			client,
			new LanguageServerFeatureProviderOptions { HardStartupFailureThreshold = 2 }
		);

		var failures = new List<StartupFailure>();

		provider.StartupFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, provider.State);

		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.AreEqual(LanguageServerFeatureProviderState.Failed, provider.State);

		// The failed state stops further startup attempts until the provider is recreated.
		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.AreEqual(2, client.StartCallCount);
		Assert.HasCount(2, failures);
		Assert.IsFalse(failures[0].IsPersistent);
		Assert.IsTrue(failures[1].IsPersistent);
	}

	[TestMethod]
	public async Task OpenDocument_WhenProviderHasTerminallyFailed_DoesNotCommitLocally()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, StartResult = false };

		using LanguageServerFeatureProvider provider = TestProviders.Create(
			[s_workspaceRoot],
			client,
			new LanguageServerFeatureProviderOptions { HardStartupFailureThreshold = 1 }
		);

		// Drive the provider to its terminal Failed state, from which it never retries.
		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.AreEqual(LanguageServerFeatureProviderState.Failed, provider.State);

		provider.OpenDocument(s_filePath, Content);

		// A record committed locally could never be mirrored by the failed provider, so the open stays untracked
		// and no didOpen is sent.
		Assert.IsNull(provider.GetTrackedSnapshot(s_filePath));
		Assert.AreEqual(0, client.GetSentMethodCount("textDocument/didOpen"));
	}

	[TestMethod]
	public async Task MissingClient_IsReportedOnceAsPersistentFailure()
	{
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client: null);
		var failures = new List<StartupFailure>();

		provider.StartupFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.AreEqual(LanguageServerFeatureProviderState.Failed, provider.State);
		Assert.HasCount(1, failures);
		Assert.IsTrue(failures[0].IsPersistent);

		// The hook's own unique payload must reach the failure: an exact match proves propagation of the hook
		// result, not a coincidence where a framework string happens to contain the same substring.
		Assert.AreEqual(TestProviders.MissingClientFailureMessage, failures[0].Message);

		// A missing client never tracks a document, so no diagnostics are attributed to the requested path.
		Assert.IsEmpty(provider.GetDiagnostics(s_filePath));
	}

	[TestMethod]
	public async Task ClientDiagnosticsPublished_RaisesProviderDiagnosticsForTrackedDocumentsOnly()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);
		var updates = new List<(string FilePath, int DiagnosticCount)>();

		provider.DiagnosticsUpdated += (_, eventArgs) => updates.Add((eventArgs.DocumentId, eventArgs.Diagnostics.Count));

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		string untrackedFilePath = Path.Combine(s_workspaceRoot, "Scripts", "other.test");
		client.PublishDiagnostics(new PublishDiagnosticsParams(new Uri(untrackedFilePath).AbsoluteUri, null, []));

		Assert.IsEmpty(updates, "Diagnostics for untracked documents must be dropped.");

		// Three entries at one offset prove that the mapped, severity-ordered list is what reaches the subscribers and the cache.
		client.PublishDiagnostics(
			new PublishDiagnosticsParams(
				new Uri(s_filePath).AbsoluteUri,
				null,

				[
					CreateDiagnosticPayload(Lsp.DiagnosticSeverity.Warning, "one"),
					CreateDiagnosticPayload(Lsp.DiagnosticSeverity.Error, "two"),
					CreateDiagnosticPayload(Lsp.DiagnosticSeverity.Hint, "three"),
				]
			)
		);

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => updates.Count == 1, s_waitTimeout));
		Assert.AreEqual(LocalPath.Normalize(s_filePath), updates[0].FilePath);
		Assert.AreEqual(3, updates[0].DiagnosticCount);

		Assert.AreSequenceEqual(["two", "one", "three"], [.. provider.GetDiagnostics(s_filePath).Select(static diagnostic => diagnostic.Message)]);

		client.PublishDiagnostics(new PublishDiagnosticsParams(new Uri(untrackedFilePath).AbsoluteUri, null, []));

		Assert.IsEmpty(provider.GetDiagnostics(untrackedFilePath));
	}

	[TestMethod]
	public async Task ClientDiagnosticsPublished_WhenTheProfileSuppliesNoMappingPolicy_RaisesNothing()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };

		using LanguageServerFeatureProvider provider = TestProviders.Create(
			[s_workspaceRoot],
			client,

			profile: TestProviders.CreateProfile() with
			{
				DiagnosticMappingPolicy = null,
			}
		);

		int updateCount = 0;

		provider.DiagnosticsUpdated += (_, _) => updateCount++;

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		client.PublishDiagnostics(
			new PublishDiagnosticsParams(new Uri(s_filePath).AbsoluteUri, null, [CreateDiagnosticPayload(Lsp.DiagnosticSeverity.Warning, "one")])
		);

		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(0, updateCount);
		Assert.IsEmpty(provider.GetDiagnostics(s_filePath));
	}

	[TestMethod]
	public async Task ClientDiagnosticsPublished_ForANonFileUri_IsDroppedWithoutThrowing()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		int updateCount = 0;

		provider.DiagnosticsUpdated += (_, _) => updateCount++;

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		client.PublishDiagnostics(new PublishDiagnosticsParams("untitled:Untitled-1", null, []));

		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(0, updateCount);
	}

	[TestMethod]
	public async Task RenameDocument_RenamesTheTrackedDocumentAndAnnouncesTheRename()
	{
		string renamedFilePath = Path.Combine(s_workspaceRoot, "Scripts", "renamed.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var host = new LanguageServerFeatureProviderTestHost([s_workspaceRoot], client);

		LanguageServerFeatureProvider provider = host.Provider;

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		provider.RenameDocument(s_filePath, renamedFilePath, Content);

		// The server-open document is closed under its old identity and reopened under the new one.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, s_waitTimeout));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout));

		Assert.AreEqual(
			new Uri(renamedFilePath).AbsoluteUri,
			client.GetLastNotificationParameters("textDocument/didOpen").GetProperty("textDocument").GetProperty("uri").GetString()
		);

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => host.RenamedPaths.Contains(renamedFilePath), s_waitTimeout));
	}

	[TestMethod]
	public async Task WorkspaceChanges_ForwardWatchedFilesAndRefreshConfiguration()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(workspaceRoot);

		try
		{
			var watchers = new RecordingFileWatcherFactory { RealWatcherSpecifications = TestProviders.WatchSpecifications };
			using var client = new FakeLanguageServerClient { IsReady = false };

			using LanguageServerFeatureProvider provider = TestProviders.Create(
				[workspaceRoot],
				client,
				workspaceFileWatcherFactory: watchers.Factory
			);

			await provider
				.GetHoverAsync(
					filePath: Path.Combine(workspaceRoot, "boot.test"),
					documentText: "return 1",
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false);

			Func<FileChangeBatch, CancellationToken, Task> dispatch =
				watchers.CreationsFor(workspaceRoot).LastOrDefault()?.DispatchAsync
				?? throw new AssertFailedException("Expected the framework to create and start a workspace file watcher during startup.");

			string scriptFilePath = Path.Combine(workspaceRoot, "script.test");
			string configurationFilePath = Path.Combine(workspaceRoot, ".testconfig");

			await dispatch(
					new FileChangeBatch([
						new FileChange(scriptFilePath, FileChangeKind.Changed),
						new FileChange(configurationFilePath, FileChangeKind.Created),
					]),

					CancellationToken.None
				)
				.ConfigureAwait(false);

			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, s_waitTimeout));

			JsonElement changes = client.GetLastNotificationParameters("workspace/didChangeWatchedFiles").GetProperty("changes");

			Assert.AreEqual(new Uri(scriptFilePath).AbsoluteUri, changes[0].GetProperty("uri").GetString());
			Assert.AreEqual((int)FileChangeType.Changed, changes[0].GetProperty("type").GetInt32());
			Assert.AreEqual(new Uri(configurationFilePath).AbsoluteUri, changes[1].GetProperty("uri").GetString());

			// The configuration file in the batch makes the provider ask the client to push the settings.
			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeConfiguration", 1, s_waitTimeout));
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task MissingClientFailureMessage_WhenBlank_ReportsNothingAndRequestsStillFallBack()
	{
		using LanguageServerFeatureProvider provider = TestProviders.Create(
			[s_workspaceRoot],
			client: null,

			profile: TestProviders.CreateProfile() with
			{
				MissingClientFailureMessage = " ",
			}
		);

		int startupFailures = 0;

		provider.StartupFailed += (_, _) => startupFailures++;

		// A blank message suppresses the report; the request must still complete with its documented fallback value.
		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.IsTrue(
			await TestPolling
				.ForConditionAsync(() => provider.State == LanguageServerFeatureProviderState.Failed, s_waitTimeout)
				.ConfigureAwait(false)
		);

		Assert.AreEqual(0, startupFailures);
	}

	[TestMethod]
	public async Task StartupFailureMessage_WhenBlank_ReportsNothingAndTheProviderStillFallsBack()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, StartResult = false };

		using LanguageServerFeatureProvider provider = TestProviders.Create(
			[s_workspaceRoot],
			client,

			profile: TestProviders.CreateProfile() with
			{
				TransientStartupFailureMessage = " ",
			}
		);

		int startupFailures = 0;

		provider.StartupFailed += (_, _) => startupFailures++;

		// A blank message suppresses the report; the request still completes with its documented fallback value.
		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.AreEqual(1, client.StartCallCount);
		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, provider.State);
		Assert.AreEqual(0, startupFailures);
	}

	[TestMethod]
	public async Task RenameDocument_WhenASubscriberThrows_StillRenamesTheRecord()
	{
		string renamedFilePath = Path.Combine(s_workspaceRoot, "Scripts", "renamed.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var host = new LanguageServerFeatureProviderTestHost([s_workspaceRoot], client);

		LanguageServerFeatureProvider provider = host.Provider;

		provider.DiagnosticsUpdated += (_, _) => throw new InvalidOperationException("Simulated subscriber failure.");

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		// The move announces the diagnostics cached for the new path, and the throwing subscriber is contained, so the
		// record still moves and the move is still announced.
		provider.RenameDocument(s_filePath, renamedFilePath, Content);

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => host.RenamedPaths.Count == 1, s_waitTimeout).ConfigureAwait(false));
		Assert.IsNotNull(provider.GetTrackedSnapshot(renamedFilePath));
		Assert.IsNull(provider.GetTrackedSnapshot(s_filePath));
	}

	[TestMethod]
	public async Task DiagnosticsUpdated_WhenAHandlerThrows_LaterHandlersStillRun()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		int observedCount = 0;

		provider.DiagnosticsUpdated += (_, _) => throw new InvalidOperationException("Simulated subscriber failure.");
		provider.DiagnosticsUpdated += (_, _) => observedCount++;

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		client.PublishDiagnostics(new PublishDiagnosticsParams(new Uri(s_filePath).AbsoluteUri, null, []));

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => observedCount == 1, s_waitTimeout));
	}

	[TestMethod]
	public async Task CapabilitiesChanged_PublishesAWholeNewSnapshot()
	{
		using var client = new FakeLanguageServerClient
		{
			IsReady = false,
			SupportsReferences = true,
			SupportsHover = true,
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		LanguageServerFeatureProviderCapabilities before = provider.Capabilities;
		var published = new List<LanguageServerFeatureProviderCapabilities>();

		provider.CapabilitiesChanged += (_, _) => published.Add(provider.Capabilities);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		// The earlier snapshot keeps its values, and the event publishes another instance that carries the whole new advertisement.
		Assert.AreEqual(LanguageServerFeatureProviderCapabilities.None, before);
		Assert.HasCount(1, published);
		Assert.AreNotSame(before, published[0]);
		Assert.IsTrue(published[0].SupportsReferences);
		Assert.IsTrue(published[0].SupportsHover);
		Assert.IsFalse(published[0].SupportsRename);
	}

	[TestMethod]
	public async Task Capabilities_FollowAvailabilityAndNegotiatedCapabilities()
	{
		using var client = new FakeLanguageServerClient
		{
			IsReady = false,
			SupportsReferences = true,
			SupportsRename = true,
			SupportsHover = true,
			SupportsDefinition = true,
			SupportsSignatureHelp = true,
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		Assert.AreEqual(LanguageServerFeatureProviderCapabilities.None, provider.Capabilities);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		Assert.AreEqual(
			new LanguageServerFeatureProviderCapabilities
			{
				SupportsHover = true,
				SupportsDefinition = true,
				SupportsSignatureHelp = true,
				SupportsReferences = true,
				SupportsRename = true,
			},

			provider.Capabilities
		);

		client.RaiseConnectionUnavailable(client.ConnectionGeneration);

		Assert.AreEqual(LanguageServerFeatureProviderCapabilities.None, provider.Capabilities);
	}

	[TestMethod]
	public async Task CapabilitiesChanged_WhenAHandlerThrows_LaterHandlersStillRun()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		int observedCount = 0;

		provider.CapabilitiesChanged += (_, _) => throw new InvalidOperationException("Simulated subscriber failure.");
		provider.CapabilitiesChanged += (_, _) => observedCount++;

		provider.OpenDocument(s_filePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => observedCount >= 1, s_waitTimeout));

		// A throwing first handler must not make the framework raise the event twice for the benign one.
		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);
		Assert.AreEqual(1, observedCount);
	}

	[TestMethod]
	public async Task StartupFailed_WhenAHandlerThrows_LaterHandlersStillRun()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, StartResult = false };

		using LanguageServerFeatureProvider provider = TestProviders.Create(
			[s_workspaceRoot],
			client,
			new LanguageServerFeatureProviderOptions { HardStartupFailureThreshold = 1 }
		);

		int observedCount = 0;

		provider.StartupFailed += (_, _) => throw new InvalidOperationException("Simulated subscriber failure.");
		provider.StartupFailed += (_, _) => observedCount++;

		Assert.IsNull(
			await provider
				.GetHoverAsync(
					filePath: s_filePath,
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false)
		);

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => observedCount == 1, s_waitTimeout));
	}

	[TestMethod]
	public async Task DisposedProvider_OpenCloseAndDiagnosticsAreNoOps()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		provider.Dispose();
		provider.OpenDocument(s_filePath, Content);
		provider.CloseDocument(s_filePath);

		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsEmpty(client.GetSentMethodNames());
		Assert.IsEmpty(provider.GetDiagnostics(s_filePath));
	}

	private static DiagnosticPayload CreateDiagnosticPayload(Lsp.DiagnosticSeverity severity, string message) =>
		new(Range: new RangePayload(new PositionPayload(0, 0), new PositionPayload(0, 1)), severity, message, Source: "test", Code: null);

	public TestContext? TestContext { get; set; }
}
