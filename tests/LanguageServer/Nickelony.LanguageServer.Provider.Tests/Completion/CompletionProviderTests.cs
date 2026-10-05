using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class CompletionProviderTests
{
	private const string Document = "local a = 1\nprint(a)";

	private static CompletionProvider CreateProvider(RecordingDocumentRequestPipeline pipeline)
	{
		(ProviderContext context, RequestRunner runner) = TestProviderContext.CreateWithRunner(client: null);
		return new(pipeline, context, runner);
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_SendsAnInvokedContextAtTheCaretPosition()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		await CreateProvider(pipeline)
			.GetCompletionItemsAsync(new CompletionRequest(TestPaths.Script("a.lua"), Document, caretOffset: 18), CancellationToken.None);

		RecordingDocumentRequestPipeline.RecordedRequest request = pipeline.SingleRequest;
		JsonElement context = request.Parameters.GetProperty("context");

		Assert.AreEqual(LspMethodNames.Completion, request.Method);
		Assert.AreEqual(18, request.Offset);
		Assert.AreEqual(new TextPosition(1, 6), request.Position);
		Assert.AreEqual((int)CompletionTriggerKind.Invoked, context.GetProperty("triggerKind").GetInt32());
		Assert.IsFalse(context.TryGetProperty("triggerCharacter", out JsonElement character) && character.ValueKind != JsonValueKind.Null);
		Assert.IsNull(request.SupportsRequest);
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_ForATriggerCharacter_SendsItInTheContext()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		await CreateProvider(pipeline)
			.GetCompletionItemsAsync(
				new CompletionRequest(
					documentId: TestPaths.Script("a.lua"),
					documentText: Document,
					caretOffset: 18,
					context: CompletionContext.ForCharacter('.')
				),

				CancellationToken.None
			);

		JsonElement context = pipeline.SingleRequest.Parameters.GetProperty("context");

		Assert.AreEqual((int)CompletionTriggerKind.TriggerCharacter, context.GetProperty("triggerKind").GetInt32());
		Assert.AreEqual(".", context.GetProperty("triggerCharacter").GetString());
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_ForAHostDefinedTrigger_ReadsAsInvoked()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		await CreateProvider(pipeline)
			.GetCompletionItemsAsync(
				new CompletionRequest(
					documentId: TestPaths.Script("a.lua"),
					documentText: Document,
					caretOffset: 0,
					context: CompletionContext.CreateCustom("Hotkey")
				),

				CancellationToken.None
			);

		Assert.AreEqual(
			(int)CompletionTriggerKind.Invoked,
			pipeline.SingleRequest.Parameters.GetProperty("context").GetProperty("triggerKind").GetInt32()
		);
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_WithoutAResponse_ReturnsAnEmptyList()
	{
		var pipeline = new RecordingDocumentRequestPipeline();

		IReadOnlyList<CompletionItem> items = await CreateProvider(pipeline)
			.GetCompletionItemsAsync(new CompletionRequest(TestPaths.Script("a.lua"), Document, caretOffset: 0), CancellationToken.None);

		Assert.IsEmpty(items);
	}

	[TestMethod]
	public async Task GetCompletionItemsAsync_ParsesTheReturnedItems()
	{
		var pipeline = new RecordingDocumentRequestPipeline
		{
			Response = TestPayloads.Deserialize<CompletionResponse>(new { items = new[] { new { label = "print", kind = 3 } } }),
		};

		IReadOnlyList<CompletionItem> items = await CreateProvider(pipeline)
			.GetCompletionItemsAsync(new CompletionRequest(TestPaths.Script("a.lua"), Document, caretOffset: 0), CancellationToken.None);

		Assert.AreEqual("print", Assert.ContainsSingle(items).Label);
	}
}
