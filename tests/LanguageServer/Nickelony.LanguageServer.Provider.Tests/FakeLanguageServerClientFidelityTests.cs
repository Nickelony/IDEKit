using System.Text.Json;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Pins how <see cref="FakeLanguageServerClient"/> stands in for the real client where the provider and the Roslyn tests rely on it.
/// </summary>
[TestClass]
public sealed class FakeLanguageServerClientFidelityTests
{
	[TestMethod]
	public void TryMarkConnectionUnhealthy_ForTheActiveGeneration_DetachesTheSessionAndReportsTrue()
	{
		using var client = new FakeLanguageServerClient { ConnectionGeneration = 1 };

		// The real client detaches the matching generation and reports whether it did, so the double applies the same
		// generation-matching rule instead of reporting false for every call.
		Assert.IsTrue(client.TryMarkConnectionUnhealthy(1));
		Assert.IsFalse(client.IsReady);
	}

	[TestMethod]
	public async Task SendNotificationAsync_RecordsTheSendSynchronously()
	{
		using var client = new FakeLanguageServerClient();

		await client.SendNotificationAsync("textDocument/didOpen", new { }, CancellationToken.None).ConfigureAwait(false);

		// The double completes sends synchronously, so the record exists immediately after the awaited call instead
		// of behind a delivery loop.
		Assert.AreEqual(1, client.GetSentMethodCount("textDocument/didOpen"));
	}

	[TestMethod]
	public async Task PushSettingsAsync_RecordsAConfigurationNotification()
	{
		using var client = new FakeLanguageServerClient();

		await client.PushSettingsAsync(CancellationToken.None).ConfigureAwait(false);

		// The double has no settings provider, so the push stands in for the real client's: the settings document the test
		// assigned (empty by default) in the notification the provider's request produces.
		Assert.AreEqual(1, client.GetSentMethodCount("workspace/didChangeConfiguration"));
	}

	[TestMethod]
	public async Task SendRequestAsync_WithoutAHandler_ReturnsTheDocumentedDefaultFallback()
	{
		using var client = new FakeLanguageServerClient();

		JsonElement result = await client.SendRequestAsync<JsonElement>("textDocument/hover", new { }, CancellationToken.None).ConfigureAwait(false);

		// No response is scripted, so the double surfaces the framework's documented fallback (default) instead of
		// fabricating a response payload; the send is still recorded.
		Assert.AreEqual(JsonValueKind.Undefined, result.ValueKind);
		Assert.AreEqual(1, client.GetSentMethodCount("textDocument/hover"));
	}

	[TestMethod]
	public async Task EnsureStartedAsync_AfterDispose_ThrowsObjectDisposedException()
	{
		var client = new FakeLanguageServerClient();

		// Dispose is a one-way flag shared by the sync and async paths; a disposed double must refuse to start
		// rather than silently report readiness, mirroring the real client's disposed-access guard.
		client.Dispose();
		client.Dispose();

		await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => client.EnsureStartedAsync(CancellationToken.None)).ConfigureAwait(false);

		await client.DisposeAsync().ConfigureAwait(false);
	}
}
