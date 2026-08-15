using System.Text.Json;

namespace Nickelony.LanguageServer.Lua.Tests;

public sealed partial class LuaLanguageServerIntelliSenseProviderTests
{
	[TestMethod]
	public async Task GetCodeActionsAsync_SendsTheCodeActionRequestAndMapsTheResponse()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			CodeActionsResponse = JsonSerializer.SerializeToElement(new object[]
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
									range = new
									{
										start = new { line = 0, character = 0 },
										end = new { line = 0, character = 5 }
									},
									newText = "local!"
								}
							]
						}
					}
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCodeAction> actions = await provider.GetCodeActionsAsync(
			new LanguageServerCodeActionRequest(filePath, content, new TextPositionRange(new TextPosition(0, 6), new TextPosition(0, 11))));

		JsonElement parameters = client.GetLastRequestParameters("textDocument/codeAction");

		Assert.AreEqual(
			Nickelony.LanguageServer.Client.LanguageServerPaths.CreateFileUri(filePath),
			parameters.GetProperty("textDocument").GetProperty("uri").GetString());
		Assert.AreEqual(6, parameters.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
		Assert.AreEqual(11, parameters.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
		Assert.AreEqual(0, parameters.GetProperty("context").GetProperty("diagnostics").GetArrayLength());

		Assert.AreEqual(1, actions.Count);
		Assert.AreEqual("Add semicolon", actions[0].Title);
		Assert.AreEqual("quickfix", actions[0].Kind);
		Assert.IsFalse(actions[0].IsPreferred);
		Assert.AreEqual("local!", actions[0].Edit.DocumentEdits[0].TextEdits[0].NewText);
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_ContextCarriesTheCachedDiagnosticsForTheRange()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1\nprint(value)\n";

		using var client = new FakeLanguageServerClient
		{
			CodeActionsResponse = JsonSerializer.SerializeToElement(Array.Empty<object>())
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.PublishDiagnostics(new PublishDiagnosticsParams(
			new Uri(filePath).AbsoluteUri,
			1,
			[
				new DiagnosticPayload(
					new ProtocolRangePayload(new ProtocolPosition(0, 6), new ProtocolPosition(0, 11)),
					DiagnosticSeverity.Warning,
					"Inside range.",
					null,
					null),
				new DiagnosticPayload(
					new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 5)),
					DiagnosticSeverity.Warning,
					"Outside range.",
					null,
					null)
			]));

		Assert.AreEqual(2, provider.GetDiagnostics(filePath).Count);

		await provider.GetCodeActionsAsync(new LanguageServerCodeActionRequest(
			filePath, content, new TextPositionRange(new TextPosition(0, 6), new TextPosition(0, 11))));

		JsonElement diagnostics = client.GetLastRequestParameters("textDocument/codeAction")
			.GetProperty("context")
			.GetProperty("diagnostics");

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
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1\nprint(value)\n";

		using var client = new FakeLanguageServerClient
		{
			CodeActionsResponse = JsonSerializer.SerializeToElement(Array.Empty<object>())
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.PublishDiagnostics(new PublishDiagnosticsParams(
			new Uri(filePath).AbsoluteUri,
			1,
			[
				new DiagnosticPayload(
					new ProtocolRangePayload(new ProtocolPosition(0, 6), new ProtocolPosition(0, 11)),
					DiagnosticSeverity.Information,
					"Information message.",
					"LuaLS",
					JsonSerializer.SerializeToElement(211)),
				new DiagnosticPayload(
					new ProtocolRangePayload(new ProtocolPosition(1, 0), new ProtocolPosition(1, 5)),
					DiagnosticSeverity.Hint,
					"Hint message.",
					null,
					JsonSerializer.SerializeToElement("W211"))
			]));

		Assert.AreEqual(2, provider.GetDiagnostics(filePath).Count);

		await provider.GetCodeActionsAsync(new LanguageServerCodeActionRequest(
			filePath, content, new TextPositionRange(new TextPosition(0, 6), new TextPosition(1, 2))));

		JsonElement diagnostics = client.GetLastRequestParameters("textDocument/codeAction")
			.GetProperty("context")
			.GetProperty("diagnostics");

		// Both diagnostics strictly overlap the requested range: the information diagnostic sits at
		// (0,6)-(0,11) and the hint at (1,0)-(1,5), while the range now extends into the hint's line.
		Assert.AreEqual(2, diagnostics.GetArrayLength());

		JsonElement information = diagnostics[0];
		Assert.AreEqual(3, information.GetProperty("severity").GetInt32());
		Assert.AreEqual("LuaLS", information.GetProperty("source").GetString());
		Assert.AreEqual(JsonValueKind.Number, information.GetProperty("code").ValueKind);
		Assert.AreEqual(211, information.GetProperty("code").GetInt32());

		JsonElement hint = diagnostics[1];
		Assert.AreEqual(4, hint.GetProperty("severity").GetInt32());
		Assert.AreEqual(JsonValueKind.String, hint.GetProperty("code").ValueKind);
		Assert.AreEqual("W211", hint.GetProperty("code").GetString());
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_DiagnosticTouchingRangeBoundary_IsExcluded()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1\nprint(value)\n";

		using var client = new FakeLanguageServerClient
		{
			CodeActionsResponse = JsonSerializer.SerializeToElement(Array.Empty<object>())
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.PublishDiagnostics(new PublishDiagnosticsParams(
			new Uri(filePath).AbsoluteUri,
			1,
			[new DiagnosticPayload(
				new ProtocolRangePayload(new ProtocolPosition(1, 0), new ProtocolPosition(1, 5)),
				DiagnosticSeverity.Warning,
				"After the range.",
				"LuaLS",
				JsonSerializer.SerializeToElement("W300"))]));

		Assert.AreEqual(1, provider.GetDiagnostics(filePath).Count);

		// LSP ranges are half-open: the requested range ends exactly where the diagnostic starts, so the
		// diagnostic does not intersect it and the context stays empty.
		await provider.GetCodeActionsAsync(new LanguageServerCodeActionRequest(
			filePath, content, new TextPositionRange(new TextPosition(0, 0), new TextPosition(1, 0))));

		JsonElement diagnostics = client.GetLastRequestParameters("textDocument/codeAction")
			.GetProperty("context")
			.GetProperty("diagnostics");

		Assert.AreEqual(0, diagnostics.GetArrayLength());
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_ContextEchoesUnmodeledDiagnosticFields()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1\nprint(value)\n";
		string fileUri = new Uri(filePath).AbsoluteUri;

		using var client = new FakeLanguageServerClient
		{
			CodeActionsResponse = JsonSerializer.SerializeToElement(Array.Empty<object>())
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.PublishDiagnostics(new PublishDiagnosticsParams(
			fileUri,
			1,
			[
				new DiagnosticPayload(
					new ProtocolRangePayload(new ProtocolPosition(0, 6), new ProtocolPosition(0, 11)),
					DiagnosticSeverity.Warning,
					"Unused local.",
					"LuaLS",
					JsonSerializer.SerializeToElement("unused-local"),
					Tags: [DiagnosticTag.Unnecessary],
					CodeDescription: new DiagnosticCodeDescriptionPayload("https://example.com/unused-local"),
					RelatedInformation:
					[
						new DiagnosticRelatedInformationPayload(
							new ProtocolLocationPayload(fileUri,
								new ProtocolRangePayload(new ProtocolPosition(0, 6), new ProtocolPosition(0, 11))),
							"Related location.")
					],
					Data: JsonSerializer.SerializeToElement(new { code = "unused-local", index = 4 }))
			]));

		Assert.AreEqual(1, provider.GetDiagnostics(filePath).Count);

		await provider.GetCodeActionsAsync(new LanguageServerCodeActionRequest(
			filePath, content, new TextPositionRange(new TextPosition(0, 6), new TextPosition(0, 11))));

		JsonElement diagnostic = client.GetLastRequestParameters("textDocument/codeAction")
			.GetProperty("context")
			.GetProperty("diagnostics")[0];

		// LuaLS derives its quick-fix family from the diagnostic's data (and tags), so the request context
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
		string filePath = TestPaths.Script("test.lua");
		const string trackedContent = "local value = 1";
		const string editedContent = "local\nvalue = 1";

		using var client = new FakeLanguageServerClient
		{
			CodeActionsResponse = JsonSerializer.SerializeToElement(Array.Empty<object>())
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, trackedContent);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.PublishDiagnostics(new PublishDiagnosticsParams(
			new Uri(filePath).AbsoluteUri,
			1,
			[
				new DiagnosticPayload(
					new ProtocolRangePayload(new ProtocolPosition(0, 6), new ProtocolPosition(0, 11)),
					DiagnosticSeverity.Warning,
					"Snapshot warning.",
					null,
					null)
			]));

		Assert.AreEqual(1, provider.GetDiagnostics(filePath).Count);

		// The caller supplies newer text than the snapshot the diagnostics were parsed against. The
		// request pipeline synchronizes the server to that newer text, so the cached positions would
		// describe a document state the request no longer identifies; the context stays empty instead
		// of sending the server coordinates for different text.
		await provider.GetCodeActionsAsync(new LanguageServerCodeActionRequest(
			filePath, editedContent, new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 5))));

		JsonElement diagnostics = client.GetLastRequestParameters("textDocument/codeAction")
			.GetProperty("context")
			.GetProperty("diagnostics");

		Assert.AreEqual(0, diagnostics.GetArrayLength());
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_ReversedRange_ProducesAnEmptyDiagnosticsContext()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			CodeActionsResponse = JsonSerializer.SerializeToElement(Array.Empty<object>())
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.PublishDiagnostics(CreateDiagnostics(filePath, 1, 6, 11, "Current warning."));
		Assert.AreEqual(1, provider.GetDiagnostics(filePath).Count);

		// A reversed range cannot be mapped to offsets, so the request context stays empty instead of
		// guessing which diagnostics were meant.
		await provider.GetCodeActionsAsync(new LanguageServerCodeActionRequest(
			filePath, content, new TextPositionRange(new TextPosition(0, 11), new TextPosition(0, 6))));

		JsonElement diagnostics = client.GetLastRequestParameters("textDocument/codeAction")
			.GetProperty("context")
			.GetProperty("diagnostics");

		Assert.AreEqual(0, diagnostics.GetArrayLength());
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_WhenUnsupported_ReturnsEmptyAndSendsNoRequest()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient { SupportsCodeActions = false };
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCodeAction> actions = await provider.GetCodeActionsAsync(
			new LanguageServerCodeActionRequest(filePath, "local value = 1", new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 5))));

		// The member is capability-gated: without a negotiated code-action provider the request is
		// skipped before the document is synchronized, so no traffic is produced at all.
		Assert.AreEqual(0, actions.Count);
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_InvalidFilePath_ReturnsEmptyWithoutSending()
	{
		string workspaceRoot = TestPaths.Root;

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCodeAction> actions = await provider.GetCodeActionsAsync(
			new LanguageServerCodeActionRequest("   ", "local value = 1", default));

		Assert.AreEqual(0, actions.Count);
		Assert.AreEqual(0, client.GetSentMethodNames().Length);
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_NullArguments_Throw()
	{
		string workspaceRoot = TestPaths.Root;

		using var client = new FakeLanguageServerClient();
		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(
			() => provider.GetCodeActionsAsync(null!));
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_CommandOnlyActions_AreNotReturned()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");

		using var client = new FakeLanguageServerClient
		{
			CodeActionsResponse = JsonSerializer.SerializeToElement(new object[]
			{
				new
				{
					title = "Disable diagnostic",
					kind = "quickfix",
					command = new { title = "Disable", command = "lua.setConfig" }
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
									range = new
									{
										start = new { line = 0, character = 0 },
										end = new { line = 0, character = 0 }
									},
									newText = ";"
								}
							]
						}
					}
				}
			})
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		IReadOnlyList<TextCodeAction> actions = await provider.GetCodeActionsAsync(
			new LanguageServerCodeActionRequest(filePath, "local value = 1", new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 5))));

		Assert.AreEqual(1, actions.Count);
		Assert.AreEqual("Add semicolon", actions[0].Title);
	}

	[TestMethod]
	public async Task GetCodeActionsAsync_ContextPreservesTheNumericDiagnosticCodeKind()
	{
		string workspaceRoot = TestPaths.Root;
		string filePath = TestPaths.Script("test.lua");
		const string content = "local value = 1";

		using var client = new FakeLanguageServerClient
		{
			CodeActionsResponse = JsonSerializer.SerializeToElement(Array.Empty<object>())
		};

		using var provider = new LuaLanguageServerIntelliSenseProvider([workspaceRoot], client);

		provider.OpenDocument(filePath, content);
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout));

		client.PublishDiagnostics(new PublishDiagnosticsParams(
			new Uri(filePath).AbsoluteUri,
			1,
			[
				new DiagnosticPayload(
					new ProtocolRangePayload(new ProtocolPosition(0, 6), new ProtocolPosition(0, 11)),
					DiagnosticSeverity.Warning,
					"Numeric code warning.",
					null,
					JsonSerializer.SerializeToElement(211))
			]));

		Assert.AreEqual(1, provider.GetDiagnostics(filePath).Count);

		await provider.GetCodeActionsAsync(new LanguageServerCodeActionRequest(
			filePath, content, new TextPositionRange(new TextPosition(0, 6), new TextPosition(0, 11))));

		JsonElement diagnostic = client.GetLastRequestParameters("textDocument/codeAction")
			.GetProperty("context")
			.GetProperty("diagnostics")[0];

		// A numeric code round-trips as a JSON number; the string form used for display must not leak
		// onto the wire, because a server matching by code would no longer recognize it.
		Assert.AreEqual(JsonValueKind.Number, diagnostic.GetProperty("code").ValueKind);
		Assert.AreEqual(211, diagnostic.GetProperty("code").GetInt32());
	}
}
