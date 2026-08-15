using Nickelony.IDEKit.IntelliSense.Hover;
using System.Text.Json;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers the request pipeline: capability-gate order, positional clamping, idle-document trimming,
/// disposal guards on document members, and failure containment on the best-effort close sends.
/// </summary>
[TestClass]
public sealed class LanguageServerIntelliSenseProviderBaseRequestPipelineTests
{
	private const string Content = "line one";
	private const string UpdatedContent = "line two";

	private static readonly string s_workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-requests-" + Guid.NewGuid().ToString("N"));
	private static readonly string s_filePath = Path.Combine(s_workspaceRoot, "Scripts", "test.test");
	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(2);

	[TestMethod]
	public async Task UnsupportedCapability_IsGatedBeforeSynchronization()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		Assert.IsNull(await provider.SendGatedRequestAsync(s_filePath, Content, static _ => false).ConfigureAwait(false));

		// The document is never synchronized for a request the connected client cannot answer.
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public async Task SupportedCapability_SynchronizesThenSendsTheRequest()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		Assert.IsNull(await provider.SendGatedRequestAsync(s_filePath, Content, static _ => true).ConfigureAwait(false));

		CollectionAssert.AreEqual(
			new[] { "textDocument/didOpen", "test/gatedRequest" },
			client.GetSentMethodNames());

		Assert.AreEqual(new Uri(s_filePath).AbsoluteUri, client.GetLastRequestParameters("test/gatedRequest")
			.GetProperty("uri").GetString());
	}

	[TestMethod]
	public async Task NegativePosition_IsClampedToZero()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		await provider.GetHoverAtPositionAsync(s_filePath, Content, new TextPosition(-5, -9)).ConfigureAwait(false);

		Assert.AreEqual(new ProtocolPosition(0, 0), provider.LastRequestedPosition);
	}

	[TestMethod]
	public async Task IdleDocuments_AreTrimmedBeyondTheConfiguredCap()
	{
		string firstFilePath = Path.Combine(s_workspaceRoot, "a.test");
		string secondFilePath = Path.Combine(s_workspaceRoot, "b.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { MaxTrackedIdleDocuments = 1 });

		await provider.GetHoverAsync(firstFilePath, Content, new TextPosition(0, 0)).ConfigureAwait(false);
		await provider.GetHoverAsync(secondFilePath, "line two", new TextPosition(0, 0)).ConfigureAwait(false);

		// The second request pushes the first idle document beyond the cap; it is trimmed and
		// closed on the server.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(new Uri(firstFilePath).AbsoluteUri, client.GetLastNotificationParameters("textDocument/didClose")
			.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => provider.InvalidatedPaths.Contains(firstFilePath), s_waitTimeout).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task DisposedProvider_UpdateAndRenameAreNoOps()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.Dispose();
		provider.UpdateDocument(s_filePath, Content);
		provider.MoveDocument(s_filePath, Path.Combine(s_workspaceRoot, "renamed.test"), Content);

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public async Task TrimSend_WhenTransportIsDisposed_KeepsTheDocumentedFallback()
	{
		string firstFilePath = Path.Combine(s_workspaceRoot, "a.test");
		string secondFilePath = Path.Combine(s_workspaceRoot, "b.test");

		using var client = new FakeLanguageServerClient { IsReady = false, ThrowObjectDisposedOnDidClose = true };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { MaxTrackedIdleDocuments = 0 });

		// With a zero cap every request-only document is trimmed on release; the simulated disposal race on
		// the didClose send must be swallowed and both requests must still return their fallback value.
		Assert.IsNull(await provider.GetHoverAsync(firstFilePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));
		Assert.IsNull(await provider.GetHoverAsync(secondFilePath, "line two", new TextPosition(0, 0)).ConfigureAwait(false));

		// The race is only covered when both trimmed closes actually reached the send that simulates disposal.
		Assert.IsTrue(await TestPolling.ForConditionAsync(
			() => client.SimulatedDidCloseDisposalRaceCount == 2,
			s_waitTimeout).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task MoveDocument_CaseOnlyRename_IsANoOpOnCaseInsensitivePlatforms()
	{
		if (LanguageServerPaths.UsesCaseSensitiveLocalPaths)
			Assert.Inconclusive("Case-only renames are real renames under case-sensitive path identity.");

		string caseVariantFilePath = Path.Combine(s_workspaceRoot, "Scripts", "TEST.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		provider.MoveDocument(s_filePath, caseVariantFilePath, Content);

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.AreEqual(1, client.GetSentMethodCount("textDocument/didOpen"));
		Assert.AreEqual(0, client.GetSentMethodCount("textDocument/didClose"));
		Assert.AreEqual(0, provider.MovedPaths.Count);
	}

	[TestMethod]
	public async Task GatedRequest_WhileAnotherRequestHoldsTheReference_DoesNotReleaseIt()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { MaxTrackedIdleDocuments = 0 });

		var requestGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		client.SendRequestHandler = async (method, _, cancellationToken) =>
		{
			if (string.Equals(method, "test/hover", StringComparison.Ordinal))
				await requestGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

			return null;
		};

		Task<TextHoverInfo?> hoverTask = provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0));

		Assert.IsTrue(await client.WaitForMethodCountAsync("test/hover", 1, s_waitTimeout).ConfigureAwait(false));

		// A request that leaves through the capability gate never acquired a reference; releasing it would
		// consume the in-flight request's reference and trim the document under that request.
		Assert.IsNull(await provider.SendGatedRequestAsync(s_filePath, Content, static _ => false).ConfigureAwait(false));

		Assert.AreEqual(0, client.GetSentMethodCount("textDocument/didClose"));
		Assert.AreEqual(0, provider.InvalidatedPaths.Count);

		requestGate.TrySetResult(true);
		Assert.IsNull(await hoverTask.ConfigureAwait(false));

		// Releasing the in-flight request now trims the idle document under the zero cap.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, s_waitTimeout).ConfigureAwait(false));
		Assert.IsTrue(provider.InvalidatedPaths.Contains(s_filePath));
	}

	[TestMethod]
	public async Task CloseDocument_WhileRequestReferenceIsActive_CompletesTheDeferredCloseOnRelease()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		var requestGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		client.SendRequestHandler = async (method, _, cancellationToken) =>
		{
			if (string.Equals(method, "test/hover", StringComparison.Ordinal))
				await requestGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

			return null;
		};

		Task<TextHoverInfo?> hoverTask = provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0));

		Assert.IsTrue(await client.WaitForMethodCountAsync("test/hover", 1, s_waitTimeout).ConfigureAwait(false));

		provider.CloseDocument(s_filePath);

		// The close must wait for the temporary request reference; nothing is closed while the request is in flight.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.AreEqual(0, client.GetSentMethodCount("textDocument/didClose"));

		requestGate.TrySetResult(true);
		Assert.IsNull(await hoverTask.ConfigureAwait(false));

		// The deferred close completes when the request reference drains.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(new Uri(s_filePath).AbsoluteUri, client.GetLastNotificationParameters("textDocument/didClose")
			.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.IsTrue(provider.InvalidatedPaths.Contains(s_filePath));
	}

	[TestMethod]
	public async Task CloseDocument_ThenOpenAgainWhileRequestReferenceIsActive_CancelsTheDeferredClose()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		var requestGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		client.SendRequestHandler = async (method, _, cancellationToken) =>
		{
			if (string.Equals(method, "test/hover", StringComparison.Ordinal))
				await requestGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

			return null;
		};

		Task<TextHoverInfo?> hoverTask = provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0));

		Assert.IsTrue(await client.WaitForMethodCountAsync("test/hover", 1, s_waitTimeout).ConfigureAwait(false));

		// The close is deferred because the temporary request reference still keeps the document tracked.
		provider.CloseDocument(s_filePath);
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);
		Assert.AreEqual(0, client.GetSentMethodCount("textDocument/didClose"));

		// Reopening the document before the request drains cancels the deferred close: the host holds the document
		// open again, so the release must not close the server copy. The server copy never closed, so the reopen
		// itself requires no second didOpen.
		provider.OpenDocument(s_filePath, Content);

		requestGate.TrySetResult(true);
		Assert.IsNull(await hoverTask.ConfigureAwait(false));

		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.AreEqual(0, client.GetSentMethodCount("textDocument/didClose"));
		Assert.AreEqual(1, client.GetSentMethodCount("textDocument/didOpen"));
		Assert.IsNotNull(provider.GetTrackedSnapshot(s_filePath), "The reopened document must stay tracked.");

		// The document is still open on the server, so an explicit close now closes it exactly once.
		provider.CloseDocument(s_filePath);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, s_waitTimeout).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task MoveDocument_WhileRequestReferenceIsActive_TransfersTheDeferredCloseToTheNewPath()
	{
		string renamedFilePath = Path.Combine(s_workspaceRoot, "Scripts", "renamed.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		var requestGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		client.SendRequestHandler = async (method, _, cancellationToken) =>
		{
			if (string.Equals(method, "test/hover", StringComparison.Ordinal))
				await requestGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

			return null;
		};

		Task<TextHoverInfo?> hoverTask = provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0));

		Assert.IsTrue(await client.WaitForMethodCountAsync("test/hover", 1, s_waitTimeout).ConfigureAwait(false));

		provider.CloseDocument(s_filePath);
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);
		Assert.AreEqual(0, client.GetSentMethodCount("textDocument/didClose"));

		// The move rekeys the still-tracked record while the request reference is active, so the pending close has
		// to follow the record to its new path.
		provider.MoveDocument(s_filePath, renamedFilePath, Content);

		requestGate.TrySetResult(true);
		Assert.IsNull(await hoverTask.ConfigureAwait(false));

		// The release completes the transferred close for the new identity, not the vacated old path.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(new Uri(renamedFilePath).AbsoluteUri, client.GetLastNotificationParameters("textDocument/didClose")
			.GetProperty("textDocument").GetProperty("uri").GetString());
	}

	[TestMethod]
	public async Task CanceledRunningUpdate_ReopensTheDocumentOnTheNextSynchronization()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		client.SendNotificationHandler = async (method, _, cancellationToken) =>
		{
			if (string.Equals(method, "textDocument/didChange", StringComparison.Ordinal))
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
		};

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		provider.UpdateDocument(s_filePath, UpdatedContent);

		Assert.IsTrue(await TestPolling.ForConditionAsync(
			() => client.GetAttemptedNotificationMethodNames().Contains("textDocument/didChange", StringComparer.Ordinal), s_waitTimeout).ConfigureAwait(false),
			$"Expected the change to reach the transport. Attempted notifications: {string.Join(", ", client.GetAttemptedNotificationMethodNames())}");

		// The open cancels the running update after the store committed its content. The canceled send must
		// invalidate the tracked synchronization so the open re-mirrors the document instead of trusting a server
		// copy that never received the change.
		provider.OpenDocument(s_filePath, UpdatedContent);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(UpdatedContent, client.GetLastNotificationParameters("textDocument/didOpen")
			.GetProperty("textDocument").GetProperty("text").GetString());
		Assert.AreEqual(0, client.GetSentMethodCount("textDocument/didChange"));
		Assert.IsTrue(provider.InvalidatedPaths.Contains(s_filePath));
	}

	[TestMethod]
	public async Task FailedChangeSend_InvalidatesSynchronizationAndReopensOnTheNextRequest()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		bool failChangeSend = true;

		client.SendNotificationHandler = (method, _, _) =>
		{
			if (failChangeSend && string.Equals(method, "textDocument/didChange", StringComparison.Ordinal))
				throw new IOException("Simulated transport failure while sending a document change.");

			return Task.CompletedTask;
		};

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		provider.UpdateDocument(s_filePath, UpdatedContent);
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => provider.InvalidatedPaths.Contains(s_filePath), s_waitTimeout).ConfigureAwait(false),
			$"Expected the failed change to invalidate the document. Attempted notifications: {string.Join(", ", client.GetAttemptedNotificationMethodNames())}");

		// The failure bookkeeping (invalidation and the transport mark) completes asynchronously relative to the
		// recorded send, so wait for the marked state before the follow-up; otherwise, the request can observe the
		// document invalidated but the transport still being restarted.
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => provider.State == LanguageServerProviderState.Unavailable, s_waitTimeout).ConfigureAwait(false),
			$"Expected the failed change send to mark the transport unavailable. State: {provider.State}");

		failChangeSend = false;

		// The next request restarts the transport and re-mirrors the invalidated document with a fresh open.
		Assert.IsNull(await provider.GetHoverAsync(s_filePath, UpdatedContent, new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false),
			$"Expected the invalidated document to be re-mirrored with a fresh open. State: {provider.State}; attempted: {string.Join(", ", client.GetAttemptedNotificationMethodNames())}; sent: {string.Join(", ", client.GetSentMethodNames())}");
		Assert.AreEqual(UpdatedContent, client.GetLastNotificationParameters("textDocument/didOpen")
			.GetProperty("textDocument").GetProperty("text").GetString());
		Assert.AreEqual(0, client.GetSentMethodCount("textDocument/didChange"));
	}

	[TestMethod]
	public async Task FullSynchronizationKind_SendsTheWholeContentWithoutARange()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, TextDocumentSyncKind = TextDocumentSyncKind.Full };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		provider.UpdateDocument(s_filePath, UpdatedContent);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, s_waitTimeout).ConfigureAwait(false));

		JsonElement change = client.GetLastNotificationParameters("textDocument/didChange").GetProperty("contentChanges")[0];

		Assert.AreEqual(UpdatedContent, change.GetProperty("text").GetString());
		Assert.IsFalse(change.TryGetProperty("range", out _));
	}

	[TestMethod]
	public async Task RequestReference_RenameWhileRequestIsInFlight_ReleasesOnTheRenamedRecord()
	{
		string renamedFilePath = Path.Combine(s_workspaceRoot, "Scripts", "renamed.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		var requestGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		client.SendRequestHandler = async (method, _, cancellationToken) =>
		{
			if (string.Equals(method, "test/hover", StringComparison.Ordinal))
				await requestGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

			return null;
		};

		Task<TextHoverInfo?> hoverTask = provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0));

		Assert.IsTrue(await client.WaitForMethodCountAsync("test/hover", 1, s_waitTimeout).ConfigureAwait(false));

		// The document is renamed while the request holds its temporary reference; the rename carries the reference
		// to the renamed record.
		provider.MoveDocument(s_filePath, renamedFilePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));

		requestGate.TrySetResult(true);
		Assert.IsNull(await hoverTask.ConfigureAwait(false));

		// The release must drain the reference on the renamed record: a close now closes the server copy instead of
		// waiting forever behind a stranded reference.
		provider.CloseDocument(renamedFilePath);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 2, s_waitTimeout).ConfigureAwait(false));
		Assert.IsNull(provider.GetTrackedSnapshot(renamedFilePath));
	}

	[TestMethod]
	public async Task TrimmedClose_WhenTheDocumentWasTrackedAgain_SkipsTheServerClose()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		DocumentSnapshot trimmedDocument = provider.GetTrackedSnapshot(s_filePath)
			?? throw new AssertFailedException("Expected the opened document to be tracked.");

		// Simulates the trim/recreate race: the snapshot was evicted, then a concurrent open recreated the record.
		await provider.CloseTrimmedDocumentAsync(trimmedDocument).ConfigureAwait(false);

		Assert.AreEqual(0, client.GetSentMethodCount("textDocument/didClose"));
		Assert.AreEqual(0, provider.InvalidatedPaths.Count);
		Assert.IsNotNull(provider.GetTrackedSnapshot(s_filePath));

		// The recreated record still owns the server document: its own close closes it exactly once.
		provider.CloseDocument(s_filePath);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, s_waitTimeout).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task CallerCancellation_BeforeTheRequestStarts_ThrowsWithoutAnyTraffic()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		using var cancellationSource = new CancellationTokenSource();
		cancellationSource.Cancel();

		await Assert.ThrowsAsync<OperationCanceledException>(
			() => provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0), cancellationSource.Token)).ConfigureAwait(false);

		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public async Task CallerCancellation_AfterTheRequestReferenceWasAcquired_StillReleasesIt()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { MaxTrackedIdleDocuments = 0 });

		var requestGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		client.SendRequestHandler = async (method, _, cancellationToken) =>
		{
			if (string.Equals(method, "test/hover", StringComparison.Ordinal))
				await requestGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

			return null;
		};

		using var cancellationSource = new CancellationTokenSource();
		Task<TextHoverInfo?> hoverTask = provider.GetHoverAsync(s_filePath, Content, new TextPosition(0, 0), cancellationSource.Token);

		Assert.IsTrue(await client.WaitForMethodCountAsync("test/hover", 1, s_waitTimeout).ConfigureAwait(false));

		cancellationSource.Cancel();

		await Assert.ThrowsAsync<OperationCanceledException>(() => hoverTask).ConfigureAwait(false);

		// The release still ran in the finally block; with a zero cap the idle document is trimmed and closed.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, s_waitTimeout).ConfigureAwait(false));
		Assert.IsNull(provider.GetTrackedSnapshot(s_filePath));
	}

	[TestMethod]
	public async Task IdleTrimming_NeverTrimsEditorOpenDocuments()
	{
		string idleFilePath = Path.Combine(s_workspaceRoot, "Scripts", "idle.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client,
			new LanguageServerProviderOptions { MaxTrackedIdleDocuments = 0 });

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		// The request's idle document is trimmed beyond the cap; the editor-open document is not.
		Assert.IsNull(await provider.GetHoverAsync(idleFilePath, "line two", new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(new Uri(idleFilePath).AbsoluteUri, client.GetLastNotificationParameters("textDocument/didClose")
			.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.IsNotNull(provider.GetTrackedSnapshot(s_filePath));
	}

	[TestMethod]
	public async Task MoveDocument_WithAlreadyTrackedDestination_IsANoOp()
	{
		string destinationFilePath = Path.Combine(s_workspaceRoot, "Scripts", "destination.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		provider.OpenDocument(destinationFilePath, "line two");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));

		provider.MoveDocument(s_filePath, destinationFilePath, Content);

		// An update for the tracked destination queues behind the move, so its delivery also proves the move
		// completed without a rekey.
		provider.UpdateDocument(destinationFilePath, "line three");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(2, client.GetSentMethodCount("textDocument/didOpen"));
		Assert.AreEqual(0, client.GetSentMethodCount("textDocument/didClose"));
		Assert.AreEqual(0, provider.MovedPaths.Count);
		Assert.IsNotNull(provider.GetTrackedSnapshot(s_filePath));
		Assert.IsNotNull(provider.GetTrackedSnapshot(destinationFilePath));
	}

	[TestMethod]
	public async Task MoveDocument_WhenTheSourceIsNotTracked_KeepsAQueuedDestinationUpdate()
	{
		string destinationFilePath = Path.Combine(s_workspaceRoot, "Scripts", "destination.test");
		string untrackedSourceFilePath = Path.Combine(s_workspaceRoot, "Scripts", "untracked-source.test");
		var openGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		client.SendNotificationHandler = async (method, _, cancellationToken) =>
		{
			if (string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal))
				await openGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
		};

		// Hold the destination's open so its update stays queued behind it.
		provider.OpenDocument(destinationFilePath, Content);
		Assert.IsTrue(await TestPolling.ForConditionAsync(
			() => client.GetAttemptedNotificationMethodNames().Contains("textDocument/didOpen", StringComparer.Ordinal), s_waitTimeout).ConfigureAwait(false));

		provider.UpdateDocument(destinationFilePath, UpdatedContent);

		// The move from an untracked source resolves to a no-op; it must not cancel the queued update.
		provider.MoveDocument(untrackedSourceFilePath, destinationFilePath, Content);

		openGate.TrySetResult(true);

		Assert.IsTrue(await TestPolling.ForConditionAsync(
			() => provider.GetTrackedSnapshot(destinationFilePath)?.Content == UpdatedContent, s_waitTimeout).ConfigureAwait(false),
			"The queued destination update must survive a move that resolved to a no-op.");

		Assert.AreEqual(0, provider.MovedPaths.Count);
		Assert.IsNull(provider.GetTrackedSnapshot(untrackedSourceFilePath));
	}

	[TestMethod]
	public async Task MoveDocument_WhenTheRekeySucceeds_DropsStaleSourceUpdatesAndAppliesDestinationUpdates()
	{
		string destinationFilePath = Path.Combine(s_workspaceRoot, "Scripts", "moved.test");
		var changeGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		bool gateFirstChange = true;

		client.SendNotificationHandler = async (method, _, cancellationToken) =>
		{
			if (gateFirstChange && string.Equals(method, "textDocument/didChange", StringComparison.Ordinal))
			{
				gateFirstChange = false;
				await changeGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
			}
		};

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		// Hold a source update in flight so the move queues behind it; the updates below then queue behind the move.
		provider.UpdateDocument(s_filePath, UpdatedContent);
		Assert.IsTrue(await TestPolling.ForConditionAsync(
			() => client.GetAttemptedNotificationMethodNames().Contains("textDocument/didChange", StringComparer.Ordinal), s_waitTimeout).ConfigureAwait(false));

		provider.MoveDocument(s_filePath, destinationFilePath, Content);
		provider.UpdateDocument(s_filePath, "line three");
		provider.UpdateDocument(destinationFilePath, "line four");

		changeGate.TrySetResult(true);

		Assert.IsTrue(await TestPolling.ForConditionAsync(
			() => provider.GetTrackedSnapshot(destinationFilePath)?.Content == "line four", s_waitTimeout).ConfigureAwait(false),
			"The destination update requested after the move must be applied to the moved record.");

		// Negative probe: give a stale (wrongly retained) source update time to surface before counting.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.IsNull(provider.GetTrackedSnapshot(s_filePath), "A stale update for the vacated path must not recreate its record.");
		Assert.AreEqual(2, client.GetAttemptedNotificationMethodNames().Count(method => string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal)));
		Assert.AreEqual(2, client.GetAttemptedNotificationMethodNames().Count(method => string.Equals(method, "textDocument/didChange", StringComparison.Ordinal)));
	}

	[TestMethod]
	public async Task ParsedResponse_IsReturnedFromTheConfiguredParser()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		client.SendRequestHandler = static (_, _, _) => Task.FromResult<object?>("parsed-response");

		Assert.AreEqual("parsed-response", await provider.SendParsedRequestAsync(s_filePath, Content).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task StructResponse_WhenTheDispatcherFallsBack_ReturnsTheCallerFallbackValue()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		client.SendRequestHandler = static (_, _, _) => throw new LanguageServerRequestRejectedException(-32602, "Simulated rejection.", null);

		// A non-nullable struct response never compares equal to null; the outcome channel must still return
		// the caller's fallback value instead of parsing a default struct.
		Assert.AreEqual("fallback", await provider.SendStructResponseRequestAsync(s_filePath, Content).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task StructResponse_WhenTheServerResponds_IsParsedFromTheResponse()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		client.SendRequestHandler = static (_, _, _) => Task.FromResult<object?>(new TestStructResponse(7));

		Assert.AreEqual("parsed:7", await provider.SendStructResponseRequestAsync(s_filePath, Content).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task NullResponse_ReturnsTheCallerFallbackValueInsteadOfReachingTheParser()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		client.SendRequestHandler = static (_, _, _) => Task.FromResult<object?>(null);

		// The server answered, but with a JSON null: that is an unusable response for a reference-type payload,
		// so the helper returns the caller's fallback value instead of handing the parser a null.
		Assert.AreEqual("fallback", await provider.SendNullSensitiveRequestAsync(s_filePath, Content).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task MoveDocument_WhenTheReopenSendFails_MarksTheTransportUnavailableAndRecovers()
	{
		string renamedFilePath = Path.Combine(s_workspaceRoot, "Scripts", "renamed.test");

		using var client = new FakeLanguageServerClient { IsReady = false };
		using var provider = new TestLanguageServerProvider([s_workspaceRoot], client);

		provider.OpenDocument(s_filePath, Content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout).ConfigureAwait(false));

		bool failReopenSend = true;

		client.SendNotificationHandler = (method, _, _) =>
		{
			if (failReopenSend && string.Equals(method, "textDocument/didOpen", StringComparison.Ordinal))
				throw new IOException("Simulated rename reopen failure.");

			return Task.CompletedTask;
		};

		provider.MoveDocument(s_filePath, renamedFilePath, Content);

		// The rename rekeyed the record, but its reopen send failed: the transport is marked unhealthy and the
		// renamed document stays tracked with an invalidated server synchronization.
		Assert.IsTrue(await TestPolling.ForConditionAsync(() => provider.State == LanguageServerProviderState.Unavailable, s_waitTimeout).ConfigureAwait(false));
		Assert.IsNull(provider.GetTrackedSnapshot(s_filePath));
		Assert.IsNotNull(provider.GetTrackedSnapshot(renamedFilePath));
		Assert.AreEqual(1, client.GetSentMethodCount("textDocument/didOpen"));

		failReopenSend = false;

		// The next request restarts the transport and re-mirrors the renamed document.
		Assert.IsNull(await provider.GetHoverAsync(renamedFilePath, Content, new TextPosition(0, 0)).ConfigureAwait(false));

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, s_waitTimeout).ConfigureAwait(false));
		Assert.AreEqual(new Uri(renamedFilePath).AbsoluteUri, client.GetLastNotificationParameters("textDocument/didOpen")
			.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.AreEqual(LanguageServerProviderState.Ready, provider.State);
	}
}
