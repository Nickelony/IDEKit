using System.Text.Json;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the construction contract of the provider: lazy startup and the didOpen payload.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderConstructionTests
{
	[TestMethod]
	public async Task Provider_IsUnavailableAndReportsNoCapabilitiesBeforeLazyStartup()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		int capabilitiesChangedCount = 0;

		provider.CapabilitiesChanged += (_, _) => capabilitiesChangedCount++;

		Assert.AreEqual(LanguageServerFeatureProviderState.Unavailable, provider.State);
		Assert.IsFalse(provider.IsAvailable);
		Assert.IsFalse(provider.Capabilities.SupportsReferences);
		Assert.IsFalse(provider.Capabilities.SupportsRename);

		await provider.GetHoverAsync(
			filePath,
			documentText: "local value = 1",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreEqual(LanguageServerFeatureProviderState.Ready, provider.State);
		Assert.IsTrue(provider.IsAvailable);
		Assert.IsTrue(provider.Capabilities.SupportsReferences);
		Assert.IsTrue(provider.Capabilities.SupportsRename);
		Assert.AreEqual(1, capabilitiesChangedCount);
	}

	[TestMethod]
	public async Task Capabilities_AfterStartup_CarryTheServersTriggerCharacters()
	{
		using var client = new FakeStatefulLanguageServerClient
		{
			IsReady = false,
			CompletionTriggerCharacters = [".", ":"],
			SignatureHelpTriggerCharacters = ["("],
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client);

		Assert.IsEmpty(provider.Capabilities.CompletionTriggerCharacters);

		await provider.GetHoverAsync(
			filePath: TestPaths.Script("test.test"),
			documentText: "local value = 1",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreSequenceEqual([".", ":"], [.. provider.Capabilities.CompletionTriggerCharacters]);
		Assert.AreSequenceEqual(["("], [.. provider.Capabilities.SignatureHelpTriggerCharacters]);
	}

	[TestMethod]
	public async Task TriggerCharacterContracts_ReadTheNegotiatedCapabilities_AndAreEmptyBeforeTheSession()
	{
		using var client = new FakeStatefulLanguageServerClient
		{
			IsReady = false,
			CompletionTriggerCharacters = [".", ":"],
			SignatureHelpTriggerCharacters = ["("],
			SignatureHelpRetriggerCharacters = [")"],
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([TestPaths.Root], client);
		ICompletionTriggerCharacters completion = provider;
		ISignatureHelpTriggerCharacters signatureHelp = provider;

		Assert.IsEmpty(completion.TriggerCharacters);
		Assert.IsEmpty(signatureHelp.TriggerCharacters);
		Assert.IsEmpty(signatureHelp.RetriggerCharacters);

		await provider.GetHoverAsync(
			filePath: TestPaths.Script("test.test"),
			documentText: "local value = 1",
			position: new TextPosition(0, 0),
			cancellationToken: TestContext.CancellationToken
		);

		Assert.AreSequenceEqual([".", ":"], [.. completion.TriggerCharacters]);
		Assert.AreSequenceEqual(["("], [.. signatureHelp.TriggerCharacters]);
		Assert.AreSequenceEqual([")"], [.. signatureHelp.RetriggerCharacters]);
	}

	[TestMethod]
	public async Task OpenDocument_SendsDidOpenPayloadWithTheLanguageIdAndInitialDocumentVersion()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		JsonElement parameters = client.GetLastNotificationParameters("textDocument/didOpen");
		JsonElement textDocument = parameters.GetProperty("textDocument");

		Assert.AreEqual(new Uri(filePath).AbsoluteUri, textDocument.GetProperty("uri").GetString());
		Assert.AreEqual("test", textDocument.GetProperty("languageId").GetString());
		Assert.AreEqual(1, textDocument.GetProperty("version").GetInt32());
		Assert.AreEqual(content, textDocument.GetProperty("text").GetString());
	}

	public TestContext? TestContext { get; set; }
}
