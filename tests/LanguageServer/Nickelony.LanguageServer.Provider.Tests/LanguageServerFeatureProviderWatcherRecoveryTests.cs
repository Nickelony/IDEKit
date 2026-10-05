using System.Text.Json;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the workspace file watcher the provider owns: startup, restart, failure reports and recovery reconciliation.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderWatcherRecoveryTests
{
	[TestMethod]
	public async Task GetHoverAsync_RetriesWorkspaceFileWatcherStartAfterWorkspaceDirectoryAppears()
	{
		// The watcher must observe a root that does not exist yet, so the path is deliberately not a
		// TemporaryDirectory (which creates the directory); the test creates it mid-test and the finally
		// deletes it best-effort.
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "WatcherRetry_" + Guid.NewGuid().ToString("N"));
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.test");

		const string content = "local value = 1";

		try
		{
			using var client = new FakeStatefulLanguageServerClient();
			var watchers = new RecordingFileWatcherFactory { PassiveWatchersReportAMissingRoot = true };

			using LanguageServerFeatureProvider provider = TestProviders.CreateWatched([workspaceRoot], client, watchers);
			var failures = new List<WorkspaceFileWatchFailure>();

			provider.WorkspaceFileWatchFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			Assert.IsNull(watchers.GetActiveWatcher(workspaceRoot));
			Assert.IsEmpty(failures);

			Directory.CreateDirectory(workspaceRoot);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			Assert.IsNotNull(watchers.GetActiveWatcher(workspaceRoot));
			Assert.IsEmpty(failures);
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task GetHoverAsync_WatcherStartupFailure_RaisesWorkspaceFileWatchFailureOnce()
	{
		var workspace = new TemporaryDirectory("WatcherStartupFailure_");

		string workspaceRoot = workspace.Path;
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.test");

		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(workspaceRoot);

			using var client = new FakeStatefulLanguageServerClient();

			var watchers = new RecordingFileWatcherFactory
			{
				PassiveWatchersReportAMissingRoot = true,
				PassiveWatcherStartupFailure = new InvalidOperationException("Simulated watcher creation failure."),
			};

			using LanguageServerFeatureProvider provider = TestProviders.Create(
				[workspaceRoot],
				client,
				workspaceFileWatcherFactory: watchers.Factory
			);

			var failures = new List<WorkspaceFileWatchFailure>();

			provider.WorkspaceFileWatchFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));
			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			Assert.IsNull(watchers.GetActiveWatcher(workspaceRoot));
			Assert.HasCount(1, failures);
			Assert.IsTrue(failures[0].Message.Contains("could not be started", StringComparison.OrdinalIgnoreCase));
			Assert.AreEqual(ProviderFailureCodes.FileWatchStartFailed, failures[0].Code);
			Assert.AreEqual("Simulated watcher creation failure.", failures[0].Exception?.Message);
			Assert.AreEqual(workspaceRoot, failures[0].WorkspaceRootDirectoryPath);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public async Task WorkspaceFileWatchFailure_AutomaticallyRestartsWithoutRaisingFailure()
	{
		var workspace = new TemporaryDirectory("WatcherFailure_");

		string workspaceRoot = workspace.Path;
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.test");

		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(workspaceRoot);

			using var client = new FakeStatefulLanguageServerClient();
			var watchers = new RecordingFileWatcherFactory { PassiveWatchersReportAMissingRoot = true };

			using LanguageServerFeatureProvider provider = TestProviders.CreateWatched([workspaceRoot], client, watchers);
			var failures = new List<WorkspaceFileWatchFailure>();

			provider.WorkspaceFileWatchFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			StubFileWatcher watcher =
				watchers.GetActiveWatcher(workspaceRoot) ?? throw new AssertFailedException("Expected the workspace watcher to start.");

			Assert.IsFalse(watcher.IsDisposed);

			watchers.Fail(watcher, new IOException("Simulated watcher failure."));

			StubFileWatcher replacementWatcher =
				watchers.GetActiveWatcher(workspaceRoot) ?? throw new AssertFailedException("Expected the workspace watcher to restart.");

			Assert.AreNotSame(watcher, replacementWatcher);
			Assert.IsTrue(watcher.IsDisposed);
			Assert.IsFalse(replacementWatcher.IsDisposed);
			Assert.IsEmpty(failures);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public async Task WorkspaceFileWatchFailure_WhenReplacementRestartFails_DisposesBothWatchersAndRaisesFailure()
	{
		var workspace = new TemporaryDirectory("WatcherFailure_");

		string workspaceRoot = workspace.Path;
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.test");

		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(workspaceRoot);

			using var client = new FakeStatefulLanguageServerClient();
			var watchers = new RecordingFileWatcherFactory();

			// The first watcher starts, and the replacement the recovery creates after its failure cannot start.
			StubFileWatcher initialWatcher = watchers.Enqueue(workspaceRoot, StubFileWatcher.ForWorkspaceRoot(workspaceRoot));

			StubFileWatcher replacementWatcher = watchers.Enqueue(
				workspaceRoot,
				StubFileWatcher.ForWorkspaceRoot(workspaceRoot, startupFailure: new InvalidOperationException("Simulated watcher creation failure."))
			);

			using LanguageServerFeatureProvider provider = TestProviders.Create(
				[workspaceRoot],
				client,
				workspaceFileWatcherFactory: watchers.Factory
			);

			var failures = new List<WorkspaceFileWatchFailure>();
			provider.WorkspaceFileWatchFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			watchers.Fail(initialWatcher, new IOException("Simulated watcher failure."));

			Assert.HasCount(2, watchers.CreationsFor(workspaceRoot));
			Assert.IsTrue(initialWatcher.IsDisposed);
			Assert.IsTrue(replacementWatcher.IsDisposed);
			Assert.IsNull(watchers.GetActiveWatcher(workspaceRoot));
			Assert.HasCount(1, failures);
			Assert.AreEqual(ProviderFailureCodes.FileWatchRecoveryFailed, failures[0].Code);
			Assert.AreEqual("Simulated watcher failure.", failures[0].Exception?.Message);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public async Task WorkspaceFileWatchFailure_RepeatedAutomaticRestartsContinueWithoutRaisingFailure()
	{
		var workspace = new TemporaryDirectory("WatcherRepeatedFailure_");

		string workspaceRoot = workspace.Path;
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.test");

		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(workspaceRoot);

			using var client = new FakeStatefulLanguageServerClient();
			var watchers = new RecordingFileWatcherFactory { PassiveWatchersReportAMissingRoot = true };

			using LanguageServerFeatureProvider provider = TestProviders.CreateWatched([workspaceRoot], client, watchers);
			var failures = new List<WorkspaceFileWatchFailure>();

			provider.WorkspaceFileWatchFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			StubFileWatcher firstWatcher =
				watchers.GetActiveWatcher(workspaceRoot) ?? throw new AssertFailedException("Expected the initial workspace watcher to start.");

			watchers.Fail(firstWatcher, new IOException("Simulated watcher failure 1."));

			StubFileWatcher secondWatcher =
				watchers.GetActiveWatcher(workspaceRoot) ?? throw new AssertFailedException("Expected the first replacement watcher to start.");

			watchers.Fail(secondWatcher, new IOException("Simulated watcher failure 2."));

			StubFileWatcher thirdWatcher =
				watchers.GetActiveWatcher(workspaceRoot) ?? throw new AssertFailedException("Expected the second replacement watcher to start.");

			Assert.AreNotSame(firstWatcher, secondWatcher);
			Assert.AreNotSame(secondWatcher, thirdWatcher);
			Assert.IsTrue(firstWatcher.IsDisposed);
			Assert.IsTrue(secondWatcher.IsDisposed);
			Assert.IsFalse(thirdWatcher.IsDisposed);
			Assert.IsEmpty(failures);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public async Task WorkspaceFileWatchFailure_WithNullException_StillRecoversWithoutRaisingFailure()
	{
		var workspace = new TemporaryDirectory("WatcherNullFailure_");

		string workspaceRoot = workspace.Path;
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.test");

		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(workspaceRoot);

			using var client = new FakeStatefulLanguageServerClient();
			var watchers = new RecordingFileWatcherFactory { PassiveWatchersReportAMissingRoot = true };

			using LanguageServerFeatureProvider provider = TestProviders.CreateWatched([workspaceRoot], client, watchers);
			var failures = new List<WorkspaceFileWatchFailure>();

			provider.WorkspaceFileWatchFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			StubFileWatcher watcher =
				watchers.GetActiveWatcher(workspaceRoot) ?? throw new AssertFailedException("Expected the workspace watcher to start.");

			// A watcher may report a failure without an exception; recovery must not depend on one.
			watchers.Fail(watcher, null);

			StubFileWatcher replacementWatcher =
				watchers.GetActiveWatcher(workspaceRoot) ?? throw new AssertFailedException("Expected the workspace watcher to restart.");

			Assert.AreNotSame(watcher, replacementWatcher);
			Assert.IsTrue(watcher.IsDisposed);
			Assert.IsFalse(replacementWatcher.IsDisposed);
			Assert.IsEmpty(failures);
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task WorkspaceFileWatchFailure_OneRootFailing_LeavesTheOtherRootForwarding()
	{
		var primaryWorkspace = new TemporaryDirectory("WatcherIsolationPrimary_");
		string primaryRoot = primaryWorkspace.Path;

		var secondaryWorkspace = new TemporaryDirectory("WatcherIsolationSecondary_");

		string secondaryRoot = secondaryWorkspace.Path;
		string filePath = Path.Combine(primaryRoot, "Scripts", "test.test");

		try
		{
			Directory.CreateDirectory(primaryRoot);
			Directory.CreateDirectory(secondaryRoot);

			using var client = new FakeStatefulLanguageServerClient();
			var watchers = new RecordingFileWatcherFactory { PassiveWatchersReportAMissingRoot = true };

			using LanguageServerFeatureProvider provider = TestProviders.CreateWatched([primaryRoot, secondaryRoot], client, watchers);
			var failures = new List<WorkspaceFileWatchFailure>();

			provider.WorkspaceFileWatchFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.StartWorkspaceWatcherAsync(client, watchers, primaryRoot);

			StubFileWatcher primaryWatcher =
				watchers.GetActiveWatcher(primaryRoot) ?? throw new AssertFailedException("Expected the primary root's workspace watcher to start.");

			StubFileWatcher secondaryWatcher =
				watchers.GetActiveWatcher(secondaryRoot)
				?? throw new AssertFailedException("Expected the secondary root's workspace watcher to start.");

			watchers.Fail(secondaryWatcher, new IOException("Simulated secondary-root watcher failure."));

			StubFileWatcher replacementSecondaryWatcher =
				watchers.GetActiveWatcher(secondaryRoot)
				?? throw new AssertFailedException("Expected the secondary root's workspace watcher to restart.");

			// Recovery is contained to the failing root: the primary root's watcher is untouched, and the
			// primary root keeps forwarding live changes while the secondary root recovers.
			Assert.AreNotSame(secondaryWatcher, replacementSecondaryWatcher);
			Assert.IsTrue(secondaryWatcher.IsDisposed);
			Assert.AreSame(primaryWatcher, watchers.GetActiveWatcher(primaryRoot));
			Assert.IsFalse(primaryWatcher.IsDisposed);

			await watchers
				.LatestFor(primaryRoot)
				.DispatchAsync(new FileChangeBatch([new FileChange(filePath, FileChangeKind.Created)]), CancellationToken.None);

			Assert.AreEqual(1, client.GetSentMethodCount("workspace/didChangeWatchedFiles"));
			Assert.IsEmpty(failures);
		}
		finally
		{
			primaryWorkspace.Dispose();
			secondaryWorkspace.Dispose();
		}
	}

	[TestMethod]
	public async Task WorkspaceFileWatcherRecovery_ReconcilesFilesCreatedWhileWatcherWasDown()
	{
		var workspace = new TemporaryDirectory("WatcherRecoveryCreate_");

		string workspaceRoot = workspace.Path;
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.test");
		string missedFilePath = Path.Combine(workspaceRoot, "Scripts", "missed.test");

		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? workspaceRoot);

			using var client = new FakeStatefulLanguageServerClient();
			var watchers = new RecordingFileWatcherFactory { PassiveWatchersReportAMissingRoot = true };

			using LanguageServerFeatureProvider provider = TestProviders.CreateWatched([workspaceRoot], client, watchers);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			StubFileWatcher watcher =
				watchers.GetActiveWatcher(workspaceRoot) ?? throw new AssertFailedException("Expected the workspace watcher to start.");

			File.WriteAllText(missedFilePath, "return 1");

			watchers.Fail(watcher, new IOException("Simulated watcher failure."));
			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, TestPolling.DefaultTimeout));

			JsonElement changes = client.GetLastNotificationParameters("workspace/didChangeWatchedFiles").GetProperty("changes");

			Assert.AreEqual(1, changes.GetArrayLength());
			Assert.AreEqual(new Uri(missedFilePath).AbsoluteUri, changes[0].GetProperty("uri").GetString());
			Assert.AreEqual((int)FileChangeType.Created, changes[0].GetProperty("type").GetInt32());
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public async Task WorkspaceFileWatcherRecovery_ReplaysConfigurationRefreshForMissedWorkspaceConfigChanges()
	{
		var workspace = new TemporaryDirectory("WatcherRecoveryConfig_");

		string workspaceRoot = workspace.Path;
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.test");
		string configFilePath = Path.Combine(workspaceRoot, ".testconfig");

		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? workspaceRoot);

			using var client = new FakeStatefulLanguageServerClient();
			var watchers = new RecordingFileWatcherFactory { PassiveWatchersReportAMissingRoot = true };

			using LanguageServerFeatureProvider provider = TestProviders.CreateWatched([workspaceRoot], client, watchers);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			StubFileWatcher watcher =
				watchers.GetActiveWatcher(workspaceRoot) ?? throw new AssertFailedException("Expected the workspace watcher to start.");

			File.WriteAllText(configFilePath, "{\"Test.maxPreload\": 1000}");

			watchers.Fail(watcher, new IOException("Simulated watcher failure."));

			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeConfiguration", 1, TestPolling.DefaultTimeout));
			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, TestPolling.DefaultTimeout));

			string[] sentMethods = client.GetSentMethodNames();

			Assert.AreSequenceEqual(
				["textDocument/didOpen", "textDocument/hover", "workspace/didChangeConfiguration", "workspace/didChangeWatchedFiles"],
				sentMethods
			);

			JsonElement changes = client.GetLastNotificationParameters("workspace/didChangeWatchedFiles").GetProperty("changes");

			Assert.AreEqual(1, changes.GetArrayLength());
			Assert.AreEqual(new Uri(configFilePath).AbsoluteUri, changes[0].GetProperty("uri").GetString());
			Assert.AreEqual((int)FileChangeType.Created, changes[0].GetProperty("type").GetInt32());
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public async Task WorkspaceFileWatcherRecovery_DoesNotReplayChangesAlreadyForwardedBeforeTheOutage()
	{
		var workspace = new TemporaryDirectory("WatcherRecoveryDuplicate_");

		string workspaceRoot = workspace.Path;
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.test");

		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? workspaceRoot);
			File.WriteAllText(filePath, content);

			using var client = new FakeStatefulLanguageServerClient();
			var watchers = new RecordingFileWatcherFactory { PassiveWatchersReportAMissingRoot = true };

			using LanguageServerFeatureProvider provider = TestProviders.CreateWatched([workspaceRoot], client, watchers);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			File.WriteAllText(filePath, content + Environment.NewLine + "return value");

			var batch = new FileChangeBatch([new FileChange(filePath, FileChangeKind.Changed)]);

			await watchers.LatestFor(workspaceRoot).DispatchAsync(batch, CancellationToken.None);

			Assert.AreEqual(1, client.GetSentMethodCount("workspace/didChangeWatchedFiles"));

			StubFileWatcher watcher =
				watchers.GetActiveWatcher(workspaceRoot) ?? throw new AssertFailedException("Expected the workspace watcher to start.");

			watchers.Fail(watcher, new IOException("Simulated watcher failure."));

			// Negative check: no observable signal exists for "no replay happened", so the bounded window is deliberate.
			await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

			Assert.AreEqual(1, client.GetSentMethodCount("workspace/didChangeWatchedFiles"));
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public async Task WorkspaceFileWatcherRecovery_ReconcilesDroppedChangesFromUnexpectedForwardingFailure()
	{
		var workspace = new TemporaryDirectory("WatcherRecoveryDropped_");

		string workspaceRoot = workspace.Path;
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.test");

		const string content = "local value = 1";
		const string updatedContent = "local value = 2";

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? workspaceRoot);
			File.WriteAllText(filePath, content);

			using var client = new FakeStatefulLanguageServerClient
			{
				ThrowInvalidOperationOnNextWatchedFilesNotification = true,
				HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
			};

			var watchers = new RecordingFileWatcherFactory { PassiveWatchersReportAMissingRoot = true };

			using LanguageServerFeatureProvider provider = TestProviders.CreateWatched([workspaceRoot], client, watchers);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			StubFileWatcher watcher =
				watchers.GetActiveWatcher(workspaceRoot) ?? throw new AssertFailedException("Expected the workspace watcher to start.");

			File.WriteAllText(filePath, updatedContent);

			await watchers
				.LatestFor(workspaceRoot)
				.DispatchAsync(new FileChangeBatch([new FileChange(filePath, FileChangeKind.Changed)]), CancellationToken.None);

			Assert.AreEqual(1, client.GetSentMethodCount("workspace/didChangeWatchedFiles"));

			watchers.Fail(watcher, new IOException("Simulated watcher failure."));
			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 2, TestPolling.DefaultTimeout));

			JsonElement changes = client.GetLastNotificationParameters("workspace/didChangeWatchedFiles").GetProperty("changes");

			Assert.AreEqual(1, changes.GetArrayLength());
			Assert.AreEqual(new Uri(filePath).AbsoluteUri, changes[0].GetProperty("uri").GetString());
			Assert.AreEqual((int)FileChangeType.Changed, changes[0].GetProperty("type").GetInt32());
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public async Task WorkspaceFileWatcherRecovery_ConcurrentDispatchDuringRecovery_ConvergesWithoutExtraReplayOnNextRecovery()
	{
		var workspace = new TemporaryDirectory("WatcherRecoveryConcurrent_");

		string workspaceRoot = workspace.Path;
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.test");
		string reconciledFilePath = Path.Combine(workspaceRoot, "Scripts", "reconciled.test");
		string liveFilePath = Path.Combine(workspaceRoot, "Scripts", "live.test");

		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? workspaceRoot);

			using var client = new FakeStatefulLanguageServerClient();
			var watchers = new RecordingFileWatcherFactory { PassiveWatchersReportAMissingRoot = true };

			using LanguageServerFeatureProvider provider = TestProviders.CreateWatched([workspaceRoot], client, watchers);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			StubFileWatcher watcher =
				watchers.GetActiveWatcher(workspaceRoot) ?? throw new AssertFailedException("Expected the workspace watcher to start.");

			File.WriteAllText(reconciledFilePath, "return 1");
			File.WriteAllText(liveFilePath, "return 2");

			client.BlockNextWatchedFilesNotification();

			Task liveDispatchTask = watchers
				.LatestFor(workspaceRoot)
				.DispatchAsync(new FileChangeBatch([new FileChange(liveFilePath, FileChangeKind.Created)]), CancellationToken.None);

			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, TestPolling.DefaultTimeout));

			watchers.Fail(watcher, new IOException("Simulated watcher failure."));

			client.ReleaseWatchedFilesNotification();
			await liveDispatchTask.ConfigureAwait(false);

			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 2, TestPolling.DefaultTimeout));

			StubFileWatcher replacementWatcher =
				watchers.GetActiveWatcher(workspaceRoot) ?? throw new AssertFailedException("Expected the replacement workspace watcher to start.");

			watchers.Fail(replacementWatcher, new IOException("Simulated watcher failure."));

			// Negative check: no observable signal exists for "no extra replay happened", so the bounded window is deliberate.
			await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

			Assert.AreEqual(2, client.GetSentMethodCount("workspace/didChangeWatchedFiles"));
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public async Task GetHoverAsync_TwoRoots_StartsAndDisposesOneWatcherPerRoot()
	{
		var primaryWorkspace = new TemporaryDirectory("MultiRootPrimary_");
		string primaryRoot = primaryWorkspace.Path;

		var secondaryWorkspace = new TemporaryDirectory("MultiRootSecondary_");

		string secondaryRoot = secondaryWorkspace.Path;
		string filePath = Path.Combine(primaryRoot, "Scripts", "test.test");

		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(primaryRoot);
			Directory.CreateDirectory(secondaryRoot);

			using var client = new FakeStatefulLanguageServerClient();
			var watchers = new RecordingFileWatcherFactory { PassiveWatchersReportAMissingRoot = true };

			using LanguageServerFeatureProvider provider = TestProviders.CreateWatched([primaryRoot, secondaryRoot], client, watchers);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			StubFileWatcher primaryWatcher =
				watchers.GetActiveWatcher(primaryRoot) ?? throw new AssertFailedException("Expected the primary root's workspace watcher to start.");

			StubFileWatcher secondaryWatcher =
				watchers.GetActiveWatcher(secondaryRoot)
				?? throw new AssertFailedException("Expected the secondary root's workspace watcher to start.");

			Assert.AreNotSame(primaryWatcher, secondaryWatcher);

			provider.Dispose();

			Assert.IsTrue(primaryWatcher.IsDisposed);
			Assert.IsTrue(secondaryWatcher.IsDisposed);
		}
		finally
		{
			primaryWorkspace.Dispose();
			secondaryWorkspace.Dispose();
		}
	}

	[TestMethod]
	public async Task GetHoverAsync_TwoRoots_IsolatesWatcherStartupFailureToItsOwnRoot()
	{
		var healthyWorkspace = new TemporaryDirectory("MultiRootHealthy_");
		string healthyRoot = healthyWorkspace.Path;

		var failingWorkspace = new TemporaryDirectory("MultiRootFailing_");

		string failingRoot = failingWorkspace.Path;
		string filePath = Path.Combine(healthyRoot, "Scripts", "test.test");
		string forwardedFilePath = Path.Combine(healthyRoot, "Scripts", "generated.test");

		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(healthyRoot);
			Directory.CreateDirectory(failingRoot);

			using var client = new FakeStatefulLanguageServerClient();

			var watchers = new RecordingFileWatcherFactory { PassiveWatchersReportAMissingRoot = true };

			watchers.Enqueue(
				failingRoot,
				StubFileWatcher.ForWorkspaceRoot(failingRoot, startupFailure: new InvalidOperationException("Simulated watcher creation failure."))
			);

			using LanguageServerFeatureProvider provider = TestProviders.Create(
				[healthyRoot, failingRoot],
				client,
				workspaceFileWatcherFactory: watchers.Factory
			);

			var failures = new List<WorkspaceFileWatchFailure>();

			provider.WorkspaceFileWatchFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			Assert.HasCount(1, failures);
			Assert.Contains(failingRoot, failures[0].Message);
			Assert.IsFalse(failures[0].Message.Contains(healthyRoot, StringComparison.Ordinal));

			await watchers
				.LatestFor(healthyRoot)
				.DispatchAsync(new FileChangeBatch([new FileChange(forwardedFilePath, FileChangeKind.Changed)]), CancellationToken.None);

			Assert.AreEqual(1, client.GetSentMethodCount("workspace/didChangeWatchedFiles"));
		}
		finally
		{
			healthyWorkspace.Dispose();
			failingWorkspace.Dispose();
		}
	}

	public TestContext? TestContext { get; set; }
}
