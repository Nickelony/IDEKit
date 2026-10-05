using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Rename;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the document lifecycle the provider mirrors to the server: open, update, close and the idle-document trim.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderDocumentSynchronizationTests
{
	[TestMethod]
	public async Task UpdateDocument_SendsFullTextChangeWhenServerAdvertisesFullSync()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { TextDocumentSyncKind = TextDocumentSyncKind.Full };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(filePath, "local value = 1");
		provider.UpdateDocument(filePath, "local value = 2");
		Assert.IsTrue(await client.WaitForNotificationAsync("textDocument/didChange", TestPolling.DefaultTimeout));

		JsonElement parameters = client.GetLastNotificationParameters("textDocument/didChange");
		JsonElement change = parameters.GetProperty("contentChanges")[0];

		Assert.AreEqual("local value = 2", change.GetProperty("text").GetString());
		Assert.IsFalse(change.TryGetProperty("range", out _));
	}

	[TestMethod]
	public async Task UpdateDocument_SendsIncrementalChangeRangeWhenServerAdvertisesIncrementalSync()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { TextDocumentSyncKind = TextDocumentSyncKind.Incremental };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(filePath, "local value = 1");
		provider.UpdateDocument(filePath, "local value = 2");
		Assert.IsTrue(await client.WaitForNotificationAsync("textDocument/didChange", TestPolling.DefaultTimeout));

		JsonElement parameters = client.GetLastNotificationParameters("textDocument/didChange");
		JsonElement change = parameters.GetProperty("contentChanges")[0];

		// The change carries the replaced span as a protocol range plus the replacement text, so the
		// server can apply an incremental edit instead of resynchronizing the whole document.
		Assert.AreEqual("2", change.GetProperty("text").GetString());

		JsonElement range = change.GetProperty("range");

		Assert.AreEqual(0, range.GetProperty("start").GetProperty("line").GetInt32());
		Assert.AreEqual(14, range.GetProperty("start").GetProperty("character").GetInt32());
		Assert.AreEqual(0, range.GetProperty("end").GetProperty("line").GetInt32());
		Assert.AreEqual(15, range.GetProperty("end").GetProperty("character").GetInt32());
	}

	[TestMethod]
	public async Task UpdateDocument_WithUnchangedContent_DoesNotSendDidChange()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		provider.UpdateDocument(filePath, content);
		Assert.IsFalse(await client.WaitForNotificationAsync("textDocument/didChange", TimeSpan.FromMilliseconds(250)));

		Assert.AreSequenceEqual(["textDocument/didOpen"], client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task UpdateDocument_CoalescesSupersededQueuedChanges()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { TextDocumentSyncKind = TextDocumentSyncKind.Full };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(filePath, "local value = 1");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.BlockNextChangeNotification();

		provider.UpdateDocument(filePath, "local value = 2");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, TestPolling.DefaultTimeout));

		provider.UpdateDocument(filePath, "local value = 3");
		provider.UpdateDocument(filePath, "local value = 4");

		client.ReleaseChangeNotification();

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 2, TestPolling.DefaultTimeout));
		Assert.IsFalse(await client.WaitForMethodCountAsync("textDocument/didChange", 3, TimeSpan.FromMilliseconds(250)));

		JsonElement parameters = client.GetLastNotificationParameters("textDocument/didChange");
		JsonElement change = parameters.GetProperty("contentChanges")[0];

		Assert.AreEqual("local value = 4", change.GetProperty("text").GetString());

		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/didChange", "textDocument/didChange"], client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task UpdateDocument_BlockedChangeOnOneFile_DoesNotStallOtherFileHover()
	{
		string workspaceRoot = TestPaths.Root;
		string firstFilePath = TestPaths.Script("first.test");
		string secondFilePath = TestPaths.Script("second.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(firstFilePath, "local first = 1");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.BlockNextChangeNotification();

		provider.UpdateDocument(firstFilePath, "local first = 2");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, TestPolling.DefaultTimeout));

		Task<HoverInformation?> hoverTask = provider.GetHoverAsync(
			filePath: secondFilePath,
			documentText: "local second = 1",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Task completedTask = await Task.WhenAny(hoverTask, Task.Delay(TestPolling.DefaultTimeout, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreSame(hoverTask, completedTask);
		Assert.IsNotNull(await hoverTask.ConfigureAwait(false));
		Assert.AreEqual(2, client.GetSentMethodCount("textDocument/didOpen"));
		Assert.AreEqual(1, client.GetSentMethodCount("textDocument/hover"));

		client.ReleaseChangeNotification();

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, TestPolling.DefaultTimeout));
	}

	[TestMethod]
	public async Task UpdateDocument_WaitsForEarlierOpenNotificationToFinish()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		client.BlockNextOpenNotification();

		provider.OpenDocument(filePath, "local value = 1");
		provider.UpdateDocument(filePath, "local value = 2");

		Assert.IsFalse(await client.WaitForNotificationAsync("textDocument/didChange", TimeSpan.FromMilliseconds(250)));

		client.ReleaseOpenNotification();

		Assert.IsTrue(await client.WaitForNotificationAsync("textDocument/didChange", TestPolling.DefaultTimeout));

		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/didChange"], client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task UpdateDocument_DisposeBeforeQueuedLatestUpdateRuns_DoesNotSendLateDidChange()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		client.BlockNextOpenNotification();

		provider.OpenDocument(filePath, "local value = 1");
		provider.UpdateDocument(filePath, "local value = 2");

		Assert.IsFalse(await client.WaitForNotificationAsync("textDocument/didChange", TimeSpan.FromMilliseconds(250)).ConfigureAwait(false));

		provider.Dispose();
		client.ReleaseOpenNotification();

		Assert.IsFalse(await client.WaitForNotificationAsync("textDocument/didChange", TimeSpan.FromMilliseconds(250)).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task CloseDocument_WaitsForQueuedOpenNotificationToFinish()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		client.BlockNextOpenNotification();

		provider.OpenDocument(filePath, "local value = 1");
		provider.CloseDocument(filePath);

		Assert.IsFalse(await client.WaitForNotificationAsync("textDocument/didClose", TimeSpan.FromMilliseconds(250)));

		client.ReleaseOpenNotification();

		Assert.IsTrue(await client.WaitForNotificationAsync("textDocument/didClose", TestPolling.DefaultTimeout));

		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/didClose"], client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task CloseDocument_SendsDidClosePayloadWithDocumentUri()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(filePath, "local value = 1");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		provider.CloseDocument(filePath);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TestPolling.DefaultTimeout));

		JsonElement parameters = client.GetLastNotificationParameters("textDocument/didClose");
		Assert.AreEqual(new Uri(filePath).AbsoluteUri, parameters.GetProperty("textDocument").GetProperty("uri").GetString());
	}

	[TestMethod]
	public async Task UpdateDocument_WithNoneTextDocumentSyncKind_LogsAndSkipsTheChange()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var logger = new RecordingLogger(LogLevel.Warning);

		using var client = new FakeStatefulLanguageServerClient
		{
			// None is the value a server reports before initialization completes: the provider must not
			// send a change it cannot express and must contain the fault through the shared observer.
			TextDocumentSyncKind = TextDocumentSyncKind.None,
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create(
			[workspaceRoot],
			client,
			logger: logger.CreateLogger<LanguageServerFeatureProvider>()
		);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		provider.UpdateDocument(filePath, content + "\nprint(value)");

		// The contained background fault is observable as a recorded warning. The poll runs against the
		// shared timeout budget instead of a fixed 1-second loop, so a loaded machine cannot turn the
		// recorded-warning observation into a spurious failure.
		await TestPolling
			.WaitForAsync(
				action: () => Task.FromResult(logger.Logs.Count),
				isSatisfied: static count => count > 0,
				timeout: TestPolling.DefaultTimeout,
				failureMessage: "Expected the contained background fault to be logged.",
				describeLastResult: static count => $"recordedWarnings={count}"
			)
			.ConfigureAwait(false);

		Assert.AreEqual(0, client.GetSentMethodCount("textDocument/didChange"));
	}

	[TestMethod]
	public async Task GetHoverAsync_IdleDocuments_DoNotAccumulateAcrossDistinctFiles()
	{
		string workspaceRoot = TestPaths.Root;

		using var client = new FakeStatefulLanguageServerClient
		{
			HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		for (int i = 0; i < 20; i++)
		{
			string filePath = TestPaths.Script($"hover_{i}.test");

			HoverInformation? hover = await provider.GetHoverAsync(
				filePath,
				documentText: "local value = 1",
				position: new TextPosition(0, 0),
				cancellationToken: TestContext.CancellationToken
			);

			Assert.IsNotNull(hover);
		}

		// Pins the observable policy: accumulation is bounded, the oldest idle documents are evicted, and the newest
		// is retained.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TestPolling.DefaultTimeout));

		int trackedCount = client.GetSentMethodCount("textDocument/didOpen") - client.GetSentMethodCount("textDocument/didClose");

		Assert.IsLessThan(20, trackedCount, "Idle documents must not accumulate one per requested file.");

		int didOpenCountBeforeReopen = client.GetSentMethodCount("textDocument/didOpen");
		int didCloseCountBeforeReopen = client.GetSentMethodCount("textDocument/didClose");

		Assert.IsNotNull(
			await provider.GetHoverAsync(
				filePath: TestPaths.Script("hover_19.test"),
				documentText: "local value = 1",
				position: new TextPosition(0, 0),
				cancellationToken: TestContext.CancellationToken
			)
		);

		Assert.AreEqual(
			didOpenCountBeforeReopen,
			client.GetSentMethodCount("textDocument/didOpen"),
			"The most recently used idle document must still be tracked."
		);

		Assert.AreEqual(didCloseCountBeforeReopen, client.GetSentMethodCount("textDocument/didClose"));

		Assert.IsNotNull(
			await provider.GetHoverAsync(
				filePath: TestPaths.Script("hover_0.test"),
				documentText: "local value = 1",
				position: new TextPosition(0, 0),
				cancellationToken: TestContext.CancellationToken
			)
		);

		Assert.AreEqual(
			didOpenCountBeforeReopen + 1,
			client.GetSentMethodCount("textDocument/didOpen"),
			"The oldest idle document must have been evicted."
		);

		Assert.IsGreaterThan(
			didCloseCountBeforeReopen,
			client.GetSentMethodCount("textDocument/didClose"),
			"Evicting the oldest idle document must close its server copy."
		);
	}

	[TestMethod]
	public async Task GetHoverAsync_IdleDocument_ReopensLazilyOnNextRequest()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("hover.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			HoverResponse = JsonSerializer.SerializeToElement(new { contents = new { kind = "markdown", value = "Hover docs." } }),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		Assert.IsNotNull(
			await provider.GetHoverAsync(
				filePath,
				documentText: "local value = 1",
				position: new TextPosition(0, 0),
				cancellationToken: TestContext.CancellationToken
			)
		);

		for (int i = 0; i < 20; i++)
		{
			string otherFilePath = TestPaths.Script($"other_{i}.test");

			Assert.IsNotNull(
				await provider.GetHoverAsync(
					filePath: otherFilePath,
					documentText: "local value = 1",
					position: new TextPosition(0, 0),
					cancellationToken: TestContext.CancellationToken
				)
			);
		}

		int didOpenCountBeforeReopen = client.GetSentMethodCount("textDocument/didOpen");
		int didCloseCountBeforeReopen = client.GetSentMethodCount("textDocument/didClose");

		Assert.IsNotNull(
			await provider.GetHoverAsync(
				filePath,
				documentText: "local value = 1",
				position: new TextPosition(0, 0),
				cancellationToken: TestContext.CancellationToken
			)
		);

		Assert.AreEqual(didOpenCountBeforeReopen + 1, client.GetSentMethodCount("textDocument/didOpen"));
		Assert.AreEqual(didCloseCountBeforeReopen + 1, client.GetSentMethodCount("textDocument/didClose"));
	}

	public TestContext? TestContext { get; set; }
}
