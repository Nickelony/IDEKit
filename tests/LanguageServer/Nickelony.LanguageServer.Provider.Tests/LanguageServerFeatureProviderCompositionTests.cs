using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the order in which the aggregate composes the push sources behind one document signal.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderCompositionTests
{
	private const string Content = "local a = 1";

	private static readonly string s_workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-composition-" + Guid.NewGuid().ToString("N"));
	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(5);

	[TestMethod]
	public async Task RenameDocument_AnnouncesTheDiagnosticsBeforeTheSemanticTokens()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		string oldPath = Path.Combine(s_workspaceRoot, "Scripts", "old.test");
		string newPath = Path.Combine(s_workspaceRoot, "Scripts", "new.test");

		var order = new List<string>();

		provider.DiagnosticsUpdated += (_, eventArgs) =>
		{
			lock (order)
				order.Add($"diagnostics:{Path.GetFileName(eventArgs.DocumentId)}");
		};

		provider.SemanticTokensUpdated += (_, eventArgs) =>
		{
			lock (order)
				order.Add($"tokens:{Path.GetFileName(eventArgs.DocumentId)}");
		};

		provider.OpenDocument(oldPath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		provider.RenameDocument(oldPath, newPath, Content);

		Assert.IsTrue(
			await TestPolling.ForConditionAsync(
				() =>
				{
					lock (order)
						return order.Contains("tokens:new.test");
				},

				s_waitTimeout
			)
		);

		lock (order)
			Assert.AreSequenceEqual(["diagnostics:new.test", "tokens:new.test"], order);
	}

	[TestMethod]
	public async Task OpenDocument_RefreshesTheSemanticTokensBeforeItPullsTheDiagnostics()
	{
		using var client = new FakeLanguageServerClient
		{
			IsReady = false,
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["keyword"],
			SupportsPullDiagnostics = true,
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		provider.OpenDocument(Path.Combine(s_workspaceRoot, "Scripts", "a.test"), Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync(LspMethodNames.SemanticTokensFull, 1, s_waitTimeout));
		Assert.IsTrue(await client.WaitForMethodCountAsync(LspMethodNames.DocumentDiagnostic, 1, s_waitTimeout));

		// Both triggers start inline from the one synchronized signal, so the order of the two requests is the order the base composed them in.
		string[] sent = client.GetSentMethodNames();

		Assert.IsLessThan(Array.IndexOf(sent, LspMethodNames.DocumentDiagnostic), Array.IndexOf(sent, LspMethodNames.SemanticTokensFull));
	}
}
