using System.Text.Json;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the code-action request through the framework pipeline: the request, the diagnostics context, and the mapped response.</summary>
[TestClass]
public sealed class LanguageServerFeatureProviderCodeActionTests
{
	[TestMethod]
	public async Task GetCodeActionsAsync_SendsTheCodeActionRequestAndMapsTheResponse()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient
		{
			CodeActionResponse = JsonSerializer.SerializeToElement(
				new object[]
				{
					new
					{
						title = "Add semicolon",
						kind = "quickfix",

						edit = new
						{
							changes = new Dictionary<string, object[]>
							{
								[new Uri(filePath).AbsoluteUri] =
								[
									new
									{
										range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 5 } },
										newText = "local!",
									},
								],
							},
						},
					},
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<CodeAction> actions = await provider.GetCodeActionsAsync(
			new CodeActionRequest(filePath, content, content.RangeAt(new TextPositionRange(new TextPosition(0, 6), new TextPosition(0, 11)))),
			TestContext.CancellationToken
		);

		JsonElement parameters = client.GetLastRequestParameters("textDocument/codeAction");

		Assert.AreEqual(
			Nickelony.LanguageServer.Client.DocumentUriConversion.CreateFileUri(filePath),
			parameters.GetProperty("textDocument").GetProperty("uri").GetString()
		);

		Assert.AreEqual(6, parameters.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
		Assert.AreEqual(11, parameters.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
		Assert.AreEqual(0, parameters.GetProperty("context").GetProperty("diagnostics").GetArrayLength());

		Assert.HasCount(1, actions);
		Assert.AreEqual("Add semicolon", actions[0].Title);
		Assert.AreEqual("quickfix", actions[0].Kind);
		Assert.IsFalse(actions[0].IsPreferred);
		Assert.AreEqual("local!", actions[0].Edit.DocumentEdits[0].TextEdits[0].NewText);
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_RangeOnLaterLines_SendsThePositionOfBothOffsets()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		// Offsets 11 and 21 sit behind two CRLF terminators: the third character of the second line and the fourth character of the third.
		const string content = "local a\r\nlocal b\r\nspawn";

		using var client = new FakeStatefulLanguageServerClient { CodeActionResponse = JsonSerializer.SerializeToElement(Array.Empty<object>()) };
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		await provider.GetCodeActionsAsync(new CodeActionRequest(filePath, content, new TextRange(11, 10)), TestContext.CancellationToken);

		JsonElement range = client.GetLastRequestParameters("textDocument/codeAction").GetProperty("range");

		Assert.AreEqual(1, range.GetProperty("start").GetProperty("line").GetInt32());
		Assert.AreEqual(2, range.GetProperty("start").GetProperty("character").GetInt32());
		Assert.AreEqual(2, range.GetProperty("end").GetProperty("line").GetInt32());
		Assert.AreEqual(3, range.GetProperty("end").GetProperty("character").GetInt32());
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_ContextCarriesTheCachedDiagnosticsForTheRange()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1\nprint(value)\n";

		using var client = new FakeStatefulLanguageServerClient { CodeActionResponse = JsonSerializer.SerializeToElement(Array.Empty<object>()) };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.PublishDiagnostics(
			new PublishDiagnosticsParams(
				new Uri(filePath).AbsoluteUri,
				1,

				[
					new DiagnosticPayload(
						Range: new RangePayload(new PositionPayload(0, 6), new PositionPayload(0, 11)),
						Severity: Lsp.DiagnosticSeverity.Warning,
						Message: "Inside range.",
						Source: null,
						Code: null
					),

					new DiagnosticPayload(
						Range: new RangePayload(new PositionPayload(0, 0), new PositionPayload(0, 5)),
						Severity: Lsp.DiagnosticSeverity.Warning,
						Message: "Outside range.",
						Source: null,
						Code: null
					),
				]
			)
		);

		Assert.HasCount(2, provider.GetDiagnostics(filePath));

		await provider.GetCodeActionsAsync(
			new CodeActionRequest(filePath, content, content.RangeAt(new TextPositionRange(new TextPosition(0, 6), new TextPosition(0, 11)))),
			TestContext.CancellationToken
		);

		JsonElement diagnostics = client.GetLastRequestParameters("textDocument/codeAction").GetProperty("context").GetProperty("diagnostics");

		// Only the diagnostic that intersects the requested range belongs to the request context.
		Assert.AreEqual(1, diagnostics.GetArrayLength());

		JsonElement diagnostic = diagnostics[0];

		Assert.AreEqual("Inside range.", diagnostic.GetProperty("message").GetString());
		Assert.AreEqual(2, diagnostic.GetProperty("severity").GetInt32());
		Assert.AreEqual(6, diagnostic.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
		Assert.AreEqual(11, diagnostic.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_DiagnosticContextMapsSeveritySourceAndNumericCode()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1\nprint(value)\n";

		using var client = new FakeStatefulLanguageServerClient { CodeActionResponse = JsonSerializer.SerializeToElement(Array.Empty<object>()) };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.PublishDiagnostics(
			new PublishDiagnosticsParams(
				new Uri(filePath).AbsoluteUri,
				1,

				[
					new DiagnosticPayload(
						Range: new RangePayload(new PositionPayload(0, 6), new PositionPayload(0, 11)),
						Severity: Lsp.DiagnosticSeverity.Information,
						Message: "Information message.",
						Source: "test-server",
						Code: JsonSerializer.SerializeToElement(211)
					),

					new DiagnosticPayload(
						Range: new RangePayload(new PositionPayload(1, 0), new PositionPayload(1, 5)),
						Severity: Lsp.DiagnosticSeverity.Hint,
						Message: "Hint message.",
						Source: null,
						Code: JsonSerializer.SerializeToElement("W211")
					),
				]
			)
		);

		Assert.HasCount(2, provider.GetDiagnostics(filePath));

		await provider.GetCodeActionsAsync(
			new CodeActionRequest(filePath, content, content.RangeAt(new TextPositionRange(new TextPosition(0, 6), new TextPosition(1, 2)))),
			TestContext.CancellationToken
		);

		JsonElement diagnostics = client.GetLastRequestParameters("textDocument/codeAction").GetProperty("context").GetProperty("diagnostics");

		// Both diagnostics strictly overlap the requested range: the information diagnostic sits at
		// (0,6)-(0,11) and the hint at (1,0)-(1,5), while the range extends into the hint's line.
		Assert.AreEqual(2, diagnostics.GetArrayLength());

		JsonElement information = diagnostics[0];

		Assert.AreEqual(3, information.GetProperty("severity").GetInt32());
		Assert.AreEqual("test-server", information.GetProperty("source").GetString());
		Assert.AreEqual(JsonValueKind.Number, information.GetProperty("code").ValueKind);
		Assert.AreEqual(211, information.GetProperty("code").GetInt32());

		JsonElement hint = diagnostics[1];

		Assert.AreEqual(4, hint.GetProperty("severity").GetInt32());
		Assert.AreEqual(JsonValueKind.String, hint.GetProperty("code").ValueKind);
		Assert.AreEqual("W211", hint.GetProperty("code").GetString());
	}

	[TestMethod]
	[DataRow(6, 11, 6, 6, true)] // a caret at the start of the diagnostic
	[DataRow(6, 11, 11, 11, true)] // a caret at the end of the diagnostic, where it rests after typing the flagged word
	[DataRow(6, 11, 8, 8, true)] // a caret inside the diagnostic
	[DataRow(6, 11, 0, 6, true)] // a selection that ends where the diagnostic starts
	[DataRow(6, 11, 11, 15, true)] // a selection that starts where the diagnostic ends
	[DataRow(16, 21, 0, 16, true)] // a selection that ends where a diagnostic on the next line starts
	[DataRow(8, 8, 8, 8, true)] // an empty diagnostic at the caret
	[DataRow(8, 8, 8, 10, true)] // a selection that starts at an empty diagnostic
	[DataRow(6, 11, 5, 5, false)] // a caret before the diagnostic
	[DataRow(6, 11, 12, 12, false)] // a caret after the diagnostic
	[DataRow(6, 11, 0, 5, false)] // a selection that ends before the diagnostic
	[DataRow(6, 11, 12, 15, false)] // a selection that starts after the diagnostic
	[DataRow(8, 8, 7, 7, false)] // a caret next to an empty diagnostic
	public async Task GetCodeActionsAsync_ContextHoldsTheDiagnosticsWhoseRangeIntersectsTheRequestedRange(
		int diagnosticStart,
		int diagnosticEnd,
		int rangeStart,
		int rangeEnd,
		bool expectedInContext
	)
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1\nprint(value)\n";

		using var client = new FakeStatefulLanguageServerClient { CodeActionResponse = JsonSerializer.SerializeToElement(Array.Empty<object>()) };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		var lineMap = TextLineMap.Build(content);

		TextPosition start = lineMap.GetPosition(diagnosticStart);
		TextPosition end = lineMap.GetPosition(diagnosticEnd);

		client.PublishDiagnostics(
			new PublishDiagnosticsParams(
				new Uri(filePath).AbsoluteUri,
				1,

				[
					new DiagnosticPayload(
						Range: new RangePayload(new PositionPayload(start.Line, start.Character), new PositionPayload(end.Line, end.Character)),
						Severity: Lsp.DiagnosticSeverity.Warning,
						Message: "Flagged.",
						Source: "test-server",
						Code: JsonSerializer.SerializeToElement("W300")
					),
				]
			)
		);

		Assert.HasCount(1, provider.GetDiagnostics(filePath));

		// The rule is closed: ranges that touch intersect, so a caret at either end of a diagnostic reaches it.
		await provider.GetCodeActionsAsync(
			new CodeActionRequest(filePath, content, TextRange.FromBounds(rangeStart, rangeEnd)),
			TestContext.CancellationToken
		);

		JsonElement diagnostics = client.GetLastRequestParameters("textDocument/codeAction").GetProperty("context").GetProperty("diagnostics");

		Assert.AreEqual(expectedInContext ? 1 : 0, diagnostics.GetArrayLength());
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_ContextEchoesUnmodeledDiagnosticFields()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1\nprint(value)\n";
		string fileUri = new Uri(filePath).AbsoluteUri;

		using var client = new FakeStatefulLanguageServerClient { CodeActionResponse = JsonSerializer.SerializeToElement(Array.Empty<object>()) };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.PublishDiagnostics(
			new PublishDiagnosticsParams(
				fileUri,
				1,

				[
					new DiagnosticPayload(
						Range: new RangePayload(new PositionPayload(0, 6), new PositionPayload(0, 11)),
						Severity: Lsp.DiagnosticSeverity.Warning,
						Message: "Unused local.",
						Source: "test-server",
						Code: JsonSerializer.SerializeToElement("unused-local"),
						Tags: [DiagnosticTag.Unnecessary],
						CodeDescription: new CodeDescriptionPayload("https://example.com/unused-local"),

						RelatedInformation:
						[
							new DiagnosticRelatedInformationPayload(
								new LocationPayload(fileUri, new RangePayload(new PositionPayload(0, 6), new PositionPayload(0, 11))),
								"Related location."
							),
						],

						Data: JsonSerializer.SerializeToElement(new { code = "unused-local", index = 4 })
					),
				]
			)
		);

		Assert.HasCount(1, provider.GetDiagnostics(filePath));

		await provider.GetCodeActionsAsync(
			new CodeActionRequest(filePath, content, content.RangeAt(new TextPositionRange(new TextPosition(0, 6), new TextPosition(0, 11)))),
			TestContext.CancellationToken
		);

		JsonElement diagnostic = client.GetLastRequestParameters("textDocument/codeAction").GetProperty("context").GetProperty("diagnostics")[0];

		// A server such as LuaLS derives its quick-fix family from the diagnostic's data (and tags), so the request context
		// must echo every unmodeled field instead of rebuilding the payload from the display fields only.
		Assert.AreEqual("unused-local", diagnostic.GetProperty("data").GetProperty("code").GetString());
		Assert.AreEqual(4, diagnostic.GetProperty("data").GetProperty("index").GetInt32());
		Assert.AreEqual(1, diagnostic.GetProperty("tags")[0].GetInt32());
		Assert.AreEqual("https://example.com/unused-local", diagnostic.GetProperty("codeDescription").GetProperty("href").GetString());
		Assert.AreEqual("Related location.", diagnostic.GetProperty("relatedInformation")[0].GetProperty("message").GetString());

		// Only the range is remapped; the rest of the payload is echoed verbatim.
		Assert.AreEqual(6, diagnostic.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
		Assert.AreEqual(11, diagnostic.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_StaleSnapshot_SendsAnEmptyDiagnosticsContext()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string trackedContent = "local value = 1";
		const string editedContent = "local\nvalue = 1";

		using var client = new FakeStatefulLanguageServerClient { CodeActionResponse = JsonSerializer.SerializeToElement(Array.Empty<object>()) };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(filePath, trackedContent);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.PublishDiagnostics(
			new PublishDiagnosticsParams(
				new Uri(filePath).AbsoluteUri,
				1,

				[
					new DiagnosticPayload(
						Range: new RangePayload(new PositionPayload(0, 6), new PositionPayload(0, 11)),
						Severity: Lsp.DiagnosticSeverity.Warning,
						Message: "Snapshot warning.",
						Source: null,
						Code: null
					),
				]
			)
		);

		Assert.HasCount(1, provider.GetDiagnostics(filePath));

		// The caller supplies newer text than the snapshot the diagnostics were parsed against. The
		// request pipeline synchronizes the server to that newer text, so the cached positions would
		// describe a document state the request does not identify; the context stays empty instead
		// of sending the server coordinates for different text.
		await provider.GetCodeActionsAsync(
			new CodeActionRequest(
				filePath,
				editedContent,
				editedContent.RangeAt(new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 5)))
			),

			TestContext.CancellationToken
		);

		JsonElement diagnostics = client.GetLastRequestParameters("textDocument/codeAction").GetProperty("context").GetProperty("diagnostics");

		Assert.AreEqual(0, diagnostics.GetArrayLength());
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_WhenUnsupported_ReturnsEmptyAndSendsNoRequest()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient { SupportsCodeActions = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<CodeAction> actions = await provider.GetCodeActionsAsync(
			new CodeActionRequest(filePath, "local value = 1", TextRange.FromBounds(0, 5)),
			TestContext.CancellationToken
		);

		// The member is capability-gated: without a negotiated code-action provider the request is
		// skipped before the document is synchronized, so no traffic is produced at all.
		Assert.IsEmpty(actions);
		Assert.IsEmpty(client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_InvalidFilePath_ReturnsEmptyWithoutSending()
	{
		string workspaceRoot = TestPaths.Root;

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<CodeAction> actions = await provider.GetCodeActionsAsync(
			new CodeActionRequest(TestPaths.InvalidFile, "local value = 1", default),
			TestContext.CancellationToken
		);

		Assert.IsEmpty(actions);
		Assert.IsEmpty(client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_NullArguments_Throw()
	{
		string workspaceRoot = TestPaths.Root;

		using var client = new FakeStatefulLanguageServerClient();
		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => provider.GetCodeActionsAsync(null!, TestContext.CancellationToken));
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_CommandOnlyActions_AreNotReturned()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		using var client = new FakeStatefulLanguageServerClient
		{
			CodeActionResponse = JsonSerializer.SerializeToElement(
				new object[]
				{
					new
					{
						title = "Disable diagnostic",
						kind = "quickfix",
						command = new { title = "Disable", command = "test.setConfig" },
					},

					new
					{
						title = "Add semicolon",
						kind = "quickfix",

						edit = new
						{
							changes = new Dictionary<string, object[]>
							{
								[new Uri(filePath).AbsoluteUri] =
								[
									new
									{
										range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 0 } },
										newText = ";",
									},
								],
							},
						},
					},
				}
			),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		IReadOnlyList<CodeAction> actions = await provider.GetCodeActionsAsync(
			new CodeActionRequest(filePath, "local value = 1", TextRange.FromBounds(0, 5)),
			TestContext.CancellationToken
		);

		Assert.HasCount(1, actions);
		Assert.AreEqual("Add semicolon", actions[0].Title);
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_ContextPreservesTheNumericDiagnosticCodeKind()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.test");

		const string content = "local value = 1";

		using var client = new FakeStatefulLanguageServerClient { CodeActionResponse = JsonSerializer.SerializeToElement(Array.Empty<object>()) };

		using LanguageServerFeatureProvider provider = TestProviders.Create([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.PublishDiagnostics(
			new PublishDiagnosticsParams(
				new Uri(filePath).AbsoluteUri,
				1,

				[
					new DiagnosticPayload(
						Range: new RangePayload(new PositionPayload(0, 6), new PositionPayload(0, 11)),
						Severity: Lsp.DiagnosticSeverity.Warning,
						Message: "Numeric code warning.",
						Source: null,
						Code: JsonSerializer.SerializeToElement(211)
					),
				]
			)
		);

		Assert.HasCount(1, provider.GetDiagnostics(filePath));

		await provider.GetCodeActionsAsync(
			new CodeActionRequest(filePath, content, content.RangeAt(new TextPositionRange(new TextPosition(0, 6), new TextPosition(0, 11)))),
			TestContext.CancellationToken
		);

		JsonElement diagnostic = client.GetLastRequestParameters("textDocument/codeAction").GetProperty("context").GetProperty("diagnostics")[0];

		// A numeric code round-trips as a JSON number; the string form used for display must not leak
		// onto the wire, because a server matching by code would not recognize it.
		Assert.AreEqual(JsonValueKind.Number, diagnostic.GetProperty("code").ValueKind);
		Assert.AreEqual(211, diagnostic.GetProperty("code").GetInt32());
	}

	public TestContext? TestContext { get; set; }
}
