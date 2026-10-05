using System.Text.Json;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Pins the fidelity contracts of <see cref="FakeStatefulLanguageServerClient"/> that the provider tests rely on.</summary>
[TestClass]
public sealed class FakeStatefulLanguageServerClientFidelityTests
{
	[TestMethod]
	public async Task IsReadyReset_ClearsThePublishedCapabilitySnapshot()
	{
		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = true };

		Assert.IsTrue(await client.EnsureStartedAsync(CancellationToken.None));
		Assert.IsTrue(client.IsReady);
		Assert.IsTrue(client.NegotiatedCapabilities.SupportsSemanticTokensFull);
		Assert.AreEqual(TextDocumentSyncKind.Incremental, client.NegotiatedCapabilities.TextDocumentSyncKind);

		client.IsReady = false;

		// The real contract clears the capability snapshot with the connection, so a bare readiness reset must
		// unnegotiate every capability instead of leaving the previous session's values readable.
		Assert.IsFalse(client.IsReady);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsSemanticTokensFull);
		Assert.AreEqual(TextDocumentSyncKind.None, client.NegotiatedCapabilities.TextDocumentSyncKind);
	}

	[TestMethod]
	public async Task SendNotificationAsync_DidChangeFailure_DetachesTheConnection()
	{
		using var client = new FakeStatefulLanguageServerClient { ThrowIOExceptionOnNextDidChange = true };

		Assert.IsTrue(await client.EnsureStartedAsync(CancellationToken.None));
		Assert.IsTrue(client.IsReady);

		bool threw = false;

		try
		{
			await client.SendNotificationAsync(
				"textDocument/didChange",
				new { textDocument = new { uri = "file:///test.lua" } },
				CancellationToken.None
			);
		}
		catch (IOException)
		{
			threw = true;
		}

		// A real write failure detaches the session before surfacing, so the fake drops readiness and unnegotiates
		// its capabilities instead of staying ready for the next send.
		Assert.IsTrue(threw);
		Assert.IsFalse(client.IsReady);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsSemanticTokensFull);
	}

	[TestMethod]
	public async Task PushSettingsAsync_RecordsAConfigurationNotificationAndRequiresAReadyConnection()
	{
		using var client = new FakeStatefulLanguageServerClient { IsReady = false };

		// Like the real client, a push requires a ready connection session.
		await Assert.ThrowsExactlyAsync<IOException>(() => client.PushSettingsAsync(CancellationToken.None));

		Assert.IsEmpty(client.GetSentMethodNames());

		Assert.IsTrue(await client.EnsureStartedAsync(CancellationToken.None));

		await client.PushSettingsAsync(CancellationToken.None);

		// The double has no settings provider, so the push stands in for the real client's: an empty settings document in the
		// notification the provider's request produces.
		Assert.AreSequenceEqual(["workspace/didChangeConfiguration"], client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task SendRequestAsync_ConnectionFailure_DetachesTheConnectionAndRaisesUnavailable()
	{
		using var client = new FakeStatefulLanguageServerClient { ThrowIOExceptionOnNextRequestMethod = "textDocument/hover" };

		Assert.IsTrue(await client.EnsureStartedAsync(CancellationToken.None));

		long generation = client.ConnectionGeneration;
		int unavailableCount = 0;
		long reportedGeneration = 0;

		client.ConnectionUnavailable += (_, eventArgs) =>
		{
			unavailableCount++;
			reportedGeneration = eventArgs.ConnectionGeneration;
		};

		await Assert.ThrowsExactlyAsync<LanguageServerConnectionUnavailableException>(async () =>
		{
			await client.SendRequestAsync<JsonElement>(
				"textDocument/hover",
				new { textDocument = new { uri = "file:///test.lua" } },
				CancellationToken.None
			);
		});

		// The real sender marks the observed generation unhealthy before surfacing the connection-failure
		// exception, so readiness, capabilities and the unavailable event all follow from one counted detach.
		Assert.IsFalse(client.IsReady);
		Assert.IsFalse(client.NegotiatedCapabilities.SupportsSemanticTokensFull);
		Assert.AreEqual(1, client.MarkConnectionUnhealthyCallCount);
		Assert.AreEqual(1, unavailableCount);
		Assert.AreEqual(generation, reportedGeneration);
	}

	[TestMethod]
	public async Task SendRequestAsync_UnconfiguredRequest_ThrowsInsteadOfReturningASilentDefault()
	{
		using var client = new FakeStatefulLanguageServerClient();

		Assert.IsTrue(await client.EnsureStartedAsync(CancellationToken.None));

		// No JSON payload is configured for this method, so the fake must fail loudly instead of fabricating an
		// empty JsonElement response that would silently look like a valid server answer.
		await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
			await client.SendRequestAsync<JsonElement>("textDocument/unknown", new { }, CancellationToken.None)
		);
	}

	[TestMethod]
	public async Task Dispose_IsIdempotentAcrossSyncAndAsyncPaths()
	{
		var client = new FakeStatefulLanguageServerClient();

		client.Dispose();
		await client.DisposeAsync();
		client.Dispose();

		// The real client tears down once and shares that path across Dispose and DisposeAsync, so the fake must
		// not re-run teardown (or count it) for a later sync or async caller.
		Assert.AreEqual(1, client.DisposeCallCount);
	}
}
