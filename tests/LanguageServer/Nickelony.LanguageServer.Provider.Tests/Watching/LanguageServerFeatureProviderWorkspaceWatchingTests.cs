using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers workspace-watching opt-out, failure containment, and nested-root routing.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderWorkspaceWatchingTests
{
	private const string Content = "line one";

	private static readonly string s_workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-watching-" + Guid.NewGuid().ToString("N"));
	private static readonly string s_filePath = Path.Combine(s_workspaceRoot, "Scripts", "test.test");

	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(2);

	private static bool ThrowConfigurationPathProbe(string normalizedPath) =>
		throw new InvalidOperationException("Simulated configuration-path probe failure.");

	[TestMethod]
	public async Task WorkspaceSendFailure_MarksTheConnectionUnavailableAndReplaysTheBufferedChanges()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-sendfail-" + Guid.NewGuid().ToString("N"));
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.test");

		Directory.CreateDirectory(workspaceRoot);

		try
		{
			using var logger = new RecordingLogger();
			using var client = new FakeLanguageServerClient { IsReady = false };

			var watchers = new RecordingFileWatcherFactory { RealWatcherSpecifications = TestProviders.WatchSpecifications };

			using LanguageServerFeatureProvider provider = TestProviders.Create(
				workspaceRootDirectoryPaths: [workspaceRoot],
				client,
				workspaceFileWatcherFactory: watchers.Factory,
				logger: logger
			);

			await provider
				.GetHoverAsync(filePath, documentText: Content, position: new TextPosition(0, 0), cancellationToken: TestContext.CancellationToken)
				.ConfigureAwait(false);

			Assert.IsTrue(
				await TestPolling.ForConditionAsync(() => watchers.CreationsFor(workspaceRoot).Count > 0, s_waitTimeout).ConfigureAwait(false)
			);

			Func<FileChangeBatch, CancellationToken, Task> dispatch = watchers.LatestFor(workspaceRoot).DispatchAsync;

			client.SendNotificationHandler = (method, _, _) =>
			{
				if (string.Equals(method, "workspace/didChangeWatchedFiles", StringComparison.Ordinal))
					throw new IOException("Simulated workspace send failure.");

				return Task.CompletedTask;
			};

			// Delivering one watched-files batch through the root's dispatch path fails on the connection send and
			// must mark the observed generation unhealthy while the batch stays buffered for replay.
			await dispatch(new FileChangeBatch([new FileChange(filePath, FileChangeKind.Changed)]), CancellationToken.None).ConfigureAwait(false);

			Assert.IsTrue(
				await TestPolling
					.ForConditionAsync(() => provider.State == LanguageServerFeatureProviderState.Unavailable, s_waitTimeout)
					.ConfigureAwait(false)
			);

			Assert.HasCount(1, client.MarkedUnhealthyGenerations);
			Assert.IsFalse(provider.IsAvailable);

			// The log names the provider first and the workspace root second, so a swapped pair of display strings shows here.
			string expectedLogPrefix = $"Failed to forward 1 Test workspace change(s) for '{LocalPath.Normalize(workspaceRoot)}' starting at '";

			Assert.Contains(log => log.Contains(expectedLogPrefix, StringComparison.Ordinal), logger.Logs);

			client.SendNotificationHandler = null;

			// The next request restarts the connection and replays the buffered changes exactly once.
			await provider
				.GetHoverAsync(filePath, documentText: Content, position: new TextPosition(0, 0), cancellationToken: TestContext.CancellationToken)
				.ConfigureAwait(false);

			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, s_waitTimeout).ConfigureAwait(false));
			Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task WorkspaceSender_ContainedHookFault_NamesTheProviderDisplayName()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-sender-" + Guid.NewGuid().ToString("N"));
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.test");

		Directory.CreateDirectory(workspaceRoot);

		try
		{
			using var logger = new RecordingLogger(LogLevel.Warning);
			using var client = new FakeLanguageServerClient { IsReady = false };

			var watchers = new RecordingFileWatcherFactory { RealWatcherSpecifications = TestProviders.WatchSpecifications };

			using LanguageServerFeatureProvider provider = TestProviders.Create(
				workspaceRootDirectoryPaths: [workspaceRoot],
				client,
				workspaceFileWatcherFactory: watchers.Factory,
				logger: logger,

				profile: TestProviders.CreateProfile(isConfigurationPath: ThrowConfigurationPathProbe) with
				{
					LanguageDisplayName = "Example",
				}
			);

			await provider
				.GetHoverAsync(filePath, documentText: Content, position: new TextPosition(0, 0), cancellationToken: TestContext.CancellationToken)
				.ConfigureAwait(false);

			Assert.IsTrue(
				await TestPolling.ForConditionAsync(() => watchers.CreationsFor(workspaceRoot).Count > 0, s_waitTimeout).ConfigureAwait(false)
			);

			await watchers
				.LatestFor(workspaceRoot)
				.DispatchAsync(new FileChangeBatch([new FileChange(filePath, FileChangeKind.Changed)]), CancellationToken.None)
				.ConfigureAwait(false);

			// The sender's containment log names the provider; the workspace roots text would show here if the two strings were swapped.
			Assert.Contains(
				log => log.Contains("The configuration-path hook failed for the Example provider", StringComparison.Ordinal),
				logger.Logs
			);
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task EmptyWatchSpecifications_DisableWatchingWithoutBreakingRequests()
	{
		// The capture proves the disabled contract: the framework must not create a watcher at all.
		var watchers = new RecordingFileWatcherFactory { RealWatcherSpecifications = TestProviders.WatchSpecifications };
		using var client = new FakeLanguageServerClient { IsReady = false };

		using LanguageServerFeatureProvider provider = TestProviders.Create(
			workspaceRootDirectoryPaths: [s_workspaceRoot],
			client,
			workspaceFileWatcherFactory: watchers.Factory,

			profile: TestProviders.CreateProfile() with
			{
				WatchSpecifications = [],
			}
		);

		var failures = new List<WorkspaceFileWatchFailure>();

		provider.WorkspaceFileWatchFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

		// Request, open, and update must all complete normally with workspace watching disabled.
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

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		provider.UpdateDocument(s_filePath, "line two");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, s_waitTimeout).ConfigureAwait(false));

		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsEmpty(watchers.Creations);
		Assert.IsEmpty(failures);
	}

	[TestMethod]
	public async Task ThrowingWatcherFactory_ReportsWatcherFailureOnceWithoutEscapingRequests()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };

		using LanguageServerFeatureProvider provider = TestProviders.Create(
			[s_workspaceRoot],
			client,
			workspaceFileWatcherFactory: static (_, _, _) => throw new InvalidOperationException("Simulated watcher factory failure.")
		);

		var failures = new List<WorkspaceFileWatchFailure>();

		provider.WorkspaceFileWatchFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

		// The request completes its documented fallback value; the factory failure must not surface as an exception.
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

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => failures.Count == 1, s_waitTimeout).ConfigureAwait(false));
		Assert.IsTrue(failures[0].Message.Contains("could not be started", StringComparison.OrdinalIgnoreCase), failures[0].Message);
		Assert.AreEqual(s_workspaceRoot, failures[0].WorkspaceRootDirectoryPath);
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);
	}

	[TestMethod]
	public async Task NestedRoots_OverlappingWatchers_ForwardNestedChangeOnce()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-nested-" + Guid.NewGuid().ToString("N"));
		string nestedRoot = Path.Combine(workspaceRoot, "nested");

		Directory.CreateDirectory(nestedRoot);

		try
		{
			var watchers = new RecordingFileWatcherFactory { RealWatcherSpecifications = TestProviders.WatchSpecifications };
			using var client = new FakeLanguageServerClient { IsReady = false };

			using LanguageServerFeatureProvider provider = TestProviders.Create(
				[workspaceRoot, nestedRoot],
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

			Assert.IsTrue(
				await TestPolling.ForConditionAsync(() => watchers.Creations.Count == 2, s_waitTimeout).ConfigureAwait(false),
				"Expected one workspace file watcher per workspace root."
			);

			Func<FileChangeBatch, CancellationToken, Task> outerDispatch = watchers.LatestFor(workspaceRoot).DispatchAsync;
			Func<FileChangeBatch, CancellationToken, Task> nestedDispatch = watchers.LatestFor(nestedRoot).DispatchAsync;

			string nestedFilePath = Path.Combine(nestedRoot, "Scripts", "nested.test");
			string nestedConfigurationPath = Path.Combine(nestedRoot, ".testconfig");

			FileChangeBatch nestedBatch = new([
				new FileChange(nestedFilePath, FileChangeKind.Changed),
				new FileChange(nestedConfigurationPath, FileChangeKind.Created),
			]);

			// The outer root's watcher observes the nested subtree and delivers the batch first. The delivering
			// scope does not own these paths, so nothing may be forwarded or refreshed from this delivery.
			await outerDispatch(nestedBatch, CancellationToken.None).ConfigureAwait(false);

			Assert.AreEqual(
				0,
				client.GetSentMethodCount("workspace/didChangeWatchedFiles"),
				"A nested workspace root's changes must only be forwarded by the scope that owns them."
			);

			Assert.AreEqual(0, client.GetSentMethodCount("workspace/didChangeConfiguration"));

			// The nested root's own watcher delivers the same batch: both changes are forwarded exactly once and
			// the settings refresh comes from the owning (nested) scope.
			await nestedDispatch(nestedBatch, CancellationToken.None).ConfigureAwait(false);

			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, s_waitTimeout).ConfigureAwait(false));
			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeConfiguration", 1, s_waitTimeout).ConfigureAwait(false));

			Assert.AreEqual(
				1,
				client.GetSentMethodCount("workspace/didChangeWatchedFiles"),
				"The nested change must be forwarded exactly once across both watchers."
			);

			Assert.AreEqual(1, client.GetSentMethodCount("workspace/didChangeConfiguration"));

			JsonElement changes = client.GetLastNotificationParameters("workspace/didChangeWatchedFiles").GetProperty("changes");

			Assert.AreEqual(2, changes.GetArrayLength());
			Assert.AreEqual(new Uri(nestedFilePath).AbsoluteUri, changes[0].GetProperty("uri").GetString());
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task NestedRoots_WhenOwnedRootIsUnwatched_ForwardedByTheDeliveringWatcher()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-unwatched-" + Guid.NewGuid().ToString("N"));
		string missingNestedRoot = Path.Combine(workspaceRoot, "not-created");

		Directory.CreateDirectory(workspaceRoot);

		try
		{
			var watchers = new RecordingFileWatcherFactory { RealWatcherSpecifications = TestProviders.WatchSpecifications };
			using var client = new FakeLanguageServerClient { IsReady = false };

			using LanguageServerFeatureProvider provider = TestProviders.Create(
				[workspaceRoot, missingNestedRoot],
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

			Assert.IsTrue(
				await TestPolling.ForConditionAsync(() => watchers.Creations.Count == 2, s_waitTimeout).ConfigureAwait(false),
				"Expected the framework to attempt one workspace file watcher per workspace root."
			);

			// The nested root does not exist, so its scope never keeps a started watcher: the outer watcher must
			// cover the subtree instead of filtering the change away.
			await watchers
				.LatestFor(workspaceRoot)
				.DispatchAsync(
					new FileChangeBatch([new FileChange(Path.Combine(missingNestedRoot, "Scripts", "nested.test"), FileChangeKind.Changed)]),
					CancellationToken.None
				)
				.ConfigureAwait(false);

			Assert.IsTrue(
				await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, s_waitTimeout).ConfigureAwait(false),
				"The delivering watcher must cover a nested root whose own watcher is not active."
			);

			Assert.AreEqual(1, client.GetSentMethodCount("workspace/didChangeWatchedFiles"));
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task MissingWorkspaceRoot_IsSilentWhileRequestsStillSucceed()
	{
		// The root intentionally does not exist: the client is available, so the provider serves requests, but its
		// watcher can never start. A missing root is an expected condition and must not surface a watcher failure.
		string missingRoot = Path.Combine(Path.GetTempPath(), "ls-provider-missing-" + Guid.NewGuid().ToString("N"));

		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([missingRoot], client);
		var failures = new List<WorkspaceFileWatchFailure>();

		provider.WorkspaceFileWatchFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

		string filePath = Path.Combine(missingRoot, "Scripts", "test.test");

		Assert.IsNull(
			await provider
				.GetHoverAsync(filePath, documentText: Content, position: new TextPosition(0, 0), cancellationToken: TestContext.CancellationToken)
				.ConfigureAwait(false)
		);

		Assert.IsTrue(provider.IsAvailable);
		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);

		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsEmpty(failures, "A missing workspace root must not be reported as a watcher failure.");
	}

	[TestMethod]
	public async Task WatcherRuntimeFailure_IsRecoveredAndReportsOnlyUnrecoverableFailures()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-watcher-recovery-" + Guid.NewGuid().ToString("N"));
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

			var failures = new List<WorkspaceFileWatchFailure>();

			provider.WorkspaceFileWatchFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider
				.GetHoverAsync(
					filePath: Path.Combine(workspaceRoot, "boot.test"),
					documentText: "return 1",
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false);

			Assert.HasCount(1, watchers.CreationsFor(workspaceRoot));

			// A runtime watcher failure is recovered by a replacement watcher without raising the
			// unrecoverable-failure report.
			watchers.LatestFor(workspaceRoot).Fail(new IOException("Simulated watcher failure."));

			Assert.IsTrue(
				await TestPolling.ForConditionAsync(() => watchers.CreationsFor(workspaceRoot).Count == 2, s_waitTimeout).ConfigureAwait(false)
			);

			Assert.IsEmpty(failures);

			// When the replacement cannot be created either, the failure is reported once.
			watchers.ThrowOnCreateFor(workspaceRoot);
			watchers.LatestFor(workspaceRoot).Fail(new IOException("Simulated watcher failure."));

			Assert.IsTrue(await TestPolling.ForConditionAsync(() => failures.Count == 1, s_waitTimeout).ConfigureAwait(false));
			Assert.IsTrue(failures[0].Message.Contains("automatic recovery failed", StringComparison.OrdinalIgnoreCase), failures[0].Message);
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task WatcherRuntimeFailure_IsContainedToItsOwnRoot()
	{
		string rootA = Path.Combine(Path.GetTempPath(), "ls-provider-contained-a-" + Guid.NewGuid().ToString("N"));
		string rootB = Path.Combine(Path.GetTempPath(), "ls-provider-contained-b-" + Guid.NewGuid().ToString("N"));

		Directory.CreateDirectory(rootA);
		Directory.CreateDirectory(rootB);

		try
		{
			var watchers = new RecordingFileWatcherFactory { RealWatcherSpecifications = TestProviders.WatchSpecifications };
			using var client = new FakeLanguageServerClient { IsReady = false };

			using LanguageServerFeatureProvider provider = TestProviders.Create(
				[rootA, rootB],
				client,
				workspaceFileWatcherFactory: watchers.Factory
			);

			var failures = new List<WorkspaceFileWatchFailure>();

			provider.WorkspaceFileWatchFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider
				.GetHoverAsync(
					filePath: Path.Combine(rootA, "boot.test"),
					documentText: "return 1",
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false);

			Assert.IsTrue(
				await TestPolling
					.ForConditionAsync(() => watchers.CreationsFor(rootA).Count == 1 && watchers.CreationsFor(rootB).Count == 1, s_waitTimeout)
					.ConfigureAwait(false),

				"Expected one workspace file watcher per workspace root."
			);

			// A recoverable runtime failure on root A is recovered by a replacement watcher for that root only.
			watchers.LatestFor(rootA).Fail(new IOException("Simulated watcher failure."));

			Assert.IsTrue(await TestPolling.ForConditionAsync(() => watchers.CreationsFor(rootA).Count == 2, s_waitTimeout).ConfigureAwait(false));
			Assert.HasCount(1, watchers.CreationsFor(rootB), "A recoverable failure on one root must not replace another root's watcher.");
			Assert.IsEmpty(failures);

			// When root A's replacement cannot be created either, exactly one unrecoverable report names root A.
			watchers.ThrowOnCreateFor(rootA);
			watchers.LatestFor(rootA).Fail(new IOException("Simulated watcher failure."));

			Assert.IsTrue(await TestPolling.ForConditionAsync(() => failures.Count == 1, s_waitTimeout).ConfigureAwait(false));
			Assert.AreEqual(rootA, failures[0].WorkspaceRootDirectoryPath);

			Assert.AreEqual(
				1,
				watchers.CreationsFor(rootB).Count,
				"An unrecoverable failure on one root must leave another root's watcher untouched."
			);

			// The surviving root still forwards a change, proving the failure stayed contained to its own root.
			await watchers
				.LatestFor(rootB)
				.DispatchAsync(
					new FileChangeBatch([new FileChange(Path.Combine(rootB, "Scripts", "survivor.test"), FileChangeKind.Changed)]),
					CancellationToken.None
				)
				.ConfigureAwait(false);

			Assert.IsTrue(
				await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, s_waitTimeout).ConfigureAwait(false),
				"The surviving root must keep forwarding changes after another root fails."
			);
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(rootA);
			TemporaryDirectory.DeleteBestEffort(rootB);
		}
	}

	[TestMethod]
	public async Task WatcherRecovery_ReplaysTheChangeMissedWhileTheWatcherWasUnavailable()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-reconcile-" + Guid.NewGuid().ToString("N"));
		string missedFilePath = Path.Combine(workspaceRoot, "Scripts", "missed.test");

		Directory.CreateDirectory(Path.GetDirectoryName(missedFilePath) ?? workspaceRoot);

		try
		{
			// The passive watcher never observes the file system, so the file created below can only reach the
			// server through the recovery reconciliation of the tracked snapshot.
			var watchers = new RecordingFileWatcherFactory();
			using var client = new FakeLanguageServerClient { IsReady = false };

			using LanguageServerFeatureProvider provider = TestProviders.Create(
				[workspaceRoot],
				client,
				workspaceFileWatcherFactory: watchers.Factory
			);

			await provider
				.GetHoverAsync(
					filePath: Path.Combine(workspaceRoot, "boot.test"),
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false);

			// The request awaited the startup flow, so the watcher exists and the recovery baseline is the
			// workspace as it was before the file below appeared.
			Assert.HasCount(1, watchers.CreationsFor(workspaceRoot));

			File.WriteAllText(missedFilePath, Content);

			// Replacing the watcher reconciles the tracked snapshot against the current workspace, so the missed
			// creation is replayed as one watched-files change.
			watchers.LatestFor(workspaceRoot).Fail(new IOException("Simulated watcher failure."));

			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, s_waitTimeout).ConfigureAwait(false));

			JsonElement changes = client.GetLastNotificationParameters("workspace/didChangeWatchedFiles").GetProperty("changes");

			Assert.AreEqual(1, changes.GetArrayLength());
			Assert.AreEqual(new Uri(missedFilePath).AbsoluteUri, changes[0].GetProperty("uri").GetString());
			Assert.AreEqual((int)FileChangeType.Created, changes[0].GetProperty("type").GetInt32());
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task ConfigurationPathProbe_WhenThrowing_IsContainedAndChangesAreStillForwarded()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-probe-throw-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(workspaceRoot);

		try
		{
			var watchers = new RecordingFileWatcherFactory { RealWatcherSpecifications = TestProviders.WatchSpecifications };
			using var client = new FakeLanguageServerClient { IsReady = false };

			using LanguageServerFeatureProvider provider = TestProviders.Create(
				workspaceRootDirectoryPaths: [workspaceRoot],
				client,
				workspaceFileWatcherFactory: watchers.Factory,
				profile: TestProviders.CreateProfile(isConfigurationPath: ThrowConfigurationPathProbe)
			);

			await provider
				.GetHoverAsync(
					filePath: Path.Combine(workspaceRoot, "boot.test"),
					documentText: "return 1",
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false);

			Assert.IsTrue(
				await TestPolling.ForConditionAsync(() => watchers.Creations.Count == 1, s_waitTimeout).ConfigureAwait(false),
				"Expected the framework to create the workspace file watcher."
			);

			await watchers
				.LatestFor(workspaceRoot)
				.DispatchAsync(
					new FileChangeBatch([
						new FileChange(Path.Combine(workspaceRoot, "Scripts", "test.test"), FileChangeKind.Changed),
						new FileChange(Path.Combine(workspaceRoot, ".testconfig"), FileChangeKind.Created),
					]),

					CancellationToken.None
				)
				.ConfigureAwait(false);

			// A throwing probe is contained: the change batch is still forwarded, and the settings refresh is
			// skipped because the probe could not report a configuration path.
			Assert.IsTrue(
				await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, s_waitTimeout).ConfigureAwait(false),
				"A contained configuration-path probe failure must not block file forwarding."
			);

			Assert.AreEqual(0, client.GetSentMethodCount("workspace/didChangeConfiguration"));
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task WatcherFailure_FromAnAlreadyReplacedWatcher_IsIgnoredWithoutRecoveryOrReport()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-watcher-stale-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(workspaceRoot);

		try
		{
			var watchers = new RecordingFileWatcherFactory { RealWatcherSpecifications = TestProviders.WatchSpecifications };
			using var client = new FakeLanguageServerClient { IsReady = false };
			using var logger = new RecordingLogger(LogLevel.Debug);

			using LanguageServerFeatureProvider provider = TestProviders.Create(
				workspaceRootDirectoryPaths: [workspaceRoot],
				client,
				workspaceFileWatcherFactory: watchers.Factory,
				logger: logger
			);

			var failures = new List<WorkspaceFileWatchFailure>();

			provider.WorkspaceFileWatchFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider
				.GetHoverAsync(
					filePath: Path.Combine(workspaceRoot, "boot.test"),
					documentText: "return 1",
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false);

			IFileWatcher failedWatcher =
				watchers.LatestFor(workspaceRoot).Watcher
				?? throw new AssertFailedException("Expected the framework to create an initial workspace file watcher.");

			// The first failure is recovered by a replacement watcher and logs its restart attempt.
			watchers.LatestFor(workspaceRoot).Fail(new IOException("Simulated watcher failure."));

			Assert.IsTrue(
				await TestPolling.ForConditionAsync(() => watchers.CreationsFor(workspaceRoot).Count == 2, s_waitTimeout).ConfigureAwait(false)
			);

			// A delayed failure callback from the replaced watcher must not start another recovery, report a
			// failure, or log a second restart attempt.
			watchers.Fail(failedWatcher, new IOException("Simulated stale watcher failure."));

			Assert.HasCount(2, watchers.CreationsFor(workspaceRoot));
			Assert.IsEmpty(failures);
			Assert.ContainsSingle(entry => entry.Contains("Attempting to restart", StringComparison.Ordinal), logger.Logs);

			Assert.Contains(
				entry => entry.Contains("stale", StringComparison.OrdinalIgnoreCase),
				logger.Logs,
				$"Expected a stale-callback debug entry. Logs: {string.Join(" | ", logger.Logs)}"
			);
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task WatcherFailureRaisedDuringStart_IsAbsorbedWithoutCreatingASecondWatcher()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-watcher-start-failure-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(workspaceRoot);

		try
		{
			int createdCount = 0;

			using var client = new FakeLanguageServerClient { IsReady = false };

			using LanguageServerFeatureProvider provider = TestProviders.Create(
				[workspaceRoot],
				client,

				workspaceFileWatcherFactory: (_, _, onWatcherFailed) =>
				{
					int creationIndex = Interlocked.Increment(ref createdCount);

					// Only the first watcher reports a failure synchronously while it starts; a replacement, if one
					// were created, would start cleanly so the assertion isolates the double-creation hazard.
					return creationIndex == 1 ? StubFileWatcher.FailingAtStart(onWatcherFailed) : new StubFileWatcher();
				}
			);

			var failures = new List<WorkspaceFileWatchFailure>();
			provider.WorkspaceFileWatchFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider
				.GetHoverAsync(
					filePath: Path.Combine(workspaceRoot, "boot.test"),
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false);

			await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

			// The start frame accounts for the synchronous failure itself: no nested recovery starts a second
			// watcher, and the startup failure is reported once.
			Assert.AreEqual(1, createdCount, "A failure raised during Start must not start a replacement watcher.");
			Assert.HasCount(1, failures);
			Assert.IsTrue(failures[0].Message.Contains("could not be started", StringComparison.OrdinalIgnoreCase), failures[0].Message);
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task WatcherFailureRecoveryCapture_DoesNotBlockTheNotificationThread()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-recovery-offload-" + Guid.NewGuid().ToString("N"));

		Directory.CreateDirectory(workspaceRoot);

		try
		{
			var watchers = new RecordingFileWatcherFactory { RealWatcherSpecifications = TestProviders.WatchSpecifications };
			using var tracker = new GatedSnapshotTracker();
			using var client = new FakeLanguageServerClient { IsReady = false };

			using LanguageServerFeatureProvider provider = TestProviders.Create(
				workspaceRootDirectoryPaths: [workspaceRoot],
				client,
				workspaceFileWatcherFactory: watchers.Factory,
				workspaceSnapshotTrackerFactory: (_, _) => tracker
			);

			await provider
				.GetHoverAsync(
					filePath: Path.Combine(workspaceRoot, "boot.test"),
					documentText: Content,
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
				.ConfigureAwait(false);

			Assert.HasCount(1, watchers.CreationsFor(workspaceRoot));

			// The recovery capture blocks, but the failure callback must return: the capture runs on a thread-pool
			// thread, not on the notification thread that also serializes disposal. If the offload regressed, the
			// callback would block here until the gate times out and the CaptureCompleted assertion would fail.
			watchers.LatestFor(workspaceRoot).Fail(new IOException("Simulated watcher failure."));

			Assert.IsTrue(
				tracker.CaptureStarted.Wait(s_waitTimeout, TestContext.CancellationToken),
				"The recovery capture should run off the notification thread."
			);

			Assert.IsFalse(tracker.CaptureCompleted.IsSet, "The watcher-failure callback should not wait for the recovery capture to finish.");

			tracker.ReleaseCapture.Set();

			Assert.IsTrue(
				tracker.CaptureCompleted.Wait(s_waitTimeout, TestContext.CancellationToken),
				"The recovery capture should finish once it is released."
			);
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	/// <summary>
	/// A snapshot tracker whose recovery capture blocks until it is released, so a test can prove the watcher-failure callback does not wait for it.
	/// </summary>
	private sealed class GatedSnapshotTracker : IFileSnapshotTracker, IDisposable
	{
		public ManualResetEventSlim CaptureStarted { get; } = new(false);

		public ManualResetEventSlim CaptureCompleted { get; } = new(false);

		public ManualResetEventSlim ReleaseCapture { get; } = new(false);

		public void CaptureTrackedSnapshot() { }

		public Dictionary<string, FileSnapshotEntry> CloneTrackedSnapshot() => new(LocalPathComparisonPolicy.ForCurrentPlatform.Comparer);

		public Dictionary<string, FileSnapshotEntry> ReplaceTrackedSnapshotWithCurrent()
		{
			CaptureStarted.Set();
			ReleaseCapture.Wait(TimeSpan.FromSeconds(10));
			CaptureCompleted.Set();

			return new Dictionary<string, FileSnapshotEntry>(LocalPathComparisonPolicy.ForCurrentPlatform.Comparer);
		}

		public void ApplyChanges(IReadOnlyList<FileChange> changes) { }

		public FileChangeBatch BuildDeltaBatch(
			IReadOnlyDictionary<string, FileSnapshotEntry> previousSnapshot,
			IReadOnlyDictionary<string, FileSnapshotEntry> currentSnapshot
		) => new([]);

		public void Dispose()
		{
			CaptureStarted.Dispose();
			CaptureCompleted.Dispose();
			ReleaseCapture.Dispose();
		}
	}

	public TestContext? TestContext { get; set; }
}
