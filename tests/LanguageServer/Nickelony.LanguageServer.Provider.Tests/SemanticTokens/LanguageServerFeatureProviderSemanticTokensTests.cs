using System.Text.Json;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers how the provider requests, decodes, caches and refreshes semantic tokens.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderSemanticTokensTests
{
	[TestMethod]
	public async Task SemanticTokensRefreshRequested_RefreshesTheTrackedDocument()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = true, SemanticTokenTypes = ["variable"] };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);
		var semanticTokensUpdated = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(eventArgs.DocumentId, filePath))
				semanticTokensUpdated.TrySetResult(eventArgs.SemanticTokens);
		};

		await provider.GetHoverAsync(filePath, content, new TextPosition(0, 0));

		client.RaiseSemanticTokensRefreshRequested();

		Task completedTask = await Task.WhenAny(semanticTokensUpdated.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(semanticTokensUpdated.Task, completedTask);

		IReadOnlyList<SemanticToken> semanticTokens = await semanticTokensUpdated.Task.ConfigureAwait(false);

		Assert.HasCount(1, semanticTokens);
		Assert.AreEqual("variable", semanticTokens[0].Type);

		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/hover", "textDocument/semanticTokens/full"], client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task SemanticTokensRefreshRequested_SupersededRequest_AppliesOnlyTheFreshResponse()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = true, SemanticTokenTypes = ["variable"] };

		client.EnqueueSemanticTokensFullResponse(JsonSerializer.SerializeToElement(new { data = new[] { 0, 6, 5, 0, 0 }, resultId = "tokens-1" }));

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		var semanticTokenUpdates = new List<IReadOnlyList<SemanticToken>>();
		var initialUpdate = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var freshUpdate = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (!LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(eventArgs.DocumentId, filePath))
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
		Assert.HasCount(1, provider.GetSemanticTokens(filePath));

		// Two refreshes are in flight while both full-token requests are parked on their response
		// gates: the second refresh supersedes (cancels) the first before either response arrives.
		// Each gate pins its request's response so the assertion cannot depend on resume order.
		client.BlockNextSemanticTokensFullRequest(JsonSerializer.SerializeToElement(new { data = new[] { 0, 7, 2, 0, 0 }, resultId = "tokens-2" }));

		client.BlockNextSemanticTokensFullRequest(JsonSerializer.SerializeToElement(new { data = new[] { 0, 9, 3, 0, 0 }, resultId = "tokens-3" }));

		client.RaiseSemanticTokensRefreshRequested();
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 2, TestPolling.DefaultTimeout));

		client.RaiseSemanticTokensRefreshRequested();
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 3, TestPolling.DefaultTimeout));

		// Releasing the gates resumes the superseded request first; it must discard its response, then
		// the fresh request applies its own.
		client.ReleaseSemanticTokensFullRequest();
		client.ReleaseSemanticTokensFullRequest();

		Task freshCompletedTask = await Task.WhenAny(freshUpdate.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(freshUpdate.Task, freshCompletedTask);

		IReadOnlyList<SemanticToken> freshTokens = await freshUpdate.Task.ConfigureAwait(false);

		Assert.HasCount(1, freshTokens);
		Assert.AreEqual(9, freshTokens[0].Range.Offset);
		Assert.AreEqual(3, freshTokens[0].Range.Length);

		Assert.HasCount(1, provider.GetSemanticTokens(filePath));
		Assert.AreEqual(9, provider.GetSemanticTokens(filePath)[0].Range.Offset);

		// Negative check: the superseded payload never surfaces; the bounded window is deliberate
		// because "no extra update happened" has no completion signal.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		lock (semanticTokenUpdates)
			Assert.HasCount(2, semanticTokenUpdates);
	}

	[TestMethod]
	public async Task OpenDocument_DisposeDuringInFlightSemanticTokensRequest_DoesNotRaiseSemanticTokensUpdated()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = true, SemanticTokenTypes = ["variable"] };

		client.BlockNextSemanticTokensFullRequest();

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);
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
	public async Task SemanticTokensRefreshResponseWithoutTokenStream_KeepsTheCachedTokensAndAsksAgainForTheSameVersion()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = true, SemanticTokenTypes = ["variable"] };

		client.EnqueueSemanticTokensFullResponse(JsonSerializer.SerializeToElement(new { data = new[] { 0, 6, 5, 0, 0 }, resultId = "tokens-1" }));

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		var semanticTokenUpdates = new List<IReadOnlyList<SemanticToken>>();
		var initialUpdate = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (!LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(eventArgs.DocumentId, filePath))
				return;

			lock (semanticTokenUpdates)
				semanticTokenUpdates.Add(eventArgs.SemanticTokens);

			initialUpdate.TrySetResult(eventArgs.SemanticTokens);
		};

		provider.OpenDocument(filePath, content);

		Task completedTask = await Task.WhenAny(initialUpdate.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(initialUpdate.Task, completedTask);

		// A response without a usable token stream (here no `data` member at all), which is also what a rejected request answers, keeps the
		// cached token set and raises no event.
		client.BlockNextSemanticTokensFullRequest(JsonSerializer.SerializeToElement(new { resultId = "tokens-without-data" }));

		client.RaiseSemanticTokensRefreshRequested();
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 2, TestPolling.DefaultTimeout).ConfigureAwait(false));

		client.EnqueueSemanticTokensFullResponse(JsonSerializer.SerializeToElement(new { data = new[] { 0, 9, 3, 0, 0 }, resultId = "tokens-2" }));
		client.ReleaseSemanticTokensFullRequest();

		lock (semanticTokenUpdates)
			Assert.HasCount(1, semanticTokenUpdates);

		Assert.AreEqual(6, provider.GetSemanticTokens(filePath)[0].Range.Offset);

		// The document kept its version, so the refresh asks again and adopts the answer that arrives.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 3, TestPolling.DefaultTimeout).ConfigureAwait(false));

		Assert.IsTrue(
			await TestPolling.ForConditionAsync(() => provider.GetSemanticTokens(filePath)[0].Range.Offset == 9, TestPolling.DefaultTimeout)
		);

		lock (semanticTokenUpdates)
			Assert.HasCount(2, semanticTokenUpdates);
	}

	[TestMethod]
	public async Task RequestThatSynchronizesNewText_RefreshesTheTokensForThatText()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = true, SemanticTokenTypes = ["variable"] };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(filePath, "local value = 1");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));

		// The hover carries newer text, so it synchronizes the document before it asks; the host's update with the same text then sends
		// nothing, so the refresh must come from the request's synchronization.
		await provider.GetHoverAsync(filePath, "local other = 1", new TextPosition(0, 6)).ConfigureAwait(false);
		provider.UpdateDocument(filePath, "local other = 1");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 2, TestPolling.DefaultTimeout).ConfigureAwait(false));

		string[] methods = [.. client.GetSentMethodNames()];

		Assert.ContainsSingle(method => method == "textDocument/didChange", methods);
		Assert.IsGreaterThan(Array.IndexOf(methods, "textDocument/didChange"), Array.LastIndexOf(methods, "textDocument/semanticTokens/full"));
	}

	[TestMethod]
	public async Task SemanticTokensRefreshResponse_AfterTheNegotiatedSnapshotChanged_IsDroppedAndKeepsTheCachedTokens()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = true, SemanticTokenTypes = ["variable"] };

		client.EnqueueSemanticTokensFullResponse(JsonSerializer.SerializeToElement(new { data = new[] { 0, 6, 5, 0, 0 }, resultId = "tokens-1" }));

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		var semanticTokenUpdates = new List<IReadOnlyList<SemanticToken>>();
		var initialUpdate = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (!LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(eventArgs.DocumentId, filePath))
				return;

			lock (semanticTokenUpdates)
				semanticTokenUpdates.Add(eventArgs.SemanticTokens);

			initialUpdate.TrySetResult(eventArgs.SemanticTokens);
		};

		provider.OpenDocument(filePath, content);

		Task completedTask = await Task.WhenAny(initialUpdate.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(initialUpdate.Task, completedTask);

		client.BlockNextSemanticTokensFullRequest(JsonSerializer.SerializeToElement(new { data = new[] { 0, 9, 3, 0, 0 }, resultId = "tokens-2" }));

		client.RaiseSemanticTokensRefreshRequested();
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 2, TestPolling.DefaultTimeout).ConfigureAwait(false));

		// The negotiated snapshot changes while the request is in flight, as it does when the connection restarts with another legend, so the
		// response cannot be decoded with the legend the refresh started from.
		client.SemanticTokenTypes = ["function"];
		client.ReleaseSemanticTokensFullRequest();

		// Negative check: the dropped response has no completion signal, so the bounded window is deliberate.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		lock (semanticTokenUpdates)
			Assert.HasCount(1, semanticTokenUpdates);

		Assert.HasCount(1, provider.GetSemanticTokens(filePath));
		Assert.AreEqual(6, provider.GetSemanticTokens(filePath)[0].Range.Offset);
	}

	[TestMethod]
	public async Task CloseDocument_CancelsInFlightSemanticTokensRequest_KeepsCachedTokens()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = true, SemanticTokenTypes = ["variable"] };

		client.BlockNextSemanticTokensFullRequest();

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);
		int semanticTokenUpdates = 0;

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(eventArgs.DocumentId, filePath))
				Interlocked.Increment(ref semanticTokenUpdates);
		};

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));

		// Close cancels the in-flight refresh, so the released response is discarded; wait for didClose, which
		// runs after the cancellation hook.
		provider.CloseDocument(filePath);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TestPolling.DefaultTimeout));

		client.ReleaseSemanticTokensFullRequest();

		// Negative check: no observable signal exists for "the discarded response raised nothing",
		// so the bounded window is deliberate.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		Assert.AreEqual(0, semanticTokenUpdates);
		Assert.IsEmpty(provider.GetSemanticTokens(filePath));
	}

	[TestMethod]
	public async Task SemanticTokensRefreshRequested_RequestFailure_KeepsCachedSemanticTokens()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = true, SemanticTokenTypes = ["variable"] };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		var tokenUpdates = new List<IReadOnlyList<SemanticToken>>();
		var initialTokensUpdated = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (!LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(eventArgs.DocumentId, filePath))
				return;

			lock (tokenUpdates)
				tokenUpdates.Add(eventArgs.SemanticTokens);

			initialTokensUpdated.TrySetResult(eventArgs.SemanticTokens);
		};

		provider.OpenDocument(filePath, content);

		Task initialCompletedTask = await Task.WhenAny(initialTokensUpdated.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);

		Assert.AreSame(initialTokensUpdated.Task, initialCompletedTask);
		Assert.HasCount(1, provider.GetSemanticTokens(filePath));

		client.ThrowInvalidOperationOnNextRequestMethod = "textDocument/semanticTokens/full";
		client.RaiseSemanticTokensRefreshRequested();

		// The failed request is sent (second full request), and because it fails the refresh keeps the
		// last known token set instead of clearing the highlighting.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 2, TestPolling.DefaultTimeout));

		// Negative check: a failed refresh produces no observable update event, so the bounded window
		// is deliberate.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);

		lock (tokenUpdates)
			Assert.HasCount(1, tokenUpdates);

		IReadOnlyList<SemanticToken> cachedTokens = provider.GetSemanticTokens(filePath);

		Assert.HasCount(1, cachedTokens);
		Assert.AreEqual("variable", cachedTokens[0].Type);

		Assert.AreSequenceEqual(
			["textDocument/didOpen", "textDocument/semanticTokens/full", "textDocument/semanticTokens/full"],
			client.GetSentMethodNames()
		);
	}

	[TestMethod]
	public async Task SemanticTokensRefreshAfterDisposal_RunsWithACanceledToken()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = true, SemanticTokenTypes = ["variable"] };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));

		DocumentSnapshot document =
			provider.Composition.Documents.GetDocumentSnapshot(filePath) ?? throw new AssertFailedException("The opened document must be tracked.");

		provider.Dispose();

		// A refresh that reaches the coordinator after disposal must run with a canceled token.
		CancellationToken effectiveToken = CancellationToken.None;

		await provider
			.Composition.SemanticTokensSource.Requests.RunAsync(
				document,

				token =>
				{
					effectiveToken = token;
					return Task.FromResult(true);
				},

				CancellationToken.None
			)
			.ConfigureAwait(false);

		Assert.IsTrue(effectiveToken.IsCancellationRequested);
	}

	[TestMethod]
	public async Task OpenDocument_SemanticTokenModifiers_AreDecodedFromTheLegend()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"],
			SemanticTokenModifiers = ["declaration", "readonly"],
		};

		// The first token carries modifier bit 0, which the legend maps back to "declaration".
		client.EnqueueSemanticTokensFullResponse(JsonSerializer.SerializeToElement(new { data = new[] { 0, 6, 5, 0, 1 }, resultId = "tokens-1" }));

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);
		var semanticTokensUpdated = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(eventArgs.DocumentId, filePath))
				semanticTokensUpdated.TrySetResult(eventArgs.SemanticTokens);
		};

		provider.OpenDocument(filePath, "local value = 1");

		Task completedTask = await Task.WhenAny(semanticTokensUpdated.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(semanticTokensUpdated.Task, completedTask);

		IReadOnlyList<SemanticToken> semanticTokens = await semanticTokensUpdated.Task.ConfigureAwait(false);

		Assert.HasCount(1, semanticTokens);
		Assert.AreEqual("variable", semanticTokens[0].Type);

		Assert.AreSequenceEqual(["declaration"], [.. semanticTokens[0].Modifiers]);
	}

	[TestMethod]
	public async Task SemanticTokensRefreshRequested_WithManyDocuments_BoundsTheConcurrentFanOut()
	{
		string workspaceRoot = TestPaths.Root;
		string[] filePaths = [.. Enumerable.Range(0, 6).Select(index => TestPaths.Script($"fanout_{index}.test"))];

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = true, SemanticTokenTypes = ["variable"] };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		foreach (string filePath in filePaths)
			provider.OpenDocument(filePath, "local value = 1");

		// Wait until every open-triggered refresh completed and cached its token set, so no in-flight
		// request can consume a fan-out gate queued below.
		await TestPolling
			.WaitForAsync(
				action: () => Task.FromResult(filePaths.All(filePath => provider.GetSemanticTokens(filePath).Count > 0)),
				isSatisfied: static allCached => allCached,
				timeout: TestPolling.DefaultTimeout,
				failureMessage: "Expected every opened document to cache its semantic tokens.",
				describeLastResult: static allCached => $"allCached={allCached}"
			)
			.ConfigureAwait(false);

		// Park the next four requests - the fan-out bound - and trigger a refresh for all six documents.
		for (int i = 0; i < 4; i++)
			client.BlockNextSemanticTokensFullRequest();

		client.RaiseSemanticTokensRefreshRequested();

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", filePaths.Length + 4, TestPolling.DefaultTimeout));

		// Negative check: while all four permits are parked, the remaining documents wait instead of
		// issuing an unbounded burst, so the bounded window is deliberate.
		await Task.Delay(TestPolling.AbsenceWindow).ConfigureAwait(false);
		Assert.AreEqual(filePaths.Length + 4, client.GetSentMethodCount("textDocument/semanticTokens/full"));

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
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = true, SemanticTokenTypes = ["variable"] };

		client.BlockNextSemanticTokensFullRequest();

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

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
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = true, SemanticTokenTypes = ["variable"] };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);
		var observedTokens = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, _) => throw new InvalidOperationException("Subscriber failure.");

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(eventArgs.DocumentId, filePath))
				observedTokens.TrySetResult(eventArgs.SemanticTokens);
		};

		provider.OpenDocument(filePath, "local value = 1");

		// A throwing subscriber is isolated: later subscribers still receive the token set.
		Task completedTask = await Task.WhenAny(observedTokens.Task, Task.Delay(TestPolling.DefaultTimeout)).ConfigureAwait(false);
		Assert.AreSame(observedTokens.Task, completedTask);

		IReadOnlyList<SemanticToken> semanticTokens = await observedTokens.Task.ConfigureAwait(false);

		Assert.HasCount(1, semanticTokens);
		Assert.AreEqual("variable", semanticTokens[0].Type);
	}

	[TestMethod]
	public async Task OpenDocument_SemanticTokensFullRequest_OmitsPreviousResultId()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = true, SemanticTokenTypes = ["variable"] };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

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
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = false, SemanticTokenTypes = ["variable"] };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));

		Assert.DoesNotContain("textDocument/semanticTokens/full", client.GetSentMethodNames());
	}

	private static readonly int[] s_tokenData = [0, 6, 5, 0, 0];

	[TestMethod]
	public async Task SemanticTokensRefreshRequested_AfterConnectionFailure_KeepsTokensAndRequestsFullRefresh()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["variable"],
			ThrowIOExceptionOnNextDidChange = true,
		};

		client.EnqueueSemanticTokensFullResponse(JsonSerializer.SerializeToElement(new { data = s_tokenData, resultId = "tokens-1" }));

		client.EnqueueSemanticTokensFullResponse(JsonSerializer.SerializeToElement(new { data = s_tokenData, resultId = "tokens-2" }));

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);
		var firstTokens = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(eventArgs.DocumentId, filePath))
				firstTokens.TrySetResult(eventArgs.SemanticTokens);
		};

		// An open lease keeps the document reachable for the refresh path after the reopen, and the
		// open also fetches the full token set.
		provider.OpenDocument(filePath, "local value = 1");

		Task firstCompletedTask = await Task.WhenAny(firstTokens.Task, Task.Delay(TestPolling.DefaultTimeout, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreSame(firstTokens.Task, firstCompletedTask);
		Assert.HasCount(1, provider.GetSemanticTokens(filePath));

		// The failed incremental update invalidates the tracked server synchronization: the reopened
		// document keeps its last decoded tokens, and the next refresh requests a full payload again.
		provider.UpdateDocument(filePath, "local value = 2");
		Assert.IsTrue(await client.WaitForNotificationAsync("textDocument/didChange", TestPolling.DefaultTimeout));

		// The failed send's recovery bookkeeping completes asynchronously relative to the recorded
		// notification, so re-issue the trigger until the provider re-establishes the document
		// instead of pinning one interleaving.
		await TestPolling.WaitForAsync(
			action: async () =>
			{
				await provider
					.GetHoverAsync(
						filePath,
						documentText: "local value = 2",
						position: new TextPosition(0, 0),
						cancellationToken: TestContext.CancellationToken
					)
					.ConfigureAwait(false);

				return client.GetSentMethodCount("textDocument/didOpen");
			},

			isSatisfied: static didOpenCount => didOpenCount >= 2,
			timeout: TestPolling.DefaultTimeout,
			failureMessage: "Expected the provider to re-establish the document after the connection failure.",
			describeLastResult: static didOpenCount => $"didOpenCount={didOpenCount}"
		);

		Assert.HasCount(1, provider.GetSemanticTokens(filePath));

		// The reopen fetches the tokens of the document with a full request of its own, so only a request after it can come from the refresh.
		Assert.IsTrue(
			await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 2, TestPolling.DefaultTimeout),
			$"Sent methods: {string.Join(", ", client.GetSentMethodNames())}"
		);

		client.RaiseSemanticTokensRefreshRequested();

		Assert.IsTrue(
			await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 3, TestPolling.DefaultTimeout),
			$"Sent methods: {string.Join(", ", client.GetSentMethodNames())}"
		);
	}

	[TestMethod]
	public async Task SemanticTokensRefreshRequested_AfterDispose_DoesNotSend()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = true, SemanticTokenTypes = ["variable"] };

		LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(filePath, "local value = 1");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 1, TestPolling.DefaultTimeout));

		provider.Dispose();
		client.RaiseSemanticTokensRefreshRequested();

		// Disposal unsubscribes the client event, so a refresh published afterwards produces no traffic.
		Assert.IsFalse(await client.WaitForMethodCountAsync("textDocument/semanticTokens/full", 2, TimeSpan.FromMilliseconds(250)));
	}

	public TestContext? TestContext { get; set; }
}
