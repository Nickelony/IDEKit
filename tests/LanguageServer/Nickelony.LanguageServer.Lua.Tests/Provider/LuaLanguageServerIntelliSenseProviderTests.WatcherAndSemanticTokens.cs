using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

public sealed partial class LuaLanguageServerIntelliSenseProviderTests
{
	[TestMethod]
	public async Task GetHoverAsync_RetriesWorkspaceWatcherStartAfterWorkspaceDirectoryAppears()
	{
		// The watcher must observe a root that does not exist yet, so the path is deliberately not a
		// TemporaryDirectory (which creates the directory); the test creates it mid-test and the finally
		// deletes it best-effort.
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "LuaWatcherRetry_" + Guid.NewGuid().ToString("N"));
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		const string content = "local value = 1";

		try
		{
			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);
			var failures = new List<WorkspaceWatcherFailure>();

			provider.WorkspaceWatcherFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			Assert.IsNull(GetWorkspaceWatcher(provider));
			Assert.AreEqual(0, failures.Count);

			Directory.CreateDirectory(workspaceRoot);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			Assert.IsNotNull(GetWorkspaceWatcher(provider));
			Assert.AreEqual(0, failures.Count);
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task GetHoverAsync_WatcherStartupFailure_RaisesWorkspaceWatcherFailureOnce()
	{
		var workspace = new TemporaryDirectory("LuaWatcherStartupFailure_");
		string workspaceRoot = workspace.Path;
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(workspaceRoot);

			using var client = new FakeLanguageServerClient();

			using var provider = new LuaLanguageServerIntelliSenseProvider(
				[workspaceRoot],
				client,
				workspaceFileWatcherFactory: (rootPath, dispatchAsync, onWatcherFailed) => new TestWorkspaceFileWatcher(
					rootPath,
					dispatchAsync,
					onWatcherFailed,
					new InvalidOperationException("Simulated watcher creation failure.")));

			var failures = new List<WorkspaceWatcherFailure>();

			provider.WorkspaceWatcherFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));
			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			Assert.IsNull(GetWorkspaceWatcher(provider));
			Assert.AreEqual(1, failures.Count);
			Assert.IsTrue(failures[0].Message.Contains("could not be started", StringComparison.OrdinalIgnoreCase));
			Assert.AreEqual(workspaceRoot, failures[0].WorkspaceRootDirectoryPath);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public async Task WorkspaceWatcherFailure_AutomaticallyRestartsWithoutRaisingFailure()
	{
		var workspace = new TemporaryDirectory("LuaWatcherFailure_");
		string workspaceRoot = workspace.Path;
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(workspaceRoot);

			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);
			var failures = new List<WorkspaceWatcherFailure>();

			provider.WorkspaceWatcherFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			TestWorkspaceFileWatcher watcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the workspace watcher to start.");

			Assert.IsFalse(watcher.IsDisposed);

			SimulateWatcherFailure(watcher, new IOException("Simulated watcher failure."));

			TestWorkspaceFileWatcher replacementWatcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the workspace watcher to restart.");

			Assert.AreNotSame(watcher, replacementWatcher);
			Assert.IsTrue(watcher.IsDisposed);
			Assert.IsFalse(replacementWatcher.IsDisposed);
			Assert.AreEqual(0, failures.Count);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public async Task WorkspaceWatcherFailure_WhenReplacementRestartFails_DisposesBothWatchersAndRaisesFailure()
	{
		var workspace = new TemporaryDirectory("LuaWatcherFailure_");
		string workspaceRoot = workspace.Path;
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(workspaceRoot);

			using var client = new FakeLanguageServerClient();
			var createdWatchers = new List<TestWorkspaceFileWatcher>();
			int watcherCreationCount = 0;
			Action<IWorkspaceFileWatcher, Exception?>? onWatcherFailed = null;

			using var provider = new LuaLanguageServerIntelliSenseProvider(
				[workspaceRoot],
				client,
				workspaceFileWatcherFactory: (rootPath, dispatchAsync, registeredOnWatcherFailed) =>
				{
					onWatcherFailed = registeredOnWatcherFailed;

					var watcher = new TestWorkspaceFileWatcher(
						rootPath,
						dispatchAsync,
						registeredOnWatcherFailed,
						watcherCreationCount++ == 0
							? null
							: new InvalidOperationException("Simulated watcher creation failure."));
					createdWatchers.Add(watcher);
					return watcher;
				});

			var failures = new List<WorkspaceWatcherFailure>();
			provider.WorkspaceWatcherFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			TestWorkspaceFileWatcher watcher = createdWatchers[0];

			onWatcherFailed!(watcher, new IOException("Simulated watcher failure."));

			Assert.AreEqual(2, createdWatchers.Count);
			Assert.IsTrue(createdWatchers[0].IsDisposed);
			Assert.IsTrue(createdWatchers[1].IsDisposed);
			Assert.IsNull(GetWorkspaceWatcher(provider));
			Assert.AreEqual(1, failures.Count);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public async Task WorkspaceWatcherFailure_RepeatedAutomaticRestartsContinueWithoutRaisingFailure()
	{
		var workspace = new TemporaryDirectory("LuaWatcherRepeatedFailure_");
		string workspaceRoot = workspace.Path;
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(workspaceRoot);

			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);
			var failures = new List<WorkspaceWatcherFailure>();

			provider.WorkspaceWatcherFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			TestWorkspaceFileWatcher firstWatcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the initial workspace watcher to start.");

			SimulateWatcherFailure(firstWatcher, new IOException("Simulated watcher failure 1."));

			TestWorkspaceFileWatcher secondWatcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the first replacement watcher to start.");

			SimulateWatcherFailure(secondWatcher, new IOException("Simulated watcher failure 2."));

			TestWorkspaceFileWatcher thirdWatcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the second replacement watcher to start.");

			Assert.AreNotSame(firstWatcher, secondWatcher);
			Assert.AreNotSame(secondWatcher, thirdWatcher);
			Assert.IsTrue(firstWatcher.IsDisposed);
			Assert.IsTrue(secondWatcher.IsDisposed);
			Assert.IsFalse(thirdWatcher.IsDisposed);
			Assert.AreEqual(0, failures.Count);
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public async Task WorkspaceWatcherFailure_WithNullException_StillRecoversWithoutRaisingFailure()
	{
		var workspace = new TemporaryDirectory("LuaWatcherNullFailure_");
		string workspaceRoot = workspace.Path;
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(workspaceRoot);

			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);
			var failures = new List<WorkspaceWatcherFailure>();

			provider.WorkspaceWatcherFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			TestWorkspaceFileWatcher watcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the workspace watcher to start.");

			// A watcher may report a failure without an exception; recovery must not depend on one.
			SimulateWatcherFailure(watcher, null);

			TestWorkspaceFileWatcher replacementWatcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the workspace watcher to restart.");

			Assert.AreNotSame(watcher, replacementWatcher);
			Assert.IsTrue(watcher.IsDisposed);
			Assert.IsFalse(replacementWatcher.IsDisposed);
			Assert.AreEqual(0, failures.Count);
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task WorkspaceWatcherFailure_OneRootFailing_LeavesTheOtherRootForwarding()
	{
		var primaryWorkspace = new TemporaryDirectory("LuaWatcherIsolationPrimary_");
		string primaryRoot = primaryWorkspace.Path;
		var secondaryWorkspace = new TemporaryDirectory("LuaWatcherIsolationSecondary_");
		string secondaryRoot = secondaryWorkspace.Path;
		string filePath = Path.Combine(primaryRoot, "Scripts", "test.lua");

		try
		{
			Directory.CreateDirectory(primaryRoot);
			Directory.CreateDirectory(secondaryRoot);

			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture([primaryRoot, secondaryRoot], client, out _);
			var failures = new List<WorkspaceWatcherFailure>();

			provider.WorkspaceWatcherFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await StartProviderAndCaptureWorkspaceWatcherAsync(provider, client, primaryRoot);

			TestWorkspaceFileWatcher primaryWatcher = GetWorkspaceWatcher(provider, primaryRoot)
				?? throw new AssertFailedException("Expected the primary root's workspace watcher to start.");
			TestWorkspaceFileWatcher secondaryWatcher = GetWorkspaceWatcher(provider, secondaryRoot)
				?? throw new AssertFailedException("Expected the secondary root's workspace watcher to start.");

			SimulateWatcherFailure(secondaryWatcher, new IOException("Simulated secondary-root watcher failure."));

			TestWorkspaceFileWatcher replacementSecondaryWatcher = GetWorkspaceWatcher(provider, secondaryRoot)
				?? throw new AssertFailedException("Expected the secondary root's workspace watcher to restart.");

			// Recovery is contained to the failing root: the primary root's watcher is untouched, and the
			// primary root keeps forwarding live changes while the secondary root recovers.
			Assert.AreNotSame(secondaryWatcher, replacementSecondaryWatcher);
			Assert.IsTrue(secondaryWatcher.IsDisposed);
			Assert.AreSame(primaryWatcher, GetWorkspaceWatcher(provider, primaryRoot));
			Assert.IsFalse(primaryWatcher.IsDisposed);

			await DispatchWorkspaceFileChangesAsync(
				provider,
				primaryRoot,
				new FileChangeBatch(
				[
					new WorkspaceFileChange(filePath, FileChangeKind.Created)
				]),
				CancellationToken.None);

			Assert.AreEqual(1, CountSentMethods(client, "workspace/didChangeWatchedFiles"));
			Assert.AreEqual(0, failures.Count);
		}
		finally
		{
			primaryWorkspace.Dispose();
			secondaryWorkspace.Dispose();
		}
	}

	[TestMethod]
	public async Task WorkspaceWatcherRecovery_ReconcilesLuaFilesCreatedWhileWatcherWasDown()
	{
		var workspace = new TemporaryDirectory("LuaWatcherRecoveryCreate_");
		string workspaceRoot = workspace.Path;
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		string missedFilePath = Path.Combine(workspaceRoot, "Scripts", "missed.lua");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? workspaceRoot);

			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			TestWorkspaceFileWatcher watcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the workspace watcher to start.");

			File.WriteAllText(missedFilePath, "return 1");

			SimulateWatcherFailure(watcher, new IOException("Simulated watcher failure."));
			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, TestPolling.DefaultTimeout));

			JsonElement changes = client.GetLastNotificationParameters("workspace/didChangeWatchedFiles").GetProperty("changes");
			Assert.AreEqual(1, changes.GetArrayLength());
			Assert.AreEqual(new Uri(missedFilePath).AbsoluteUri, changes[0].GetProperty("uri").GetString());
			Assert.AreEqual((int)FileChangeKind.Created, changes[0].GetProperty("type").GetInt32());
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public async Task WorkspaceWatcherRecovery_ReplaysConfigurationRefreshForMissedWorkspaceConfigChanges()
	{
		var workspace = new TemporaryDirectory("LuaWatcherRecoveryConfig_");
		string workspaceRoot = workspace.Path;
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		string configFilePath = Path.Combine(workspaceRoot, ".luarc.json");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? workspaceRoot);

			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			TestWorkspaceFileWatcher watcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the workspace watcher to start.");

			File.WriteAllText(configFilePath, "{\"Lua.workspace.maxPreload\": 1000}");

			SimulateWatcherFailure(watcher, new IOException("Simulated watcher failure."));
			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeConfiguration", 1, TestPolling.DefaultTimeout));
			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, TestPolling.DefaultTimeout));

			string[] sentMethods = client.GetSentMethodNames();

			CollectionAssert.AreEqual(
				new[] { "textDocument/didOpen", "textDocument/hover", "workspace/didChangeConfiguration", "workspace/didChangeWatchedFiles" },
				sentMethods);

			JsonElement changes = client.GetLastNotificationParameters("workspace/didChangeWatchedFiles").GetProperty("changes");
			Assert.AreEqual(1, changes.GetArrayLength());
			Assert.AreEqual(new Uri(configFilePath).AbsoluteUri, changes[0].GetProperty("uri").GetString());
			Assert.AreEqual((int)FileChangeKind.Created, changes[0].GetProperty("type").GetInt32());
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public async Task WorkspaceWatcherRecovery_DoesNotReplayChangesAlreadyForwardedBeforeTheOutage()
	{
		var workspace = new TemporaryDirectory("LuaWatcherRecoveryDuplicate_");
		string workspaceRoot = workspace.Path;
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? workspaceRoot);
			File.WriteAllText(filePath, content);

			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			File.WriteAllText(filePath, content + Environment.NewLine + "return value");

			var batch = new FileChangeBatch(
			[
				new WorkspaceFileChange(filePath, FileChangeKind.Changed)
			]);

			await DispatchWorkspaceFileChangesAsync(provider, batch, CancellationToken.None);

			Assert.AreEqual(1, CountSentMethods(client, "workspace/didChangeWatchedFiles"));

			TestWorkspaceFileWatcher watcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the workspace watcher to start.");

			SimulateWatcherFailure(watcher, new IOException("Simulated watcher failure."));

			// Negative check: no observable signal exists for "no replay happened", so the bounded window is deliberate.
			await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

			Assert.AreEqual(1, CountSentMethods(client, "workspace/didChangeWatchedFiles"));
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public async Task WorkspaceWatcherRecovery_ReconcilesDroppedChangesFromUnexpectedForwardingFailure()
	{
		var workspace = new TemporaryDirectory("LuaWatcherRecoveryDropped_");
		string workspaceRoot = workspace.Path;
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		const string content = "local value = 1";
		const string updatedContent = "local value = 2";

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? workspaceRoot);
			File.WriteAllText(filePath, content);

			using var client = new FakeLanguageServerClient
			{
				ThrowInvalidOperationOnNextWatchedFilesNotification = true,
				HoverResponse = JsonSerializer.SerializeToElement(new
				{
					contents = new
					{
						kind = "markdown",
						value = "Hover docs."
					}
				})
			};

			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			TestWorkspaceFileWatcher watcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the workspace watcher to start.");

			File.WriteAllText(filePath, updatedContent);

			await DispatchWorkspaceFileChangesAsync(
				provider,
				new FileChangeBatch(
				[
					new WorkspaceFileChange(filePath, FileChangeKind.Changed)
				]),
				CancellationToken.None);

			Assert.AreEqual(1, CountSentMethods(client, "workspace/didChangeWatchedFiles"));

			SimulateWatcherFailure(watcher, new IOException("Simulated watcher failure."));
			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 2, TestPolling.DefaultTimeout));

			JsonElement changes = client.GetLastNotificationParameters("workspace/didChangeWatchedFiles").GetProperty("changes");
			Assert.AreEqual(1, changes.GetArrayLength());
			Assert.AreEqual(new Uri(filePath).AbsoluteUri, changes[0].GetProperty("uri").GetString());
			Assert.AreEqual((int)FileChangeKind.Changed, changes[0].GetProperty("type").GetInt32());
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public async Task WorkspaceWatcherRecovery_ConcurrentDispatchDuringRecovery_ConvergesWithoutExtraReplayOnNextRecovery()
	{
		var workspace = new TemporaryDirectory("LuaWatcherRecoveryConcurrent_");
		string workspaceRoot = workspace.Path;
		string filePath = Path.Combine(workspaceRoot, "Scripts", "test.lua");
		string reconciledFilePath = Path.Combine(workspaceRoot, "Scripts", "reconciled.lua");
		string liveFilePath = Path.Combine(workspaceRoot, "Scripts", "live.lua");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? workspaceRoot);

			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture(workspaceRoot, client, out _);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			TestWorkspaceFileWatcher watcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the workspace watcher to start.");

			File.WriteAllText(reconciledFilePath, "return 1");
			File.WriteAllText(liveFilePath, "return 2");

			client.BlockNextWatchedFilesNotification();

			Task liveDispatchTask = DispatchWorkspaceFileChangesAsync(
				provider,
				new FileChangeBatch(
				[
					new WorkspaceFileChange(liveFilePath, FileChangeKind.Created)
				]),
				CancellationToken.None);

			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, TestPolling.DefaultTimeout));

			SimulateWatcherFailure(watcher, new IOException("Simulated watcher failure."));

			client.ReleaseWatchedFilesNotification();
			await liveDispatchTask.ConfigureAwait(false);

			Assert.IsTrue(await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 2, TestPolling.DefaultTimeout));

			TestWorkspaceFileWatcher replacementWatcher = GetWorkspaceWatcher(provider)
				?? throw new AssertFailedException("Expected the replacement workspace watcher to start.");

			SimulateWatcherFailure(replacementWatcher, new IOException("Simulated watcher failure."));

			// Negative check: no observable signal exists for "no extra replay happened", so the bounded window is deliberate.
			await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

			Assert.AreEqual(2, CountSentMethods(client, "workspace/didChangeWatchedFiles"));
		}
		finally
		{
			workspace.Dispose();
		}
	}

	[TestMethod]
	public async Task SemanticTokensRefreshRequested_RefreshesTheTrackedDocument()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var semanticTokensUpdated = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (LanguageServerPaths.AreLocalPathsEqual(eventArgs.FilePath, filePath))
				semanticTokensUpdated.TrySetResult(eventArgs.SemanticTokens);
		};

		await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

		client.PublishSemanticTokensRefreshRequested();

		Task completedTask = await Task.WhenAny(semanticTokensUpdated.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(semanticTokensUpdated.Task, completedTask);

		IReadOnlyList<SemanticToken> semanticTokens = await semanticTokensUpdated.Task.ConfigureAwait(false);

		Assert.AreEqual(1, semanticTokens.Count);
		Assert.AreEqual("variable", semanticTokens[0].Type);

		CollectionAssert.AreEqual(
			new[] { "textDocument/didOpen", "textDocument/hover", "textDocument/semanticTokens/full" },
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task SemanticTokensRefreshRequested_SupersededRequest_AppliesOnlyTheFreshResponse()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		client.EnqueueSemanticTokensFullResponse(JsonSerializer.SerializeToElement(new
		{
			data = new[] { 0, 6, 5, 0, 0 },
			resultId = "tokens-1"
		}));

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var semanticTokenUpdates = new List<IReadOnlyList<SemanticToken>>();
		var initialUpdate = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var freshUpdate = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (!LanguageServerPaths.AreLocalPathsEqual(eventArgs.FilePath, filePath))
				return;

			lock (semanticTokenUpdates)
				semanticTokenUpdates.Add(eventArgs.SemanticTokens);

			if (!initialUpdate.Task.IsCompleted)
				initialUpdate.TrySetResult(eventArgs.SemanticTokens);
			else
				freshUpdate.TrySetResult(eventArgs.SemanticTokens);
		};

		provider.OpenDocument(filePath, content);

		Task initialCompletedTask = await Task.WhenAny(initialUpdate.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(initialUpdate.Task, initialCompletedTask);
		Assert.AreEqual(1, provider.GetSemanticTokens(filePath).Count);

		// Two refreshes are in flight while both full-token requests are parked on their response
		// gates: the second refresh supersedes (cancels) the first before either response arrives.
		// Each gate pins its request's response so the assertion cannot depend on resume order.
		client.BlockNextSemanticTokensFullRequest(JsonSerializer.SerializeToElement(new
		{
			data = new[] { 0, 7, 2, 0, 0 },
			resultId = "tokens-2"
		}));

		client.BlockNextSemanticTokensFullRequest(JsonSerializer.SerializeToElement(new
		{
			data = new[] { 0, 9, 3, 0, 0 },
			resultId = "tokens-3"
		}));

		client.PublishSemanticTokensRefreshRequested();
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 2, TestPolling.DefaultTimeout));

		client.PublishSemanticTokensRefreshRequested();
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 3, TestPolling.DefaultTimeout));

		// Releasing the gates resumes the superseded request first; it must discard its response, then
		// the fresh request applies its own.
		client.ReleaseSemanticTokensFullRequest();
		client.ReleaseSemanticTokensFullRequest();

		Task freshCompletedTask = await Task.WhenAny(freshUpdate.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(freshUpdate.Task, freshCompletedTask);

		IReadOnlyList<SemanticToken> freshTokens = await freshUpdate.Task.ConfigureAwait(false);

		Assert.AreEqual(1, freshTokens.Count);
		Assert.AreEqual(9, freshTokens[0].Character);
		Assert.AreEqual(3, freshTokens[0].Length);

		Assert.AreEqual(1, provider.GetSemanticTokens(filePath).Count);
		Assert.AreEqual(9, provider.GetSemanticTokens(filePath)[0].Character);

		// Negative check: the superseded payload never surfaces; the bounded window is deliberate
		// because "no extra update happened" has no completion signal.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		lock (semanticTokenUpdates)
			Assert.AreEqual(2, semanticTokenUpdates.Count);
	}

	[TestMethod]
	public async Task OpenDocument_DisposeDuringInFlightSemanticTokensRequest_DoesNotRaiseSemanticTokensUpdated()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		client.BlockNextSemanticTokensFullRequest();

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var semanticTokensUpdated = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) => semanticTokensUpdated.TrySetResult(eventArgs.SemanticTokens);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));

		provider.Dispose();
		client.ReleaseSemanticTokensFullRequest();

		Task completedTask = await Task.WhenAny(semanticTokensUpdated.Task, Task.Delay(TestPolling.AbsenceWindow)).ConfigureAwait(false);

		Assert.AreNotSame(semanticTokensUpdated.Task, completedTask);
	}

	[TestMethod]
	public async Task SemanticTokensRefreshResponseWithoutTokenStream_KeepsPreviouslyCachedTokens()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		client.EnqueueSemanticTokensFullResponse(JsonSerializer.SerializeToElement(new
		{
			data = new[] { 0, 6, 5, 0, 0 },
			resultId = "tokens-1"
		}));

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var semanticTokenUpdates = new List<IReadOnlyList<SemanticToken>>();
		var initialUpdate = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (!LanguageServerPaths.AreLocalPathsEqual(eventArgs.FilePath, filePath))
				return;

			lock (semanticTokenUpdates)
				semanticTokenUpdates.Add(eventArgs.SemanticTokens);

			initialUpdate.TrySetResult(eventArgs.SemanticTokens);
		};

		provider.OpenDocument(filePath, content);

		Task completedTask = await Task.WhenAny(initialUpdate.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(initialUpdate.Task, completedTask);

		// A response without a usable token stream (here no `data` member at all) must keep the cached
		// token set and raise no event.
		client.BlockNextSemanticTokensFullRequest(JsonSerializer.SerializeToElement(new
		{
			resultId = "tokens-without-data"
		}));

		client.PublishSemanticTokensRefreshRequested();
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 2, TestPolling.DefaultTimeout).ConfigureAwait(false));

		client.ReleaseSemanticTokensFullRequest();

		// Negative check: the discarded payload has no completion signal, so the bounded window is deliberate.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		lock (semanticTokenUpdates)
			Assert.AreEqual(1, semanticTokenUpdates.Count);

		Assert.AreEqual(1, provider.GetSemanticTokens(filePath).Count);
		Assert.AreEqual(6, provider.GetSemanticTokens(filePath)[0].Character);
	}

	[TestMethod]
	public async Task CloseDocument_CancelsInFlightSemanticTokensRequest_KeepsCachedTokens()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		client.BlockNextSemanticTokensFullRequest();

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var semanticTokenUpdates = 0;

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (LanguageServerPaths.AreLocalPathsEqual(eventArgs.FilePath, filePath))
				Interlocked.Increment(ref semanticTokenUpdates);
		};

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));

		// Closing the document cancels its in-flight refresh before the parked response arrives; the
		// released response is then discarded instead of being stored for a closed document. The close
		// itself is queued (fire-and-forget), so wait for the didClose notification - the invalidation
		// hook that cancels the refresh runs before it - instead of racing the queued close work.
		provider.CloseDocument(filePath);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TestPolling.DefaultTimeout));

		client.ReleaseSemanticTokensFullRequest();

		// Negative check: no observable signal exists for "the discarded response raised nothing",
		// so the bounded window is deliberate.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.AreEqual(0, semanticTokenUpdates);
		Assert.AreEqual(0, provider.GetSemanticTokens(filePath).Count);
	}

	[TestMethod]
	public async Task SemanticTokensRefreshRequested_RequestFailure_KeepsCachedSemanticTokens()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var tokenUpdates = new List<IReadOnlyList<SemanticToken>>();
		var initialTokensUpdated = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (!LanguageServerPaths.AreLocalPathsEqual(eventArgs.FilePath, filePath))
				return;

			lock (tokenUpdates)
				tokenUpdates.Add(eventArgs.SemanticTokens);

			initialTokensUpdated.TrySetResult(eventArgs.SemanticTokens);
		};

		provider.OpenDocument(filePath, content);

		Task initialCompletedTask = await Task.WhenAny(initialTokensUpdated.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(initialTokensUpdated.Task, initialCompletedTask);
		Assert.AreEqual(1, provider.GetSemanticTokens(filePath).Count);

		client.ThrowInvalidOperationOnNextRequestMethod = "textDocument/semanticTokens/full";
		client.PublishSemanticTokensRefreshRequested();

		// The failed request is sent (second full request), and because it fails the refresh keeps the
		// last known token set instead of clearing the highlighting.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 2, TestPolling.DefaultTimeout));

		// Negative check: a failed refresh produces no observable update event, so the bounded window
		// is deliberate.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		lock (tokenUpdates)
			Assert.AreEqual(1, tokenUpdates.Count);

		IReadOnlyList<SemanticToken> cachedTokens = provider.GetSemanticTokens(filePath);

		Assert.AreEqual(1, cachedTokens.Count);
		Assert.AreEqual("variable", cachedTokens[0].Type);

		CollectionAssert.AreEqual(
			new[]
			{
				"textDocument/didOpen",
				"textDocument/semanticTokens/full",
				"textDocument/semanticTokens/full"
			},
			client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task SemanticTokensRefreshAfterAdmissionClosed_PublishesNoRequest()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));

		provider.Dispose();

		// Disposal closes request admission before it drains in-flight requests; a refresh that still reaches
		// the replacement step must abort through a canceled token without publishing a source the drain has
		// already passed.
		(CancellationToken effectiveToken, bool publishedSource) =
			LuaLanguageServerIntelliSenseProviderTestAccess.InvokeSemanticTokenRequestReplacement(provider, filePath);

		Assert.IsTrue(effectiveToken.IsCancellationRequested);
		Assert.IsFalse(publishedSource);
	}

	[TestMethod]
	public async Task OpenDocument_SemanticTokenModifiers_AreDecodedFromTheLegend()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"],
			SemanticTokenModifiers = ["declaration", "readonly"]
		};

		// The first token carries modifier bit 0, which the legend maps back to "declaration".
		client.EnqueueSemanticTokensFullResponse(JsonSerializer.SerializeToElement(new
		{
			data = new[] { 0, 6, 5, 0, 1 },
			resultId = "tokens-1"
		}));

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var semanticTokensUpdated = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (LanguageServerPaths.AreLocalPathsEqual(eventArgs.FilePath, filePath))
				semanticTokensUpdated.TrySetResult(eventArgs.SemanticTokens);
		};

		provider.OpenDocument(filePath, "local value = 1");

		Task completedTask = await Task.WhenAny(semanticTokensUpdated.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(semanticTokensUpdated.Task, completedTask);

		IReadOnlyList<SemanticToken> semanticTokens = await semanticTokensUpdated.Task.ConfigureAwait(false);

		Assert.AreEqual(1, semanticTokens.Count);
		Assert.AreEqual("variable", semanticTokens[0].Type);
		CollectionAssert.AreEqual(new[] { "declaration" }, semanticTokens[0].Modifiers.ToArray());
	}

	[TestMethod]
	public async Task SemanticTokensRefreshRequested_WithManyDocuments_BoundsTheConcurrentFanOut()
	{
		string workspaceRoot = TestPaths.Root;
		string[] filePaths = [.. Enumerable.Range(0, 6).Select(index => TestPaths.Script($"fanout_{index}.lua"))];

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		foreach (string filePath in filePaths)
			provider.OpenDocument(filePath, "local value = 1");

		// Wait until every open-triggered refresh completed and cached its token set, so no in-flight
		// request can consume a fan-out gate queued below.
		await TestPolling.WaitForAsync(
			() => Task.FromResult(filePaths.All(filePath => provider.GetSemanticTokens(filePath).Count > 0)),
			static allCached => allCached,
			TestPolling.DefaultTimeout,
			"Expected every opened document to cache its semantic tokens.",
			static allCached => $"allCached={allCached}").ConfigureAwait(false);

		// Park the next four requests - the fan-out bound - and trigger a refresh for all six documents.
		for (int i = 0; i < 4; i++)
			client.BlockNextSemanticTokensFullRequest();

		client.PublishSemanticTokensRefreshRequested();

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", filePaths.Length + 4, TestPolling.DefaultTimeout));

		// Negative check: while all four permits are parked, the remaining documents wait instead of
		// issuing an unbounded burst, so the bounded window is deliberate.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);
		Assert.AreEqual(filePaths.Length + 4, CountSentMethods(client, "textDocument/semanticTokens/full"));

		client.ReleaseSemanticTokensFullRequest();
		client.ReleaseSemanticTokensFullRequest();
		client.ReleaseSemanticTokensFullRequest();
		client.ReleaseSemanticTokensFullRequest();

		// Releasing the parked requests admits the remaining documents.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", filePaths.Length + 6, TestPolling.DefaultTimeout));
	}

	[TestMethod]
	public async Task UpdateDocument_WhileSemanticTokensResponseIsStalled_DeliversChangeNotification()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		client.BlockNextSemanticTokensFullRequest();

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, "local value = 1");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 1, TestPolling.DefaultTimeout));

		// The first edit's token refresh is parked on its response gate; the next edit must still reach
		// the server instead of queuing behind the stalled semantic-token round trip.
		provider.UpdateDocument(filePath, "local value = 2");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, TestPolling.DefaultTimeout));

		provider.UpdateDocument(filePath, "local value = 3");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 2, TestPolling.DefaultTimeout));

		client.ReleaseSemanticTokensFullRequest();
	}

	[TestMethod]
	public async Task OpenDocument_OneSemanticTokenSubscriberExceptionDoesNotSuppressLaterSubscribers()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);
		var observedTokens = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, _) => throw new InvalidOperationException("Subscriber failure.");

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (LanguageServerPaths.AreLocalPathsEqual(eventArgs.FilePath, filePath))
				observedTokens.TrySetResult(eventArgs.SemanticTokens);
		};

		provider.OpenDocument(filePath, "local value = 1");

		// A throwing subscriber is isolated: later subscribers still receive the token set.
		Task completedTask = await Task.WhenAny(observedTokens.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(observedTokens.Task, completedTask);

		IReadOnlyList<SemanticToken> semanticTokens = await observedTokens.Task.ConfigureAwait(false);

		Assert.AreEqual(1, semanticTokens.Count);
		Assert.AreEqual("variable", semanticTokens[0].Type);
	}

	[TestMethod]
	public async Task OpenDocument_SemanticTokensFullRequest_OmitsPreviousResultId()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"]
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 1, TestPolling.DefaultTimeout));

		JsonElement parameters = client.GetLastRequestParameters("textDocument/semanticTokens/full");

		Assert.AreEqual(new Uri(filePath).AbsoluteUri, parameters.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.IsFalse(parameters.TryGetProperty("previousResultId", out _));
	}

	[TestMethod]
	public async Task OpenDocument_WithSemanticTokenLegendButNoFullSupport_DoesNotRequestSemanticTokens()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			SupportsSemanticTokensFull = false,
			SemanticTokenTypes = ["variable"]
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));

		CollectionAssert.DoesNotContain(client.GetSentMethodNames(), "textDocument/semanticTokens/full");
	}

	[TestMethod]
	public async Task GetHoverAsync_TwoRoots_StartsAndDisposesOneWatcherPerRoot()
	{
		var primaryWorkspace = new TemporaryDirectory("LuaMultiRootPrimary_");
		string primaryRoot = primaryWorkspace.Path;
		var secondaryWorkspace = new TemporaryDirectory("LuaMultiRootSecondary_");
		string secondaryRoot = secondaryWorkspace.Path;
		string filePath = Path.Combine(primaryRoot, "Scripts", "test.lua");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(primaryRoot);
			Directory.CreateDirectory(secondaryRoot);

			using var client = new FakeLanguageServerClient();
			using var provider = CreateProviderWithWatcherCapture([primaryRoot, secondaryRoot], client, out _);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			TestWorkspaceFileWatcher primaryWatcher = GetWorkspaceWatcher(provider, primaryRoot)
				?? throw new AssertFailedException("Expected the primary root's workspace watcher to start.");
			TestWorkspaceFileWatcher secondaryWatcher = GetWorkspaceWatcher(provider, secondaryRoot)
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
		var healthyWorkspace = new TemporaryDirectory("LuaMultiRootHealthy_");
		string healthyRoot = healthyWorkspace.Path;
		var failingWorkspace = new TemporaryDirectory("LuaMultiRootFailing_");
		string failingRoot = failingWorkspace.Path;
		string filePath = Path.Combine(healthyRoot, "Scripts", "test.lua");
		string forwardedFilePath = Path.Combine(healthyRoot, "Scripts", "generated.lua");
		const string content = "local value = 1";

		try
		{
			Directory.CreateDirectory(healthyRoot);
			Directory.CreateDirectory(failingRoot);

			using var client = new FakeLanguageServerClient();
			Func<FileChangeBatch, CancellationToken, Task>? healthyRootDispatch = null;

			using var provider = new LuaLanguageServerIntelliSenseProvider(
				[healthyRoot, failingRoot],
				client,
				workspaceFileWatcherFactory: (rootPath, dispatchAsync, onWatcherFailed) =>
				{
					if (LanguageServerPaths.AreLocalPathsEqual(rootPath, healthyRoot))
						healthyRootDispatch = dispatchAsync;

					return new TestWorkspaceFileWatcher(
						rootPath,
						dispatchAsync,
						onWatcherFailed,
						LanguageServerPaths.AreLocalPathsEqual(rootPath, failingRoot)
							? new InvalidOperationException("Simulated watcher creation failure.")
							: null);
				});
			var failures = new List<WorkspaceWatcherFailure>();

			provider.WorkspaceWatcherFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

			await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

			Assert.AreEqual(1, failures.Count);
			StringAssert.Contains(failures[0].Message, failingRoot);
			Assert.IsFalse(failures[0].Message.Contains(healthyRoot, StringComparison.Ordinal));

			Func<FileChangeBatch, CancellationToken, Task> dispatch = healthyRootDispatch
				?? throw new AssertFailedException("Expected the healthy root's dispatch delegate to be captured.");

			await dispatch(
				new FileChangeBatch(
				[
					new WorkspaceFileChange(forwardedFilePath, FileChangeKind.Changed)
				]),
				CancellationToken.None);

			Assert.AreEqual(1, CountSentMethods(client, "workspace/didChangeWatchedFiles"));
		}
		finally
		{
			healthyWorkspace.Dispose();
			failingWorkspace.Dispose();
		}
	}
}
