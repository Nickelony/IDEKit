using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class SignatureHelpProviderTests
{
	private const string Document = "print(a, b)";

	[TestMethod]
	public async Task GetSignatureHelpAsync_SendsARequestAtTheCaretPositionWithoutAContext()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		await new SignatureHelpProvider(pipeline).GetSignatureHelpAsync(
			new SignatureHelpRequest(TestPaths.Script("a.lua"), Document, caretOffset: 6),
			CancellationToken.None
		);

		RecordingDocumentRequestPipeline.RecordedRequest request = pipeline.SingleRequest;

		Assert.AreEqual(Lsp.LspMethodNames.SignatureHelp, request.Method);
		Assert.AreEqual(new TextPosition(0, 6), request.Position);
		Assert.IsNull(request.SupportsRequest);
		Assert.IsFalse(request.Parameters.TryGetProperty("context", out JsonElement context) && context.ValueKind != JsonValueKind.Null);
	}

	[TestMethod]
	public async Task GetSignatureHelpAsync_ProjectsTheTriggerContext()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		var context = new SignatureHelpContext(SignatureHelpTriggerKind.TriggerCharacter, triggerCharacter: "(", isRetrigger: true);

		await new SignatureHelpProvider(pipeline).GetSignatureHelpAsync(
			new SignatureHelpRequest(documentId: TestPaths.Script("a.lua"), documentText: Document, caretOffset: 6, context),
			CancellationToken.None
		);

		JsonElement sent = pipeline.SingleRequest.Parameters.GetProperty("context");

		Assert.AreEqual((int)SignatureHelpTriggerKind.TriggerCharacter, sent.GetProperty("triggerKind").GetInt32());
		Assert.AreEqual("(", sent.GetProperty("triggerCharacter").GetString());
		Assert.IsTrue(sent.GetProperty("isRetrigger").GetBoolean());
	}

	[TestMethod]
	public async Task GetSignatureHelpAsync_ForwardsTheActiveSignatureHelpWithAnExplicitNullActiveParameter()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		var active = new SignatureHelpInformation(
			[new SignatureInformation("print(a, b)", parameters: [new ParameterInformation("a"), new ParameterInformation("b")])],
			activeSignatureIndex: 0,
			activeParameter: SignatureActiveParameterSelection.None
		);

		await new SignatureHelpProvider(pipeline).GetSignatureHelpAsync(
			new SignatureHelpRequest(
				documentId: TestPaths.Script("a.lua"),
				documentText: Document,
				caretOffset: 6,
				context: new SignatureHelpContext(SignatureHelpTriggerKind.ContentChange, isRetrigger: true, activeSignatureHelp: active)
			),

			CancellationToken.None
		);

		JsonElement sent = pipeline.SingleRequest.Parameters.GetProperty("context").GetProperty("activeSignatureHelp");

		Assert.AreEqual(0, sent.GetProperty("activeSignature").GetInt32());
		Assert.AreEqual(JsonValueKind.Null, sent.GetProperty("activeParameter").ValueKind);
		Assert.AreEqual("print(a, b)", sent.GetProperty("signatures")[0].GetProperty("label").GetString());
		Assert.AreEqual(2, sent.GetProperty("signatures")[0].GetProperty("parameters").GetArrayLength());
	}

	[TestMethod]
	public async Task GetSignatureHelpAsync_ForwardsTheEffectiveActiveParameterIndex()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		var active = new SignatureHelpInformation(
			[new SignatureInformation("print(a, b)", parameters: [new ParameterInformation("a"), new ParameterInformation("b")])],
			activeSignatureIndex: 0,
			activeParameter: SignatureActiveParameterSelection.At(1)
		);

		await new SignatureHelpProvider(pipeline).GetSignatureHelpAsync(
			new SignatureHelpRequest(
				documentId: TestPaths.Script("a.lua"),
				documentText: Document,
				caretOffset: 9,
				context: new SignatureHelpContext(SignatureHelpTriggerKind.ContentChange, isRetrigger: true, activeSignatureHelp: active)
			),

			CancellationToken.None
		);

		JsonElement sent = pipeline.SingleRequest.Parameters.GetProperty("context").GetProperty("activeSignatureHelp");

		Assert.AreEqual(1, sent.GetProperty("activeParameter").GetInt32());
	}

	[TestMethod]
	public async Task GetSignatureHelpAsync_WithoutAResponse_ReturnsNull()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		SignatureHelpInformation? help = await new SignatureHelpProvider(pipeline).GetSignatureHelpAsync(
			new SignatureHelpRequest(TestPaths.Script("a.lua"), Document, caretOffset: 6),
			CancellationToken.None
		);

		Assert.IsNull(help);
	}
}
