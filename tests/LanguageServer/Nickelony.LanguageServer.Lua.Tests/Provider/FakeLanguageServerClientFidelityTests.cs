using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

/// <summary>
/// Pins the fidelity contracts of <see cref="FakeLanguageServerClient"/> that the provider tests rely on:
/// a readiness reset clears the capability snapshot, a notification or request write failure detaches the
/// transport before surfacing, an unconfigured request fails loudly, and disposal is a single shared teardown,
/// all mirroring the real client.
/// </summary>
[TestClass]
public sealed class FakeLanguageServerClientFidelityTests
{
	[TestMethod]
	public async Task IsReadyReset_ClearsThePublishedCapabilitySnapshot()
	{
		using var client = new FakeLanguageServerClient { SupportsSemanticTokensFull = true };

		Assert.IsTrue(await client.StartAsync(CancellationToken.None));
		Assert.IsTrue(client.IsReady);
		Assert.IsTrue(client.SupportsSemanticTokensFull);
		Assert.AreEqual(TextDocumentSyncKind.Incremental, client.TextDocumentSyncKind);

		client.IsReady = false;

		// The real contract clears the capability snapshot with the transport, so a bare readiness reset must
		// unnegotiate every capability instead of leaving the previous session's values readable.
		Assert.IsFalse(client.IsReady);
		Assert.IsFalse(client.SupportsSemanticTokensFull);
		Assert.AreEqual(TextDocumentSyncKind.None, client.TextDocumentSyncKind);
	}

	[TestMethod]
	public async Task SendNotificationAsync_DidChangeFailure_DetachesTheTransport()
	{
		using var client = new FakeLanguageServerClient { ThrowIOExceptionOnNextDidChange = true };

		Assert.IsTrue(await client.StartAsync(CancellationToken.None));
		Assert.IsTrue(client.IsReady);

		bool threw = false;

		try
		{
			await client.SendNotificationAsync(
				"textDocument/didChange",
				new { textDocument = new { uri = "file:///test.lua" } },
				CancellationToken.None);
		}
		catch (IOException)
		{
			threw = true;
		}

		// A real write failure detaches the session before surfacing, so the fake drops readiness and unnegotiates
		// its capabilities instead of staying ready for the next send.
		Assert.IsTrue(threw);
		Assert.IsFalse(client.IsReady);
		Assert.IsFalse(client.SupportsSemanticTokensFull);
	}

	[TestMethod]
	public async Task SendRequestAsync_TransportFailure_DetachesTheTransportAndRaisesUnavailable()
	{
		using var client = new FakeLanguageServerClient { ThrowIOExceptionOnNextRequestMethod = "textDocument/hover" };

		Assert.IsTrue(await client.StartAsync(CancellationToken.None));

		long generation = client.TransportGeneration;
		int unavailableCount = 0;
		long reportedGeneration = 0;

		client.TransportUnavailable += (_, eventArgs) =>
		{
			unavailableCount++;
			reportedGeneration = eventArgs.Generation;
		};

		await Assert.ThrowsExactlyAsync<LanguageServerTransportUnavailableException>(async () =>
		{
			await client.SendRequestAsync<JsonElement>(
				"textDocument/hover",
				new { textDocument = new { uri = "file:///test.lua" } },
				CancellationToken.None);
		});

		// The real sender marks the observed generation unhealthy before surfacing the transport-failure
		// exception, so readiness, capabilities and the unavailable event all follow from one counted detach.
		Assert.IsFalse(client.IsReady);
		Assert.IsFalse(client.SupportsSemanticTokensFull);
		Assert.AreEqual(1, client.MarkTransportUnhealthyCallCount);
		Assert.AreEqual(1, unavailableCount);
		Assert.AreEqual(generation, reportedGeneration);
	}

	[TestMethod]
	public async Task SendRequestAsync_UnconfiguredRequest_ThrowsInsteadOfReturningASilentDefault()
	{
		using var client = new FakeLanguageServerClient();

		Assert.IsTrue(await client.StartAsync(CancellationToken.None));

		// No JSON payload is configured for this method, so the fake must fail loudly instead of fabricating an
		// empty JsonElement response that would silently look like a valid server answer.
		await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
		{
			await client.SendRequestAsync<JsonElement>("textDocument/unknown", new { }, CancellationToken.None);
		});
	}

	[TestMethod]
	public async Task Dispose_IsIdempotentAcrossSyncAndAsyncPaths()
	{
		var client = new FakeLanguageServerClient();

		client.Dispose();
		await client.DisposeAsync();
		client.Dispose();

		// The real client tears down once and shares that path across Dispose and DisposeAsync, so the fake must
		// not re-run teardown (or count it) for a later sync or async caller.
		Assert.AreEqual(1, client.DisposeCallCount);
	}
}
