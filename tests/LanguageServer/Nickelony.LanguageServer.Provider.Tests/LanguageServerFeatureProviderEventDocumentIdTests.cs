using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using static Nickelony.LanguageServer.Provider.Tests.DiagnosticsTestHelpers;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the document id that the provider's document-scoped events carry when the host spells a path differently.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderEventDocumentIdTests
{
	// The "/./" segment makes the host's id differ ordinally from the normalized path on every platform.
	private static string HostSpelling(string fileName) => TestPaths.Root + "/Scripts/./" + fileName;

	[TestMethod]
	public async Task PublishDiagnostics_ForADocumentOpenedUnderAnotherSpelling_RaisesTheIdTheHostOpenedItWith()
	{
		string hostDocumentId = HostSpelling("test.test");
		string normalizedFilePath = TestPaths.Script("test.test");

		Assert.AreNotEqual(normalizedFilePath, hostDocumentId);

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client);
		var raisedIds = new List<string>();

		provider.DiagnosticsUpdated += (_, eventArgs) =>
		{
			lock (raisedIds)
				raisedIds.Add(eventArgs.DocumentId);
		};

		provider.OpenDocument(hostDocumentId, "local value = 1");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.PublishDiagnostics(
			CreateDiagnostics(filePath: normalizedFilePath, version: 1, startCharacter: 6, endCharacter: 11, message: "Warning.")
		);

		lock (raisedIds)
			Assert.AreSequenceEqual([hostDocumentId], raisedIds);
	}

	[TestMethod]
	public async Task PublishDiagnostics_AfterASecondOpenUnderAnotherSpelling_RaisesTheLatestOpenId()
	{
		string firstDocumentId = TestPaths.Script("test.test");
		string secondDocumentId = HostSpelling("test.test");

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client);
		var raisedIds = new List<string>();

		provider.DiagnosticsUpdated += (_, eventArgs) =>
		{
			lock (raisedIds)
				raisedIds.Add(eventArgs.DocumentId);
		};

		provider.OpenDocument(firstDocumentId, "local value = 1");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		provider.OpenDocument(secondDocumentId, "local value = 1");

		// The second open sends nothing, because the content is unchanged, so a request through the same slot marks
		// the point where its synchronization has run.
		await provider.GetHoverAsync(
			filePath: firstDocumentId,
			documentText: "local value = 1",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		client.PublishDiagnostics(CreateDiagnostics(filePath: firstDocumentId, version: 1, startCharacter: 6, endCharacter: 11, message: "Warning."));

		lock (raisedIds)
			Assert.AreSequenceEqual([secondDocumentId], raisedIds);
	}

	[TestMethod]
	public async Task PublishDiagnostics_ForADocumentTrackedOnlyByARequest_RaisesTheRequestsDocumentId()
	{
		string hostDocumentId = HostSpelling("test.test");
		string normalizedFilePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client);
		var raisedIds = new List<string>();

		provider.DiagnosticsUpdated += (_, eventArgs) =>
		{
			lock (raisedIds)
				raisedIds.Add(eventArgs.DocumentId);
		};

		await provider.GetHoverAsync(
			filePath: hostDocumentId,
			documentText: "local value = 1",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		client.PublishDiagnostics(
			CreateDiagnostics(filePath: normalizedFilePath, version: 1, startCharacter: 6, endCharacter: 11, message: "Warning.")
		);

		lock (raisedIds)
			Assert.AreSequenceEqual([hostDocumentId], raisedIds);
	}

	[TestMethod]
	public async Task SemanticTokensUpdated_ForADocumentOpenedUnderAnotherSpelling_RaisesTheIdTheHostOpenedItWith()
	{
		string hostDocumentId = HostSpelling("test.test");

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = true, SemanticTokenTypes = ["variable"] };
		using LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client);
		var raisedId = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) => raisedId.TrySetResult(eventArgs.DocumentId);

		provider.OpenDocument(hostDocumentId, "local value = 1");

		Task completedTask = await Task.WhenAny(raisedId.Task, Task.Delay(TestPolling.DefaultTimeout, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreSame(raisedId.Task, completedTask);
		Assert.AreEqual(hostDocumentId, await raisedId.Task.ConfigureAwait(false));
	}

	[TestMethod]
	public async Task RenameDocument_ToAnotherSpelling_RaisesTheRenamedPayloadsUnderTheNewHostId()
	{
		string oldDocumentId = TestPaths.Script("test.test");
		string newDocumentId = HostSpelling("renamed.test");
		string newFilePath = TestPaths.Script("renamed.test");

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client);
		var renamedId = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.OpenDocument(oldDocumentId, "local value = 1");
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.PublishDiagnostics(CreateDiagnostics(filePath: oldDocumentId, version: 1, startCharacter: 6, endCharacter: 11, message: "Warning."));

		provider.DiagnosticsUpdated += (_, eventArgs) => renamedId.TrySetResult(eventArgs.DocumentId);
		provider.RenameDocument(oldDocumentId, newDocumentId, "local value = 1");

		Task completedTask = await Task.WhenAny(renamedId.Task, Task.Delay(TestPolling.DefaultTimeout, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreSame(renamedId.Task, completedTask);
		Assert.AreEqual(newDocumentId, await renamedId.Task.ConfigureAwait(false));

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, TestPolling.DefaultTimeout));
		client.PublishDiagnostics(CreateDiagnostics(filePath: newFilePath, version: 1, startCharacter: 6, endCharacter: 11, message: "Warning."));

		IReadOnlyList<Diagnostic> diagnostics = provider.GetDiagnostics(newDocumentId);
		Assert.HasCount(1, diagnostics);
	}

	public TestContext? TestContext { get; set; }
}
