using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.LanguageFeatures.Rename;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the aggregate's feature members: each reaches its feature provider, and each guards a missing request.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderFeatureTests
{
	private const string Content = "local a = 1";

	private static readonly string s_workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-features-" + Guid.NewGuid().ToString("N"));
	private static readonly string s_filePath = Path.Combine(s_workspaceRoot, "Scripts", "test.test");

	private static FakeLanguageServerClient CreateClient()
	{
		return new FakeLanguageServerClient
		{
			IsReady = false,
			SupportsHover = true,
			SupportsDefinition = true,
			SupportsSignatureHelp = true,
			SupportsReferences = true,
			SupportsRename = true,
			SupportsFormatting = true,
			SupportsDocumentSymbols = true,
			SupportsCodeActions = true,
		};
	}

	private static async Task AssertReachesTheServerAsync(Func<LanguageServerFeatureProvider, Task> call, string method)
	{
		using FakeLanguageServerClient client = CreateClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		await call(provider).ConfigureAwait(false);

		Assert.Contains(method, client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_SendsACompletionRequest()
	{
		var request = new CompletionRequest(s_filePath, Content, caretOffset: 0);
		await AssertReachesTheServerAsync(provider => provider.GetCompletionItemsAsync(request), LspMethodNames.Completion);
	}

	[TestMethod]
	public async Task GetHoverAsync_SendsAHoverRequest()
	{
		var request = new HoverRequest(s_filePath, Content, hoveredOffset: 0);
		await AssertReachesTheServerAsync(provider => provider.GetHoverAsync(request), LspMethodNames.Hover);
	}

	[TestMethod]
	public async Task GetDefinitionAsync_SendsADefinitionRequest()
	{
		var request = new DefinitionRequest(documentId: s_filePath, documentText: Content, symbolName: "a", symbolOffset: 0);
		await AssertReachesTheServerAsync(provider => provider.GetDefinitionAsync(request), LspMethodNames.Definition);
	}

	[TestMethod]
	public async Task GetReferencesAsync_SendsAReferencesRequest()
	{
		var request = new ReferenceRequest(s_filePath, Content, symbolOffset: 0);
		await AssertReachesTheServerAsync(provider => provider.GetReferencesAsync(request), LspMethodNames.References);
	}

	[TestMethod]
	public async Task GetRenameEditsAsync_SendsARenameRequest()
	{
		var request = new RenameRequest(documentId: s_filePath, documentText: Content, symbolOffset: 0, newName: "b");
		await AssertReachesTheServerAsync(provider => provider.GetRenameEditsAsync(request), LspMethodNames.Rename);
	}

	[TestMethod]
	public async Task GetFormattingEditsAsync_SendsAFormattingRequest()
	{
		var request = new FormattingRequest(s_filePath, Content, new FormattingOptions(tabSize: 4, insertSpaces: true));
		await AssertReachesTheServerAsync(provider => provider.GetFormattingEditsAsync(request), LspMethodNames.Formatting);
	}

	[TestMethod]
	public async Task GetSignatureHelpAsync_SendsASignatureHelpRequest()
	{
		var request = new SignatureHelpRequest(s_filePath, Content, caretOffset: 0);
		await AssertReachesTheServerAsync(provider => provider.GetSignatureHelpAsync(request), LspMethodNames.SignatureHelp);
	}

	[TestMethod]
	public async Task GetDocumentSymbolsAsync_SendsADocumentSymbolRequest()
	{
		var request = new DocumentSymbolRequest(s_filePath, Content, filterText: string.Empty);
		await AssertReachesTheServerAsync(provider => provider.GetDocumentSymbolsAsync(request), LspMethodNames.DocumentSymbol);
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_SendsACodeActionRequest()
	{
		var request = new CodeActionRequest(s_filePath, Content, new TextRange(0, 1));
		await AssertReachesTheServerAsync(provider => provider.GetCodeActionsAsync(request), LspMethodNames.CodeAction);
	}

	[TestMethod]
	public void EachFeatureMember_WithANullRequest_ThrowsAtTheCallBeforeAnyTraffic()
	{
		using FakeLanguageServerClient client = CreateClient();

		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		Assert.ThrowsExactly<ArgumentNullException>(() => _ = provider.GetCompletionItemsAsync(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = provider.GetHoverAsync(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = provider.GetDefinitionAsync(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = provider.GetReferencesAsync(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = provider.GetRenameEditsAsync(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = provider.GetFormattingEditsAsync(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = provider.GetSignatureHelpAsync(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = provider.GetDocumentSymbolsAsync(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => _ = provider.GetCodeActionsAsync(null!));
		Assert.IsEmpty(client.GetSentMethodNames());
	}

	[TestMethod]
	public void TheSourceMembers_WithANullDocumentId_ThrowArgumentNullException()
	{
		using FakeLanguageServerClient client = CreateClient();

		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		Assert.ThrowsExactly<ArgumentNullException>(() => provider.GetDiagnostics(null!));
		Assert.ThrowsExactly<ArgumentNullException>(() => provider.GetSemanticTokens(null!));
	}
}
