using System.Text.Json;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers how a forwarded batch becomes LSP notifications, and how a connection failure is reported.</summary>
[TestClass]
public sealed class WorkspaceChangeSenderTests
{
	private const string WatchedFiles = "workspace/didChangeWatchedFiles";
	private const string Configuration = "workspace/didChangeConfiguration";

	private sealed class TestHost : IDisposable
	{
		private readonly object _syncRoot = new();
		private readonly List<long> _markedUnhealthy = [];

		internal TestHost(FakeLanguageServerClient? client = null, bool hasClient = true)
		{
			Client = hasClient ? client ?? new FakeLanguageServerClient { ConnectionGeneration = 4 } : null;

			var owner = new WorkspaceChangeOwnerContext(
				ClientAccessor: () => Client,
				Disposal,
				EnsureConnectionStartedAsync: static _ => Task.FromResult(true),

				MarkConnectionUnhealthy: generation =>
				{
					lock (_syncRoot)
						_markedUnhealthy.Add(generation);
				},

				RaiseWorkspaceFileWatchFailed: static _ => { }
			);

			Sender = new WorkspaceChangeSender(
				owner,

				isConfigurationPath: path =>
				{
					Interlocked.Increment(ref _configurationPathChecks);
					return IsConfigurationPath(path);
				},

				providerDisplayName: "Test",
				Logger
			);
		}

		private int _configurationPathChecks;

		internal FakeLanguageServerClient? Client { get; }

		internal ProviderLifetime Disposal { get; } = new();

		internal RecordingLogger Logger { get; } = new();

		internal WorkspaceChangeSender Sender { get; }

		internal Func<string, bool> IsConfigurationPath { get; set; } = static path => path.EndsWith("settings.json", StringComparison.Ordinal);

		internal int ConfigurationPathChecks => Volatile.Read(ref _configurationPathChecks);

		internal IReadOnlyList<long> MarkedUnhealthy
		{
			get
			{
				lock (_syncRoot)
					return [.. _markedUnhealthy];
			}
		}

		public void Dispose() => Logger.Dispose();
	}

	private static FileChange Change(string fileName, FileChangeKind kind = FileChangeKind.Changed) => new(TestPaths.Script(fileName), kind);

	[TestMethod]
	public async Task SendAsync_ForAWatchedFileBatch_SendsOneNotificationWithEveryChange()
	{
		using var fixture = new TestHost();

		await fixture.Sender.SendAsync(
			[Change("a.lua", FileChangeKind.Created), Change("b.lua"), Change("c.lua", FileChangeKind.Deleted)],
			CancellationToken.None
		);

		Assert.AreSequenceEqual([WatchedFiles], fixture.Client!.GetSentMethodNames());

		JsonElement changes = fixture.Client.GetLastNotificationParameters(WatchedFiles).GetProperty("changes");

		Assert.AreEqual(3, changes.GetArrayLength());
		Assert.AreEqual(DocumentUriConversion.CreateFileUri(TestPaths.Script("a.lua")), changes[0].GetProperty("uri").GetString());
		Assert.AreEqual((int)FileChangeType.Created, changes[0].GetProperty("type").GetInt32());
		Assert.AreEqual((int)FileChangeType.Changed, changes[1].GetProperty("type").GetInt32());
		Assert.AreEqual((int)FileChangeType.Deleted, changes[2].GetProperty("type").GetInt32());
	}

	[TestMethod]
	public async Task SendAsync_WhenConfigurationPathsChanged_PushesTheSettingsOnceBeforeTheWatchedFiles()
	{
		using var fixture = new TestHost();

		await fixture.Sender.SendAsync(
			[Change("a.lua"), Change("settings.json"), Change("other-settings.json"), Change("b.lua")],
			CancellationToken.None
		);

		Assert.AreSequenceEqual([Configuration, WatchedFiles], fixture.Client!.GetSentMethodNames());
	}

	[TestMethod]
	public async Task SendAsync_ChecksConfigurationPathsOnlyUntilOneIsFound()
	{
		using var fixture = new TestHost();

		await fixture.Sender.SendAsync([Change("a.lua"), Change("settings.json"), Change("b.lua"), Change("c.lua")], CancellationToken.None);

		// The hook ran for the first two paths, then the refresh was settled.
		Assert.AreEqual(2, fixture.ConfigurationPathChecks);
	}

	[TestMethod]
	public async Task SendAsync_WhenNoPathIsAConfigurationPath_SendsNoSettingsRefresh()
	{
		using var fixture = new TestHost();

		await fixture.Sender.SendAsync([Change("a.lua"), Change("b.lua")], CancellationToken.None);

		Assert.AreSequenceEqual([WatchedFiles], fixture.Client!.GetSentMethodNames());
		Assert.AreEqual(2, fixture.ConfigurationPathChecks);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheConfigurationPathHookThrows_StillSendsTheWatchedFilesAndLogsTheFault()
	{
		using var fixture = new TestHost();

		fixture.IsConfigurationPath = static _ => throw new InvalidOperationException("hook failure");

		await fixture.Sender.SendAsync([Change("settings.json")], CancellationToken.None);

		Assert.AreSequenceEqual([WatchedFiles], fixture.Client!.GetSentMethodNames());
		Assert.Contains(static log => log.Contains("configuration-path hook failed", StringComparison.Ordinal), fixture.Logger.Logs);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheSettingsPushFailsOnTheConnection_MarksTheObservedGenerationUnhealthyAndSendsNoWatchedFiles()
	{
		using var fixture = new TestHost();

		fixture.Client!.SendNotificationHandler = static (method, _, _) =>
			method == Configuration ? Task.FromException(new IOException("The connection closed.")) : Task.CompletedTask;

		await Assert.ThrowsExactlyAsync<IOException>(() => fixture.Sender.SendAsync([Change("settings.json")], CancellationToken.None));

		Assert.AreSequenceEqual([4L], fixture.MarkedUnhealthy);
		Assert.IsEmpty(fixture.Client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task SendAsync_WithoutAClient_AfterDisposalStartedOrForNoChanges_SendsNothing()
	{
		using var noClient = new TestHost(hasClient: false);

		await noClient.Sender.SendAsync([Change("a.lua")], CancellationToken.None);

		using var disposed = new TestHost();

		disposed.Disposal.Signal();

		await disposed.Sender.SendAsync([Change("a.lua")], CancellationToken.None);

		using var empty = new TestHost();

		await empty.Sender.SendAsync([], CancellationToken.None);

		Assert.IsEmpty(disposed.Client!.GetSentMethodNames());
		Assert.IsEmpty(empty.Client!.GetSentMethodNames());
	}

	[TestMethod]
	public async Task SendAsync_WhenTheConnectionFails_MarksTheObservedGenerationUnhealthyAndRethrows()
	{
		using var client = new FakeLanguageServerClient
		{
			ConnectionGeneration = 7,
			SendNotificationHandler = static (_, _, _) => Task.FromException(new IOException("The connection closed.")),
		};

		using var fixture = new TestHost(client);

		await Assert.ThrowsExactlyAsync<IOException>(() => fixture.Sender.SendAsync([Change("a.lua")], CancellationToken.None));

		Assert.AreSequenceEqual([7L], fixture.MarkedUnhealthy);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheConnectionRestartsDuringAFailingSend_MarksTheGenerationObservedBeforeTheSend()
	{
		using var fixture = new TestHost();

		fixture.Client!.SendNotificationHandler = (_, _, _) =>
		{
			// A restart completes while the send fails, so the client already reports the next generation.
			fixture.Client.ConnectionGeneration = 5;
			return Task.FromException(new IOException("The connection closed."));
		};

		await Assert.ThrowsExactlyAsync<IOException>(() => fixture.Sender.SendAsync([Change("a.lua")], CancellationToken.None));

		Assert.AreSequenceEqual([4L], fixture.MarkedUnhealthy);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheCallerCancels_DoesNotMarkTheConnectionUnhealthy()
	{
		using var cancellation = new CancellationTokenSource();
		using var fixture = new TestHost();

		fixture.Client!.SendNotificationHandler = (_, _, token) =>
		{
			cancellation.Cancel();
			token.ThrowIfCancellationRequested();

			return Task.CompletedTask;
		};

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Sender.SendAsync([Change("a.lua")], cancellation.Token));

		Assert.IsEmpty(fixture.MarkedUnhealthy);
	}

	[TestMethod]
	public async Task SendAsync_WhenAnUnownedCancellationAbortsTheSend_MarksTheConnectionUnhealthy()
	{
		using var fixture = new TestHost();

		fixture.Client!.SendNotificationHandler = static (_, _, _) => Task.FromException(new OperationCanceledException());

		await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Sender.SendAsync([Change("a.lua")], CancellationToken.None));

		Assert.AreSequenceEqual([4L], fixture.MarkedUnhealthy);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheClientIsDisposedOutsideProviderLifetime_MarksTheConnectionUnhealthy()
	{
		using var fixture = new TestHost();

		fixture.Client!.SendNotificationHandler = static (_, _, _) => Task.FromException(new ObjectDisposedException("client"));

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => fixture.Sender.SendAsync([Change("a.lua")], CancellationToken.None));

		Assert.AreSequenceEqual([4L], fixture.MarkedUnhealthy);
	}

	[TestMethod]
	public async Task SendAsync_WhenTheClientIsDisposedDuringProviderLifetime_DoesNotMarkTheConnectionUnhealthy()
	{
		using var fixture = new TestHost();

		fixture.Client!.SendNotificationHandler = (_, _, _) =>
		{
			fixture.Disposal.Signal();
			return Task.FromException(new ObjectDisposedException("client"));
		};

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => fixture.Sender.SendAsync([Change("a.lua")], CancellationToken.None));

		Assert.IsEmpty(fixture.MarkedUnhealthy);
	}
}
