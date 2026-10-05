using System.Text.Json;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using static Nickelony.LanguageServer.Provider.Tests.DiagnosticsTestHelpers;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the diagnostics payloads the client publishes: version fencing, the cache and the update event.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderDiagnosticsEventTests
{
	[TestMethod]
	public async Task PublishDiagnostics_EmptyList_ClearsTheCachedDiagnosticsAndRaisesAnEmptyUpdate()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);
		var publishedDiagnostics = new TaskCompletionSource<IReadOnlyList<Diagnostic>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		provider.DiagnosticsUpdated += (_, eventArgs) =>
		{
			if (LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(eventArgs.DocumentId, filePath))
				publishedDiagnostics.TrySetResult(eventArgs.Diagnostics);
		};

		client.PublishDiagnostics(CreateDiagnostics(filePath, version: 1, startCharacter: 6, endCharacter: 11, message: "Current warning."));

		Task firstCompletedTask = await Task.WhenAny(publishedDiagnostics.Task, Task.Delay(TestPolling.DefaultTimeout, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreSame(publishedDiagnostics.Task, firstCompletedTask);
		Assert.HasCount(1, provider.GetDiagnostics(filePath));

		// A server that fixed the problem publishes an empty list; the cache clears and the empty update
		// is announced so consumers can drop the squiggles.
		var clearedDiagnostics = new TaskCompletionSource<IReadOnlyList<Diagnostic>>(TaskCreationOptions.RunContinuationsAsynchronously);
		provider.DiagnosticsUpdated += (_, eventArgs) => clearedDiagnostics.TrySetResult(eventArgs.Diagnostics);

		client.PublishDiagnostics(new Lsp.PublishDiagnosticsParams(new Uri(filePath).AbsoluteUri, 1, []));

		Task clearedCompletedTask = await Task.WhenAny(clearedDiagnostics.Task, Task.Delay(TestPolling.DefaultTimeout, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreSame(clearedDiagnostics.Task, clearedCompletedTask);
		Assert.IsEmpty(await clearedDiagnostics.Task.ConfigureAwait(false));
		Assert.IsEmpty(provider.GetDiagnostics(filePath));
	}

	[TestMethod]
	public async Task DiagnosticsPublished_IgnoresVersionMismatchAndStoresMatchingVersion()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string firstContent = "local value = 1";
		const string secondContent = "local second = 2";

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		int diagnosticsUpdatedCount = 0;
		provider.DiagnosticsUpdated += (_, _) => diagnosticsUpdatedCount++;

		await provider.GetHoverAsync(
			filePath,
			documentText: firstContent,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		await provider.GetHoverAsync(
			filePath,
			documentText: secondContent,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		client.PublishDiagnostics(CreateDiagnostics(filePath, version: 1, startCharacter: 6, endCharacter: 12, message: "Stale warning."));

		Assert.AreEqual(0, diagnosticsUpdatedCount);
		Assert.IsEmpty(provider.GetDiagnostics(filePath));

		client.PublishDiagnostics(CreateDiagnostics(filePath, version: 3, startCharacter: 6, endCharacter: 12, message: "Future warning."));

		Assert.AreEqual(0, diagnosticsUpdatedCount);
		Assert.IsEmpty(provider.GetDiagnostics(filePath));

		client.PublishDiagnostics(CreateDiagnostics(filePath, version: 2, startCharacter: 6, endCharacter: 12, message: "Current warning."));

		IReadOnlyList<Diagnostic> diagnostics = provider.GetDiagnostics(filePath);

		Assert.AreEqual(1, diagnosticsUpdatedCount);
		Assert.HasCount(1, diagnostics);
		Assert.AreEqual(6, diagnostics[0].Range.Offset);
		Assert.AreEqual(12, diagnostics[0].Range.EndOffset);
	}

	[TestMethod]
	public async Task DiagnosticsPublished_WithoutVersion_StoresFallbackDiagnosticsForTrackedDocument()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		int diagnosticsUpdatedCount = 0;

		provider.DiagnosticsUpdated += (_, _) => diagnosticsUpdatedCount++;

		await provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		client.PublishDiagnostics(CreateDiagnostics(filePath, version: null, startCharacter: 6, endCharacter: 11, message: "Fallback warning."));

		IReadOnlyList<Diagnostic> diagnostics = provider.GetDiagnostics(filePath);

		Assert.AreEqual(1, diagnosticsUpdatedCount);
		Assert.HasCount(1, diagnostics);
		Assert.AreEqual(DiagnosticSeverity.Warning, diagnostics[0].Severity);
		Assert.AreEqual(6, diagnostics[0].Range.Offset);
		Assert.AreEqual(11, diagnostics[0].Range.EndOffset);
	}

	[TestMethod]
	public async Task DiagnosticsPublished_OneSubscriberExceptionDoesNotSuppressLaterSubscribers()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		int notifiedSubscribers = 0;

		provider.DiagnosticsUpdated += (_, _) =>
		{
			notifiedSubscribers++;
			throw new InvalidOperationException("Simulated diagnostics subscriber failure.");
		};

		provider.DiagnosticsUpdated += (_, _) => notifiedSubscribers++;

		await provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		client.PublishDiagnostics(CreateDiagnostics(filePath, version: 1, startCharacter: 6, endCharacter: 12, message: "Current warning."));

		Assert.AreEqual(2, notifiedSubscribers);
	}

	[TestMethod]
	public async Task DiagnosticsPublished_DisposeInFirstSubscriber_DoesNotNotifyLaterSubscribers()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		int firstSubscriberCalls = 0;
		int secondSubscriberCalls = 0;

		provider.DiagnosticsUpdated += (_, _) =>
		{
			firstSubscriberCalls++;
			provider.Dispose();
		};

		provider.DiagnosticsUpdated += (_, _) => secondSubscriberCalls++;

		await provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		client.PublishDiagnostics(CreateDiagnostics(filePath, version: 1, startCharacter: 6, endCharacter: 12, message: "Current warning."));

		Assert.AreEqual(1, firstSubscriberCalls);
		Assert.AreEqual(0, secondSubscriberCalls);
	}

	[TestMethod]
	public async Task DiagnosticsPublished_DisposeWhileSubscriberIsRunning_DoesNotAdmitLaterSubscribers()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient();

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		var firstSubscriberEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseFirstSubscriber = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		int secondSubscriberCalls = 0;

		provider.DiagnosticsUpdated += (_, _) =>
		{
			firstSubscriberEntered.TrySetResult(true);
			releaseFirstSubscriber.Task.GetAwaiter().GetResult();
		};

		provider.DiagnosticsUpdated += (_, _) => secondSubscriberCalls++;

		await provider.GetHoverAsync(
			filePath,
			documentText: content,
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		var publishTask = Task.Run(
			() =>
				client.PublishDiagnostics(CreateDiagnostics(filePath, version: 1, startCharacter: 6, endCharacter: 12, message: "Current warning.")),

			TestContext.CancellationToken
		);

		Task enteredTask = await Task.WhenAny(firstSubscriberEntered.Task, Task.Delay(TestPolling.DefaultTimeout, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreSame(firstSubscriberEntered.Task, enteredTask);

		provider.Dispose();
		releaseFirstSubscriber.TrySetResult(true);
		await publishTask.ConfigureAwait(false);

		Assert.AreEqual(0, secondSubscriberCalls);
	}

	[TestMethod]
	public async Task DiagnosticsPublished_ForUntrackedDocument_IsIgnored()
	{
		string workspaceRoot = TestPaths.Root;
		string trackedFilePath = TestPaths.Script("tracked.test");
		string untrackedFilePath = TestPaths.Script("untracked.test");

		using var client = new FakeStatefulLanguageServerClient();

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		var updates = new List<(string FilePath, int Count)>();
		var trackedUpdate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.DiagnosticsUpdated += (_, eventArgs) =>
		{
			lock (updates)
				updates.Add((eventArgs.DocumentId, eventArgs.Diagnostics.Count));

			if (LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(eventArgs.DocumentId, trackedFilePath))
				trackedUpdate.TrySetResult(true);
		};

		await provider.GetHoverAsync(
			filePath: trackedFilePath,
			documentText: "local value = 1",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		client.PublishDiagnostics(
			CreateDiagnostics(filePath: untrackedFilePath, version: 1, startCharacter: 0, endCharacter: 1, message: "Untracked warning.")
		);

		client.PublishDiagnostics(
			CreateDiagnostics(filePath: trackedFilePath, version: 1, startCharacter: 6, endCharacter: 11, message: "Tracked warning.")
		);

		Task completedTask = await Task.WhenAny(trackedUpdate.Task, Task.Delay(TestPolling.DefaultTimeout, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreSame(trackedUpdate.Task, completedTask);

		lock (updates)
			Assert.AreSequenceEqual([trackedFilePath], [.. updates.Select(update => update.FilePath)]);
	}

	public TestContext? TestContext { get; set; }
}
