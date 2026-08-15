using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers workspace-watching opt-out and failure containment: an empty specification list disables watching
/// without breaking requests, and a throwing watcher factory surfaces through the watcher-failure report
/// instead of escaping request APIs. Also covers nested-root routing: only the scope that owns a path forwards
/// it, so overlapping watchers produce exactly one notification.
/// </summary>
[TestClass]
public sealed class WorkspaceWatchingTests
{
	private const string Content = "line one";

	private static readonly string s_workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-watching-" + Guid.NewGuid().ToString("N"));
	private static readonly string s_filePath = Path.Combine(s_workspaceRoot, "Scripts", "test.test");
	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(2);

	[TestMethod]
	public async Task WorkspaceSendFailure_MarksTheTransportUnavailableAndReplaysTheBufferedChanges()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-sendfail-" + Guid.NewGuid().ToString("N"));
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.test");

		Directory.CreateDirectory(workspaceRoot);

		try
		{
			var watcherCapture = new TestWorkspaceWatcherCapture();
			using var client = new FakeLanguageServerClient { IsReady = false };
			using var provider = new TestLanguageServerProvider([workspaceRoot], client, workspaceFileWatcherFactory: watcherCapture.Create);

			await provider.GetHoverAsync(filePath, Content, new TextPosition(0, 0)).ConfigureAwait(false);
			Assert.IsTrue(await TestPolling.ForConditionAsync(() => watcherCapture.Dispatch is not null, s_waitTimeout).ConfigureAwait(false));

			Func<FileChangeBatch, CancellationToken, Task> dispatch = watcherCapture.Dispatch!;

			client.SendNotificationHandler = (method, _, _) =>
			{
				if (string.Equals(method, "workspace/didChangeWatchedFiles", StringComparison.Ordinal))
					throw new IOException("Simulated workspace send failure.");

				return Task.CompletedTask;
			};

			// Delivering one watched-files batch through the root's dispatch path fails on the transport send and
			// must mark the observed generation unhealthy while the batch stays buffered for replay.
			await dispatch(new FileChangeBatch([new WorkspaceFileChange(filePath, FileChangeKind.Changed)]), CancellationToken.None).ConfigureAwait(false);

			Assert.IsTrue(await TestPolling.ForConditionAsync(() => provider.State == LanguageServerProviderState.Unavailable, s_waitTimeout).ConfigureAwait(false));
			Assert.AreEqual(1, client.MarkedUnhealthyGenerations.Count);
			Assert.IsFalse(provider.IsAvailable);

			client.SendNotificationHandler = null;

			// The next request restarts the transport and replays the buffered changes exactly once.
			await provider.GetHoverAsync(filePath, Content, new TextPosition(0, 0)).ConfigureAwait(false);

			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, s_waitTimeout).ConfigureAwait(false));
			Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
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
		var watcherCapture = new TestWorkspaceWatcherCapture();
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestDisabledWatchingProvider([s_workspaceRoot], client,
			workspaceFileWatcherFactory: watcherCapture.Create);
		var failures = new List<WorkspaceWatcherFailure>();

		provider.WorkspaceWatcherFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

		// Request, open, and update must all complete normally with workspace watching disabled.
		Assert.IsNull(await provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		provider.UpdateDocument(s_filePath, "line two");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, s_waitTimeout).ConfigureAwait(false));

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.AreEqual(0, watcherCapture.CreatedCount);
		Assert.AreEqual(0, failures.Count);
	}

	[TestMethod]
	public async Task ThrowingWatcherFactory_ReportsWatcherFailureOnceWithoutEscapingRequests()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client,
			workspaceFileWatcherFactory: static (_, _, _) => throw new InvalidOperationException("Simulated watcher factory failure."));
		var failures = new List<WorkspaceWatcherFailure>();

		provider.WorkspaceWatcherFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

		// The request completes its documented fallback value; the factory failure must not surface as an exception.
		Assert.IsNull(await provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));
		Assert.IsNull(await provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => failures.Count == 1, s_waitTimeout).ConfigureAwait(false));
		Assert.IsTrue(failures[0].Message.Contains("could not be started", StringComparison.OrdinalIgnoreCase), failures[0].Message);
		Assert.AreEqual(s_workspaceRoot, failures[0].WorkspaceRootDirectoryPath);
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
	}

	[TestMethod]
	public async Task NestedRoots_OverlappingWatchers_ForwardNestedChangeOnce()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-nested-" + Guid.NewGuid().ToString("N"));
		string nestedRoot = Path.Combine(workspaceRoot, "nested");

		Directory.CreateDirectory(nestedRoot);

		try
		{
			var watcherCapture = new TestMultiRootWatcherCapture();
			using var client = new FakeLanguageServerClient { IsReady = false };
			using var provider = new TestLanguageServerProvider([workspaceRoot, nestedRoot], client, workspaceFileWatcherFactory: watcherCapture.Create);

			await provider.GetHoverAsync(Path.Combine(workspaceRoot, "boot.test"), "return 1", new TextPosition(0, 0)).ConfigureAwait(false);

			Assert.IsTrue(await TestPolling.ForConditionAsync(() => watcherCapture.Dispatches.Count == 2, s_waitTimeout).ConfigureAwait(false),
				"Expected one workspace file watcher per workspace root.");

			Func<FileChangeBatch, CancellationToken, Task> outerDispatch = watcherCapture.Dispatches[workspaceRoot];
			Func<FileChangeBatch, CancellationToken, Task> nestedDispatch = watcherCapture.Dispatches[nestedRoot];

			string nestedFilePath = Path.Combine(nestedRoot, "Scripts", "nested.test");
			string nestedConfigurationPath = Path.Combine(nestedRoot, ".testconfig");

			FileChangeBatch nestedBatch = new(
			[
				new WorkspaceFileChange(nestedFilePath, FileChangeKind.Changed),
				new WorkspaceFileChange(nestedConfigurationPath, FileChangeKind.Created)
			]);

			// The outer root's watcher observes the nested subtree and delivers the batch first. The delivering
			// scope does not own these paths, so nothing may be forwarded or refreshed from this delivery.
			await outerDispatch(nestedBatch, CancellationToken.None).ConfigureAwait(false);

			Assert.AreEqual(0, client.GetSentMethodCount("workspace/didChangeWatchedFiles"),
				"A nested workspace root's changes must only be forwarded by the scope that owns them.");
			Assert.AreEqual(0, client.GetSentMethodCount("workspace/didChangeConfiguration"));

			// The nested root's own watcher delivers the same batch: both changes are forwarded exactly once and
			// the settings refresh comes from the owning (nested) scope.
			await nestedDispatch(nestedBatch, CancellationToken.None).ConfigureAwait(false);

			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, s_waitTimeout).ConfigureAwait(false));
			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeConfiguration", 1, s_waitTimeout).ConfigureAwait(false));
			Assert.AreEqual(1, client.GetSentMethodCount("workspace/didChangeWatchedFiles"),
				"The nested change must be forwarded exactly once across both watchers.");
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
			var watcherCapture = new TestMultiRootWatcherCapture();
			using var client = new FakeLanguageServerClient { IsReady = false };
			using var provider = new TestLanguageServerProvider([workspaceRoot, missingNestedRoot], client, workspaceFileWatcherFactory: watcherCapture.Create);

			await provider.GetHoverAsync(Path.Combine(workspaceRoot, "boot.test"), "return 1", new TextPosition(0, 0)).ConfigureAwait(false);

			Assert.IsTrue(await TestPolling.ForConditionAsync(() => watcherCapture.Dispatches.Count == 2, s_waitTimeout).ConfigureAwait(false),
				"Expected the framework to attempt one workspace file watcher per workspace root.");

			// The nested root does not exist, so its scope never keeps a started watcher: the outer watcher must
			// cover the subtree instead of filtering the change away.
			await watcherCapture.Dispatches[workspaceRoot](new FileChangeBatch(
			[
				new WorkspaceFileChange(Path.Combine(missingNestedRoot, "Scripts", "nested.test"), FileChangeKind.Changed)
			]), CancellationToken.None).ConfigureAwait(false);

			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, s_waitTimeout).ConfigureAwait(false),
				"The delivering watcher must cover a nested root whose own watcher is not active.");
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
		using var provider = new TestLanguageServerProvider([missingRoot], client);
		var failures = new List<WorkspaceWatcherFailure>();

		provider.WorkspaceWatcherFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

		string filePath = Path.Combine(missingRoot, "Scripts", "test.test");

		Assert.IsNull(await provider.GetHoverAsync(filePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));
		Assert.IsTrue(provider.IsAvailable);
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.AreEqual(0, failures.Count, "A missing workspace root must not be reported as a watcher failure.");
	}

	[TestMethod]
	public async Task WatcherRuntimeFailure_IsRecoveredAndReportsOnlyUnrecoverableFailures()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-watcher-recovery-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(workspaceRoot);

		try
		{
			var watcherCapture = new TestWorkspaceWatcherCapture();
			using var client = new FakeLanguageServerClient { IsReady = false };
			using var provider = new TestLanguageServerProvider([workspaceRoot], client, workspaceFileWatcherFactory: watcherCapture.Create);
			var failures = new List<WorkspaceWatcherFailure>();

			provider.WorkspaceWatcherFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(Path.Combine(workspaceRoot, "boot.test"), "return 1", new TextPosition(0, 0)).ConfigureAwait(false);

			Assert.AreEqual(1, watcherCapture.CreatedCount);

			// A runtime watcher failure is recovered by a replacement watcher without raising the
			// unrecoverable-failure report.
			watcherCapture.Fail(new IOException("Simulated watcher failure."));

			Assert.IsTrue(await TestPolling.ForConditionAsync(() => watcherCapture.CreatedCount == 2, s_waitTimeout).ConfigureAwait(false));
			Assert.AreEqual(0, failures.Count);

			// When the replacement cannot be created either, the failure is reported once.
			watcherCapture.ThrowOnCreate = true;
			watcherCapture.Fail(new IOException("Simulated watcher failure."));

			Assert.IsTrue(await TestPolling.ForConditionAsync(() => failures.Count == 1, s_waitTimeout).ConfigureAwait(false));
			Assert.IsTrue(failures[0].Message.Contains("automatic recovery failed", StringComparison.OrdinalIgnoreCase), failures[0].Message);
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
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
			var watcherCapture = new TestWorkspaceWatcherCapture { UsePassiveWatcher = true };
			using var client = new FakeLanguageServerClient { IsReady = false };
			using var provider = new TestLanguageServerProvider([workspaceRoot], client, workspaceFileWatcherFactory: watcherCapture.Create);

			await provider.GetHoverAsync(Path.Combine(workspaceRoot, "boot.test"), Content, new TextPosition(0, 0)).ConfigureAwait(false);

			// The request awaited the startup flow, so the watcher exists and the recovery baseline is the
			// workspace as it was before the file below appeared.
			Assert.AreEqual(1, watcherCapture.CreatedCount);

			File.WriteAllText(missedFilePath, Content);

			// Replacing the watcher reconciles the tracked snapshot against the current workspace, so the missed
			// creation is replayed as one watched-files change.
			watcherCapture.Fail(new IOException("Simulated watcher failure."));

			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, s_waitTimeout).ConfigureAwait(false));

			JsonElement changes = client.GetLastNotificationParameters("workspace/didChangeWatchedFiles").GetProperty("changes");

			Assert.AreEqual(1, changes.GetArrayLength());
			Assert.AreEqual(new Uri(missedFilePath).AbsoluteUri, changes[0].GetProperty("uri").GetString());
			Assert.AreEqual((int)FileChangeKind.Created, changes[0].GetProperty("type").GetInt32());
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
			var watcherCapture = new TestMultiRootWatcherCapture();
			using var client = new FakeLanguageServerClient { IsReady = false };
			using var provider = new TestLanguageServerProvider([workspaceRoot], client, workspaceFileWatcherFactory: watcherCapture.Create)
			{
				ThrowOnConfigurationPathProbe = true
			};

			await provider.GetHoverAsync(Path.Combine(workspaceRoot, "boot.test"), "return 1", new TextPosition(0, 0)).ConfigureAwait(false);

			Assert.IsTrue(await TestPolling.ForConditionAsync(() => watcherCapture.Dispatches.Count == 1, s_waitTimeout).ConfigureAwait(false),
				"Expected the framework to create the workspace file watcher.");

			await watcherCapture.Dispatches[workspaceRoot](new FileChangeBatch(
			[
				new WorkspaceFileChange(Path.Combine(workspaceRoot, "Scripts", "test.test"), FileChangeKind.Changed),
				new WorkspaceFileChange(Path.Combine(workspaceRoot, ".testconfig"), FileChangeKind.Created)
			]), CancellationToken.None).ConfigureAwait(false);

			// A throwing probe is contained: the change batch is still forwarded, and the settings refresh is
			// skipped because the probe could not report a configuration path.
			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, s_waitTimeout).ConfigureAwait(false),
				"A contained configuration-path probe failure must not block file forwarding.");
			Assert.AreEqual(0, client.GetSentMethodCount("workspace/didChangeConfiguration"));
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task SettingsPayload_WhenThrowing_IsContainedAndChangesAreStillForwarded()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-payload-throw-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(workspaceRoot);

		try
		{
			var watcherCapture = new TestMultiRootWatcherCapture();
			using var client = new FakeLanguageServerClient { IsReady = false };
			using var provider = new TestLanguageServerProvider([workspaceRoot], client, workspaceFileWatcherFactory: watcherCapture.Create)
			{
				ThrowOnSettingsPayload = true
			};

			await provider.GetHoverAsync(Path.Combine(workspaceRoot, "boot.test"), "return 1", new TextPosition(0, 0)).ConfigureAwait(false);

			Assert.IsTrue(await TestPolling.ForConditionAsync(() => watcherCapture.Dispatches.Count == 1, s_waitTimeout).ConfigureAwait(false),
				"Expected the framework to create the workspace file watcher.");

			await watcherCapture.Dispatches[workspaceRoot](new FileChangeBatch(
			[
				new WorkspaceFileChange(Path.Combine(workspaceRoot, ".testconfig"), FileChangeKind.Changed)
			]), CancellationToken.None).ConfigureAwait(false);

			// A throwing settings payload is contained: the configuration notification is skipped, and the
			// watched-files notification still reports the change.
			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, s_waitTimeout).ConfigureAwait(false),
				"A contained settings-payload failure must not block the watched-files notification.");
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
			var watcherCapture = new TestWorkspaceWatcherCapture();
			using var client = new FakeLanguageServerClient { IsReady = false };
			using var logScope = new TestLoggerScope(LogLevel.Debug);
			using var provider = new TestLanguageServerProvider([workspaceRoot], client,
				workspaceFileWatcherFactory: watcherCapture.Create,
				logger: logScope);
			var failures = new List<WorkspaceWatcherFailure>();

			provider.WorkspaceWatcherFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(Path.Combine(workspaceRoot, "boot.test"), "return 1", new TextPosition(0, 0)).ConfigureAwait(false);

			IWorkspaceFileWatcher failedWatcher = watcherCapture.Watcher
				?? throw new AssertFailedException("Expected the framework to create an initial workspace file watcher.");

			// The first failure is recovered by a replacement watcher and logs its restart attempt.
			watcherCapture.Fail(new IOException("Simulated watcher failure."));
			Assert.IsTrue(await TestPolling.ForConditionAsync(() => watcherCapture.CreatedCount == 2, s_waitTimeout).ConfigureAwait(false));

			// A delayed failure callback from the replaced watcher must not start another recovery, report a
			// failure, or log a second restart attempt.
			watcherCapture.Fail(failedWatcher, new IOException("Simulated stale watcher failure."));

			Assert.AreEqual(2, watcherCapture.CreatedCount);
			Assert.AreEqual(0, failures.Count);
			Assert.AreEqual(1, logScope.Logs.Count(entry => entry.Contains("Attempting to restart", StringComparison.Ordinal)));
			Assert.IsTrue(logScope.Logs.Any(entry => entry.Contains("stale", StringComparison.OrdinalIgnoreCase)),
				$"Expected a stale-callback debug entry. Logs: {string.Join(" | ", logScope.Logs)}");
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
			using var provider = new TestLanguageServerProvider([workspaceRoot], client,
				workspaceFileWatcherFactory: (_, _, onWatcherFailed) =>
				{
					int creationIndex = Interlocked.Increment(ref createdCount);

					// Only the first watcher reports a failure synchronously while it starts; a replacement, if one
					// were created, would start cleanly so the assertion isolates the double-creation hazard.
					return creationIndex == 1
						? new TestFailingStartWorkspaceFileWatcher(onWatcherFailed)
						: new TestPassiveWorkspaceFileWatcher();
				});

			var failures = new List<WorkspaceWatcherFailure>();
			provider.WorkspaceWatcherFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(Path.Combine(workspaceRoot, "boot.test"), Content, new TextPosition(0, 0)).ConfigureAwait(false);

			await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

			// The start frame accounts for the synchronous failure itself: no nested recovery starts a second
			// watcher, and the startup failure is reported once.
			Assert.AreEqual(1, createdCount, "A failure raised during Start must not start a replacement watcher.");
			Assert.AreEqual(1, failures.Count);
			Assert.IsTrue(failures[0].Message.Contains("could not be started", StringComparison.OrdinalIgnoreCase), failures[0].Message);
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}
}
