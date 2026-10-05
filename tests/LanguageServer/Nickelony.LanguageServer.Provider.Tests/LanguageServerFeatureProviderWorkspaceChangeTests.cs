using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers how workspace changes are forwarded to the server, buffered across failures and replayed.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderWorkspaceChangeTests
{
	[TestMethod]
	public async Task DispatchWorkspaceChangesAsync_ReplaysBufferedChangesAfterStartupRecovery()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "BufferedWorkspaceReplay_" + Guid.NewGuid().ToString("N"));
		string configurationFilePath = Path.Combine(workspaceRoot, ".testconfig");
		string scriptFilePath = Path.Combine(workspaceRoot, "Scripts", "test.test");

		try
		{
			Directory.CreateDirectory(workspaceRoot);

			using var client = new FakeStatefulLanguageServerClient
			{
				IsReady = false,
				StartResult = false,
				HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
			};

			var watchers = new RecordingFileWatcherFactory { PassiveWatchersReportAMissingRoot = true };

			using LanguageServerFeatureProvider provider = TestProviders.CreateWatched([workspaceRoot], client, watchers);

			await provider.StartWorkspaceWatcherAsync(client, watchers, workspaceRoot);

			var batch = new FileChangeBatch([new FileChange(configurationFilePath, FileChangeKind.Changed)]);

			await watchers.LatestFor(workspaceRoot).DispatchAsync(batch, CancellationToken.None);

			Assert.IsEmpty(client.GetSentMethodNames());

			client.StartResult = true;

			HoverInformation? hover = await provider.GetHoverAsync(scriptFilePath, "local value = 1", new TextPosition(0, 0));

			Assert.IsNotNull(hover);

			Assert.AreSequenceEqual(
				["workspace/didChangeConfiguration", "workspace/didChangeWatchedFiles", "textDocument/didOpen", "textDocument/hover"],
				client.GetSentMethodNames()
			);
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task DispatchWorkspaceChangesAsync_ReplaysBufferedChangesAfterWorkspaceNotificationConnectionFailure()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "BufferedWorkspaceConnection_" + Guid.NewGuid().ToString("N"));
		string changedFilePath = Path.Combine(workspaceRoot, "Scripts", "generated.test");
		string scriptFilePath = Path.Combine(workspaceRoot, "Scripts", "test.test");

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(changedFilePath) ?? workspaceRoot);

			using var client = new FakeStatefulLanguageServerClient
			{
				ThrowIOExceptionOnNextWatchedFilesNotification = true,
				HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
			};

			var watchers = new RecordingFileWatcherFactory { PassiveWatchersReportAMissingRoot = true };

			using LanguageServerFeatureProvider provider = TestProviders.CreateWatched([workspaceRoot], client, watchers);

			await provider.StartWorkspaceWatcherAsync(client, watchers, workspaceRoot);

			var batch = new FileChangeBatch([new FileChange(changedFilePath, FileChangeKind.Changed)]);

			await watchers.LatestFor(workspaceRoot).DispatchAsync(batch, CancellationToken.None);

			Assert.AreSequenceEqual(["workspace/didChangeWatchedFiles"], client.GetSentMethodNames());

			HoverInformation? hover = await provider.GetHoverAsync(scriptFilePath, "local value = 1", new TextPosition(0, 0));

			Assert.IsNotNull(hover);

			Assert.AreSequenceEqual(
				["workspace/didChangeWatchedFiles", "workspace/didChangeWatchedFiles", "textDocument/didOpen", "textDocument/hover"],
				client.GetSentMethodNames()
			);
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task GetHoverAsync_ReplaysBufferedWorkspaceChangesAfterReplayCancellation()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "BufferedWorkspaceCancellation_" + Guid.NewGuid().ToString("N"));
		string changedFilePath = Path.Combine(workspaceRoot, "Scripts", "generated.test");
		string scriptFilePath = Path.Combine(workspaceRoot, "Scripts", "test.test");

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(changedFilePath) ?? workspaceRoot);

			using var client = new FakeStatefulLanguageServerClient
			{
				IsReady = false,
				StartResult = false,
				HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
			};

			var watchers = new RecordingFileWatcherFactory { PassiveWatchersReportAMissingRoot = true };

			using LanguageServerFeatureProvider provider = TestProviders.CreateWatched([workspaceRoot], client, watchers);

			await provider.StartWorkspaceWatcherAsync(client, watchers, workspaceRoot);

			var batch = new FileChangeBatch([new FileChange(changedFilePath, FileChangeKind.Changed)]);

			await watchers.LatestFor(workspaceRoot).DispatchAsync(batch, CancellationToken.None);

			client.StartResult = true;

			using var cancellationTokenSource = new CancellationTokenSource();
			cancellationTokenSource.Cancel();

			Task<HoverInformation?> canceledHoverTask = provider.GetHoverAsync(
				filePath: scriptFilePath,
				documentText: "local value = 1",
				position: new TextPosition(0, 0),
				cancellationToken: cancellationTokenSource.Token
			);

			HoverInformation? recoveredHover = await provider.GetHoverAsync(scriptFilePath, "local value = 1", new TextPosition(0, 0));

			await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => canceledHoverTask);
			Assert.IsNotNull(recoveredHover);

			Assert.AreSequenceEqual(["workspace/didChangeWatchedFiles", "textDocument/didOpen", "textDocument/hover"], client.GetSentMethodNames());
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task DispatchWorkspaceChangesAsync_UnexpectedNotificationFailureDoesNotMarkConnectionUnhealthyAndDropsChanges()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "BufferedWorkspaceUnexpected_" + Guid.NewGuid().ToString("N"));
		string changedFilePath = Path.Combine(workspaceRoot, "Scripts", "generated.test");
		string scriptFilePath = Path.Combine(workspaceRoot, "Scripts", "test.test");

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(changedFilePath) ?? workspaceRoot);

			using var client = new FakeStatefulLanguageServerClient
			{
				ThrowInvalidOperationOnNextWatchedFilesNotification = true,
				HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
			};

			var watchers = new RecordingFileWatcherFactory { PassiveWatchersReportAMissingRoot = true };

			using LanguageServerFeatureProvider provider = TestProviders.CreateWatched([workspaceRoot], client, watchers);

			await provider.StartWorkspaceWatcherAsync(client, watchers, workspaceRoot);

			var batch = new FileChangeBatch([new FileChange(changedFilePath, FileChangeKind.Changed)]);

			await watchers.LatestFor(workspaceRoot).DispatchAsync(batch, CancellationToken.None);

			HoverInformation? hover = await provider.GetHoverAsync(scriptFilePath, "local value = 1", new TextPosition(0, 0));

			Assert.IsNotNull(hover);
			Assert.AreEqual(0, client.MarkConnectionUnhealthyCallCount);
			Assert.AreEqual(1, client.StartCallCount);

			Assert.AreSequenceEqual(["workspace/didChangeWatchedFiles", "textDocument/didOpen", "textDocument/hover"], client.GetSentMethodNames());
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task DispatchWorkspaceChangesAsync_StaleWatcherConnectionFailureDoesNotInvalidateRestartedConnection()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "BufferedWorkspaceStaleConnection_" + Guid.NewGuid().ToString("N"));
		string changedFilePath = Path.Combine(workspaceRoot, "Scripts", "generated.test");
		string scriptFilePath = Path.Combine(workspaceRoot, "Scripts", "test.test");

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(changedFilePath) ?? workspaceRoot);

			using var client = new FakeStatefulLanguageServerClient
			{
				HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
			};

			var watchers = new RecordingFileWatcherFactory { PassiveWatchersReportAMissingRoot = true };

			using LanguageServerFeatureProvider provider = TestProviders.CreateWatched([workspaceRoot], client, watchers);

			await provider.StartWorkspaceWatcherAsync(client, watchers, workspaceRoot);

			client.BlockNextWatchedFilesNotification();
			client.ThrowIOExceptionAfterWatchedFilesNotificationGateRelease = true;

			var batch = new FileChangeBatch([new FileChange(changedFilePath, FileChangeKind.Changed)]);

			Task dispatchTask = watchers.LatestFor(workspaceRoot).DispatchAsync(batch, CancellationToken.None);

			Assert.IsTrue(
				await client.WaitForMethodCountAsync("workspace/didChangeWatchedFiles", 1, TestPolling.DefaultTimeout).ConfigureAwait(false)
			);

			client.IsReady = false;

			Task<HoverInformation?> hoverTask = provider.GetHoverAsync(scriptFilePath, "local value = 1", new TextPosition(0, 0));

			await TestPolling
				.UntilAsync(() => client.StartCallCount >= 2, TestPolling.DefaultTimeout, "The hover must drive the next startup attempt.")
				.ConfigureAwait(false);

			Assert.AreEqual(2, client.StartCallCount);

			client.ReleaseWatchedFilesNotification();

			await dispatchTask.ConfigureAwait(false);
			Assert.IsNotNull(await hoverTask.ConfigureAwait(false));

			HoverInformation? followUpHover = await provider
				.GetHoverAsync(scriptFilePath, "local value = 2", new TextPosition(0, 0))
				.ConfigureAwait(false);

			Assert.IsNotNull(followUpHover);
			Assert.AreEqual(0, client.MarkConnectionUnhealthyCallCount);
			Assert.AreEqual(2, client.StartCallCount);
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task DispatchWorkspaceChangesAsync_RepeatedConnectionFailuresReplayEachBufferedBatchOnce()
	{
		string workspaceRoot = Path.Combine(Path.GetTempPath(), "BufferedWorkspaceRepeated_" + Guid.NewGuid().ToString("N"));
		string scriptFilePath = Path.Combine(workspaceRoot, "Scripts", "test.test");

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(scriptFilePath) ?? workspaceRoot);

			using var client = new FakeStatefulLanguageServerClient
			{
				HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
			};

			var watchers = new RecordingFileWatcherFactory { PassiveWatchersReportAMissingRoot = true };

			using LanguageServerFeatureProvider provider = TestProviders.CreateWatched([workspaceRoot], client, watchers);

			await provider.StartWorkspaceWatcherAsync(client, watchers, workspaceRoot);

			for (int i = 1; i <= 3; i++)
			{
				string changedFilePath = Path.Combine(workspaceRoot, "Scripts", $"generated{i}.test");

				var batch = new FileChangeBatch([new FileChange(changedFilePath, FileChangeKind.Changed)]);

				client.ThrowIOExceptionOnNextWatchedFilesNotification = true;

				await watchers.LatestFor(workspaceRoot).DispatchAsync(batch, CancellationToken.None);

				HoverInformation? hover = await provider.GetHoverAsync(scriptFilePath, $"local value = {i}", new TextPosition(0, 0));

				Assert.IsNotNull(hover);
				Assert.AreEqual(i, client.MarkConnectionUnhealthyCallCount);
				Assert.AreEqual(i * 2, client.GetSentMethodCount("workspace/didChangeWatchedFiles"));

				JsonElement replayPayload = client.GetLastNotificationParameters("workspace/didChangeWatchedFiles").GetProperty("changes");

				Assert.AreEqual(1, replayPayload.GetArrayLength());
				Assert.AreEqual(new Uri(changedFilePath).AbsoluteUri, replayPayload[0].GetProperty("uri").GetString());
			}
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(workspaceRoot);
		}
	}

	[TestMethod]
	public async Task DispatchWorkspaceChangesAsync_RefreshesConfigurationFromNonPrimaryRoot()
	{
		string primaryRoot = Path.Combine(Path.GetTempPath(), "MultiRootConfigPrimary_" + Guid.NewGuid().ToString("N"));
		string secondaryRoot = Path.Combine(Path.GetTempPath(), "MultiRootConfigSecondary_" + Guid.NewGuid().ToString("N"));
		string configurationFilePath = Path.Combine(secondaryRoot, ".testconfig");

		try
		{
			Directory.CreateDirectory(primaryRoot);
			Directory.CreateDirectory(secondaryRoot);

			using var client = new FakeStatefulLanguageServerClient();
			var watchers = new RecordingFileWatcherFactory { PassiveWatchersReportAMissingRoot = true };

			using LanguageServerFeatureProvider provider = TestProviders.CreateWatched([primaryRoot, secondaryRoot], client, watchers);

			await provider.StartWorkspaceWatcherAsync(client, watchers, primaryRoot);

			var batch = new FileChangeBatch([new FileChange(configurationFilePath, FileChangeKind.Changed)]);

			await watchers.LatestFor(secondaryRoot).DispatchAsync(batch, CancellationToken.None);

			Assert.AreSequenceEqual(["workspace/didChangeConfiguration", "workspace/didChangeWatchedFiles"], client.GetSentMethodNames());
		}
		finally
		{
			TemporaryDirectory.DeleteBestEffort(primaryRoot);
			TemporaryDirectory.DeleteBestEffort(secondaryRoot);
		}
	}

	public TestContext? TestContext { get; set; }
}
