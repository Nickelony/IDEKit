using System.Text.Json;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using static Nickelony.LanguageServer.Provider.Tests.DiagnosticsTestHelpers;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers renaming a document: its payloads, leases and diagnostics follow the new path.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderDocumentRenameTests
{
	[TestMethod]
	public async Task RenameDocument_RenamesDiagnosticsAndSemanticTokensToNewPath()
	{
		string workspaceRoot = TestPaths.Root;
		string oldFilePath = TestPaths.Script("test.test");
		string newFilePath = TestPaths.Script("renamed.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = true, SemanticTokenTypes = ["variable"] };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);
		var semanticTokensUpdated = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		var semanticTokensUpdatedForNewPath = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(
			TaskCreationOptions.RunContinuationsAsynchronously
		);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(eventArgs.DocumentId, oldFilePath))
				semanticTokensUpdated.TrySetResult(eventArgs.SemanticTokens);
			else if (LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(eventArgs.DocumentId, newFilePath))
				semanticTokensUpdatedForNewPath.TrySetResult(eventArgs.SemanticTokens);
		};

		provider.OpenDocument(oldFilePath, content);

		Task completedTask = await Task.WhenAny(semanticTokensUpdated.Task, Task.Delay(TestPolling.DefaultTimeout, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreSame(semanticTokensUpdated.Task, completedTask);

		client.PublishDiagnostics(
			CreateDiagnostics(filePath: oldFilePath, version: 1, startCharacter: 6, endCharacter: 11, message: "Current warning.")
		);

		Assert.HasCount(1, provider.GetDiagnostics(oldFilePath));
		Assert.HasCount(1, provider.GetSemanticTokens(oldFilePath));

		provider.RenameDocument(oldFilePath, newFilePath, content);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TestPolling.DefaultTimeout));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, TestPolling.DefaultTimeout));

		// The rename renames the cached tokens to the new path and announces them for that path without
		// waiting for the fresh server response.
		Task newPathCompletedTask = await Task.WhenAny(
				semanticTokensUpdatedForNewPath.Task,
				Task.Delay(TestPolling.DefaultTimeout, TestContext.CancellationToken)
			)
			.ConfigureAwait(false);

		Assert.AreSame(semanticTokensUpdatedForNewPath.Task, newPathCompletedTask);

		IReadOnlyList<SemanticToken> renamedTokens = await semanticTokensUpdatedForNewPath.Task.ConfigureAwait(false);

		Assert.HasCount(1, renamedTokens);
		Assert.AreEqual(6, renamedTokens[0].Range.Offset);
		Assert.AreEqual("variable", renamedTokens[0].Type);

		Assert.IsEmpty(provider.GetDiagnostics(oldFilePath));
		Assert.IsEmpty(provider.GetSemanticTokens(oldFilePath));
		Assert.HasCount(1, provider.GetDiagnostics(newFilePath));
		Assert.HasCount(1, provider.GetSemanticTokens(newFilePath));

		Assert.AreSequenceEqual(
			["textDocument/didOpen", "textDocument/semanticTokens/full", "textDocument/didClose", "textDocument/didOpen"],
			client.GetSentMethodNames()
		);
	}

	[TestMethod]
	public async Task RenameDocument_ContentChangingRename_ClearsThenRefreshesTheNewPath()
	{
		string workspaceRoot = TestPaths.Root;
		string oldFilePath = TestPaths.Script("test.test");
		string newFilePath = TestPaths.Script("renamed.test");

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = true, SemanticTokenTypes = ["variable"] };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		var semanticTokensUpdated = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var clearedTokensUpdated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var refreshedTokensUpdated = new TaskCompletionSource<IReadOnlyList<SemanticToken>>(TaskCreationOptions.RunContinuationsAsynchronously);

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			if (LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(eventArgs.DocumentId, oldFilePath))
			{
				semanticTokensUpdated.TrySetResult(eventArgs.SemanticTokens);
			}
			else if (LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(eventArgs.DocumentId, newFilePath))
			{
				if (eventArgs.SemanticTokens.Count == 0)
					clearedTokensUpdated.TrySetResult(true);
				else
					refreshedTokensUpdated.TrySetResult(eventArgs.SemanticTokens);
			}
		};

		provider.OpenDocument(oldFilePath, "local value = 1");

		Task completedTask = await Task.WhenAny(semanticTokensUpdated.Task, Task.Delay(TestPolling.DefaultTimeout, TestContext.CancellationToken))
			.ConfigureAwait(false);

		Assert.AreSame(semanticTokensUpdated.Task, completedTask);

		client.PublishDiagnostics(
			CreateDiagnostics(filePath: oldFilePath, version: 1, startCharacter: 6, endCharacter: 11, message: "Current warning.")
		);

		Assert.HasCount(1, provider.GetDiagnostics(oldFilePath));
		Assert.HasCount(1, provider.GetSemanticTokens(oldFilePath));

		provider.RenameDocument(oldFilePath, newFilePath, "local value = 2");

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TestPolling.DefaultTimeout));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, TestPolling.DefaultTimeout));

		// The content-changing rename first clears the cached tokens for the new path and announces the empty set.
		Task clearedCompletedTask = await Task.WhenAny(
				clearedTokensUpdated.Task,
				Task.Delay(TestPolling.DefaultTimeout, TestContext.CancellationToken)
			)
			.ConfigureAwait(false);

		Assert.AreSame(clearedTokensUpdated.Task, clearedCompletedTask);

		// Because the content changed, the rename behaves like an ordinary open and refreshes the new path, which
		// then announces the freshly fetched tokens; the old path reports nothing.
		Task refreshedCompletedTask = await Task.WhenAny(
				refreshedTokensUpdated.Task,
				Task.Delay(TestPolling.DefaultTimeout, TestContext.CancellationToken)
			)
			.ConfigureAwait(false);

		Assert.AreSame(refreshedTokensUpdated.Task, refreshedCompletedTask);

		Assert.IsEmpty(provider.GetDiagnostics(oldFilePath));
		Assert.IsEmpty(provider.GetSemanticTokens(oldFilePath));
		Assert.IsEmpty(provider.GetDiagnostics(newFilePath));
		Assert.HasCount(1, provider.GetSemanticTokens(newFilePath));
	}

	[TestMethod]
	public async Task RenameDocument_CaseOnlyRename_FollowsHostPathIdentity()
	{
		string workspaceRoot = TestPaths.Root;
		string oldFilePath = TestPaths.Script("test.test");
		string newFilePath = TestPaths.Script("Test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(oldFilePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		provider.RenameDocument(oldFilePath, newFilePath, content);

		if (!LocalPathComparisonPolicy.ForCurrentPlatform.IgnoreCase)
		{
			// On case-sensitive hosts the rename is observable: the server sees the close and the reopen.
			Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TestPolling.DefaultTimeout));
			Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, TestPolling.DefaultTimeout));
		}
		else
		{
			// On case-insensitive hosts a case-only rename is the same tracked document, so the
			// provider must not send rename traffic; the identity check is synchronous, so nothing can
			// arrive later either.
			Assert.IsFalse(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TimeSpan.FromMilliseconds(250)));
			Assert.AreEqual(1, client.GetSentMethodCount("textDocument/didOpen"));
		}
	}

	[TestMethod]
	public async Task RenameDocument_PreservesOpenLeaseCountsAcrossMultipleTabs()
	{
		string workspaceRoot = TestPaths.Root;
		string oldFilePath = TestPaths.Script("test.test");
		string newFilePath = TestPaths.Script("renamed.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(oldFilePath, content);
		provider.OpenDocument(oldFilePath, content);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		provider.RenameDocument(oldFilePath, newFilePath, content);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TestPolling.DefaultTimeout));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, TestPolling.DefaultTimeout));

		provider.CloseDocument(newFilePath);
		Assert.IsFalse(await client.WaitForMethodCountAsync("textDocument/didClose", 2, TimeSpan.FromMilliseconds(250)));

		provider.CloseDocument(newFilePath);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 2, TestPolling.DefaultTimeout));
	}

	[TestMethod]
	public async Task RenameDocument_UpdateOnNewPath_WaitsForRenameReopenToFinish()
	{
		string workspaceRoot = TestPaths.Root;
		string oldFilePath = TestPaths.Script("test.test");
		string newFilePath = TestPaths.Script("renamed.test");

		const string originalContent = "local value = 1";
		const string updatedContent = "local value = 2";

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = false };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(oldFilePath, originalContent);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.BlockNextOpenNotification();

		provider.RenameDocument(oldFilePath, newFilePath, originalContent);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TestPolling.DefaultTimeout));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, TestPolling.DefaultTimeout));

		provider.UpdateDocument(newFilePath, updatedContent);
		Assert.IsFalse(await client.WaitForMethodCountAsync("textDocument/didChange", 1, TimeSpan.FromMilliseconds(250)));

		client.ReleaseOpenNotification();
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didChange", 1, TestPolling.DefaultTimeout));

		Assert.AreSequenceEqual(
			["textDocument/didOpen", "textDocument/didClose", "textDocument/didOpen", "textDocument/didChange"],
			client.GetSentMethodNames()
		);
	}

	[TestMethod]
	public async Task RenameDocument_DisposeDuringBlockedRenameReopen_DoesNotRaiseDiagnosticsUpdatedForNewPath()
	{
		string workspaceRoot = TestPaths.Root;
		string oldFilePath = TestPaths.Script("test.test");
		string newFilePath = TestPaths.Script("renamed.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient { SupportsSemanticTokensFull = false };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);
		int renamedDiagnosticsUpdatedCount = 0;

		provider.DiagnosticsUpdated += (_, eventArgs) =>
		{
			if (LocalPathComparisonPolicy.ForCurrentPlatform.Comparer.Equals(eventArgs.DocumentId, newFilePath))
				renamedDiagnosticsUpdatedCount++;
		};

		provider.OpenDocument(oldFilePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));

		client.PublishDiagnostics(
			CreateDiagnostics(filePath: oldFilePath, version: 1, startCharacter: 6, endCharacter: 11, message: "Current warning.")
		);

		Assert.HasCount(1, provider.GetDiagnostics(oldFilePath));

		client.BlockNextOpenNotification();

		provider.RenameDocument(oldFilePath, newFilePath, content);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didClose", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 2, TestPolling.DefaultTimeout).ConfigureAwait(false));

		provider.Dispose();
		client.ReleaseOpenNotification();

		// Negative check: no observable signal exists for "no callback arrived", so the bounded window is deliberate.
		await Task.Delay(TestPolling.AbsenceWindow, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.AreEqual(0, renamedDiagnosticsUpdatedCount);
	}

	public TestContext? TestContext { get; set; }
}
