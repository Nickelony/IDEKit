using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.Markup;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the signature-help request through the framework pipeline.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderSignatureHelpTests
{
	[TestMethod]
	public async Task GetSignatureHelpAsync_WithoutContext_SendsAPositionOnlyRequest()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { SignatureHelpResponse = CreateMinimalSignatureHelpResponse() };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		await provider.GetSignatureHelpAsync(new SignatureHelpRequest(filePath, "spawn(", 6), TestContext.CancellationToken);

		JsonElement parameters = client.GetLastRequestParameters("textDocument/signatureHelp");

		// A caller without an editor context issues the position-only request shape.
		Assert.IsFalse(parameters.TryGetProperty("context", out _), parameters.GetRawText());
		Assert.AreEqual(0, parameters.GetProperty("position").GetProperty("line").GetInt32());
		Assert.AreEqual(6, parameters.GetProperty("position").GetProperty("character").GetInt32());
	}

	[TestMethod]
	public async Task GetSignatureHelpAsync_WithTriggerCharacterContext_ForwardsTheTriggerContext()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { SignatureHelpResponse = CreateMinimalSignatureHelpResponse() };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		var context = new SignatureHelpContext(SignatureHelpTriggerKind.TriggerCharacter, "(", isRetrigger: true);

		await provider.GetSignatureHelpAsync(
			new SignatureHelpRequest(documentId: filePath, documentText: "spawn(", caretOffset: 6, context),
			TestContext.CancellationToken
		);

		JsonElement contextElement = client.GetLastRequestParameters("textDocument/signatureHelp").GetProperty("context");

		Assert.AreEqual(2, contextElement.GetProperty("triggerKind").GetInt32());
		Assert.AreEqual("(", contextElement.GetProperty("triggerCharacter").GetString());
		Assert.IsTrue(contextElement.GetProperty("isRetrigger").GetBoolean());

		// No visible payload was supplied, so the retrigger hint carries no active signature help.
		Assert.IsFalse(contextElement.TryGetProperty("activeSignatureHelp", out _));
	}

	[TestMethod]
	public async Task GetSignatureHelpAsync_WithContentChangeContext_OmitsTheTriggerCharacter()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { SignatureHelpResponse = CreateMinimalSignatureHelpResponse() };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		await provider.GetSignatureHelpAsync(
			new SignatureHelpRequest(
				documentId: filePath,
				documentText: "spawn(",
				caretOffset: 6,
				context: new SignatureHelpContext(SignatureHelpTriggerKind.ContentChange)
			),

			TestContext.CancellationToken
		);

		JsonElement contextElement = client.GetLastRequestParameters("textDocument/signatureHelp").GetProperty("context");

		Assert.AreEqual(3, contextElement.GetProperty("triggerKind").GetInt32());
		Assert.IsFalse(contextElement.GetProperty("isRetrigger").GetBoolean());
		Assert.IsFalse(contextElement.TryGetProperty("triggerCharacter", out _), contextElement.GetRawText());
	}

	[TestMethod]
	public async Task GetSignatureHelpAsync_WithActiveSignatureHelp_ForwardsTheVisiblePayload()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { SignatureHelpResponse = CreateMinimalSignatureHelpResponse() };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		var activeSignatureHelp = new SignatureHelpInformation(
			[
				new SignatureInformation(
					"spawn(room, objectName)",
					"Spawns an object.",
					parameters: [new ParameterInformation("room", "Room id."), new ParameterInformation("objectName")]
				),

				new SignatureInformation("spawn(room)"),
			],

			activeSignatureIndex: 0,
			activeParameter: SignatureActiveParameterSelection.At(1)
		);

		var context = new SignatureHelpContext(SignatureHelpTriggerKind.ContentChange, isRetrigger: true, activeSignatureHelp: activeSignatureHelp);

		await provider.GetSignatureHelpAsync(
			new SignatureHelpRequest(documentId: filePath, documentText: "spawn(room, ", caretOffset: 12, context),
			TestContext.CancellationToken
		);

		JsonElement activeElement = client
			.GetLastRequestParameters("textDocument/signatureHelp")
			.GetProperty("context")
			.GetProperty("activeSignatureHelp");

		Assert.AreEqual(0, activeElement.GetProperty("activeSignature").GetInt32());
		Assert.AreEqual(1, activeElement.GetProperty("activeParameter").GetInt32());
		Assert.AreEqual(2, activeElement.GetProperty("signatures").GetArrayLength());

		JsonElement firstSignature = activeElement.GetProperty("signatures")[0];

		Assert.AreEqual("spawn(room, objectName)", firstSignature.GetProperty("label").GetString());

		// The editor model is plain text, so documentation travels as plain-text MarkupContent.
		JsonElement signatureDocumentation = firstSignature.GetProperty("documentation");

		Assert.AreEqual("plaintext", signatureDocumentation.GetProperty("kind").GetString());
		Assert.AreEqual("Spawns an object.", signatureDocumentation.GetProperty("value").GetString());

		// The payload-level index carries the effective selection, so signatures omit their own
		// active-parameter override.
		Assert.IsFalse(firstSignature.TryGetProperty("activeParameter", out _));

		JsonElement firstParameters = firstSignature.GetProperty("parameters");

		Assert.AreEqual(2, firstParameters.GetArrayLength());
		Assert.AreEqual("room", firstParameters[0].GetProperty("label").GetString());
		Assert.AreEqual("plaintext", firstParameters[0].GetProperty("documentation").GetProperty("kind").GetString());
		Assert.AreEqual("Room id.", firstParameters[0].GetProperty("documentation").GetProperty("value").GetString());
		Assert.AreEqual("objectName", firstParameters[1].GetProperty("label").GetString());
		Assert.IsFalse(firstParameters[1].TryGetProperty("documentation", out _));

		// A signature without parameters omits the parameter list entirely.
		Assert.IsFalse(activeElement.GetProperty("signatures")[1].TryGetProperty("parameters", out _));
	}

	[TestMethod]
	public async Task GetSignatureHelpAsync_WithNoActiveParameter_WritesTheNullState()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { SignatureHelpResponse = CreateMinimalSignatureHelpResponse() };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		var activeSignatureHelp = new SignatureHelpInformation(
			[new SignatureInformation("fn(a)", parameters: [new ParameterInformation("a")])],
			activeParameter: SignatureActiveParameterSelection.None
		);

		var context = new SignatureHelpContext(SignatureHelpTriggerKind.ContentChange, activeSignatureHelp: activeSignatureHelp);

		await provider.GetSignatureHelpAsync(
			new SignatureHelpRequest(documentId: filePath, documentText: "fn(", caretOffset: 3, context),
			TestContext.CancellationToken
		);

		JsonElement activeElement = client
			.GetLastRequestParameters("textDocument/signatureHelp")
			.GetProperty("context")
			.GetProperty("activeSignatureHelp");

		// The LSP 3.18 "no active parameter" state is the JSON null form, not an absent property.
		Assert.AreEqual(JsonValueKind.Null, activeElement.GetProperty("activeParameter").ValueKind);
	}

	[TestMethod]
	public async Task GetSignatureHelpAsync_InvalidFilePath_ReturnsNullWithoutSending()
	{
		string workspaceRoot = TestPaths.Root;

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		SignatureHelpInformation? signature = await provider.GetSignatureHelpAsync(
			new SignatureHelpRequest(TestPaths.InvalidFile, "spawn(", 6),
			TestContext.CancellationToken
		);

		Assert.IsNull(signature);
		Assert.IsEmpty(client.GetSentMethodNames());
		Assert.AreEqual(0, client.StartCallCount);
	}

	private static JsonElement CreateMinimalSignatureHelpResponse() =>
		JsonSerializer.SerializeToElement(new { activeSignature = 0, signatures = new[] { new { label = "spawn(room)" } } });

	[TestMethod]
	public async Task GetSignatureHelpAsync_ReturnsParsedSignatureHelpFromTypedResponse()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			SignatureHelpResponse = JsonSerializer.SerializeToElement(
				new
				{
					activeSignature = 0,
					activeParameter = 1,

					signatures = new[]
					{
						new
						{
							label = "spawn(room, objectName)",
							documentation = new { kind = "markdown", value = "Spawns an object." },

							parameters = new object[]
							{
								new { label = new[] { 6, 10 }, documentation = "Room id." },
								new { label = new[] { 12, 22 }, documentation = "Object name." },
							},
						},
					},
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		SignatureHelpInformation? signature = await provider.GetSignatureHelpAsync(new SignatureHelpRequest(filePath, "spawn(", 6));

		Assert.IsNotNull(signature);
		Assert.AreEqual("spawn(room, objectName)", signature.ActiveSignature.Label);
		Assert.AreEqual("Spawns an object.", signature.ActiveSignature.Documentation?.Value);
		Assert.AreEqual(1, signature.ActiveParameterIndex);
		Assert.HasCount(2, signature.ActiveSignature.Parameters);
		Assert.AreEqual("objectName", signature.ActiveSignature.Parameters[1].Label);
		Assert.AreEqual("Object name.", signature.ActiveSignature.Parameters[1].Documentation?.Value);

		Assert.AreSequenceEqual(["textDocument/didOpen", "textDocument/signatureHelp"], client.GetSentMethodNames());
	}

	public TestContext? TestContext { get; set; }
}
