using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the document request pipeline's fallback paths and its success path directly.</summary>
[TestClass]
public sealed class DocumentRequestPipelineTests
{
	private const string Content = "local a = 1";
	private const string Method = "test/documentRequest";

	private static readonly string s_workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-provider-pipeline-" + Guid.NewGuid().ToString("N"));
	private static readonly string s_filePath = Path.Combine(s_workspaceRoot, "Scripts", "test.test");

	private static DocumentRequestDescriptor<string, string> CreateDescriptor(Func<ILanguageServerClient, bool>? supportsRequest = null)
	{
		return new DocumentRequestDescriptor<string, string>(
			Method,
			buildParameters: static (textDocument, position) => new { textDocument.Uri, position.Line },
			parseResponse: static (response, _) => $"parsed:{response}",
			fallbackValue: "fallback",
			supportsRequest
		);
	}

	private static Task<string> SendAsync(
		LanguageServerFeatureProvider provider,
		string filePath,
		DocumentRequestDescriptor<string, string> request,
		CancellationToken cancellationToken = default
	)
	{
		return provider.Composition.RequestPipeline.SendAsync(filePath, documentText: Content, request, cancellationToken);
	}

	[TestMethod]
	public async Task SendAsync_ParsesTheResponseAfterSynchronizingTheDocument()
	{
		using var client = new FakeLanguageServerClient
		{
			IsReady = false,
			SendRequestHandler = (method, _, _) => Task.FromResult<object?>(string.Equals(method, Method, StringComparison.Ordinal) ? "ok" : null),
		};

		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		string result = await SendAsync(provider, s_filePath, CreateDescriptor());

		Assert.AreEqual("parsed:ok", result);
		Assert.AreSequenceEqual(["textDocument/didOpen", Method], client.GetSentMethodNames());
	}

	[TestMethod]
	[DataRow("local a = 1\nlocal b = 2", 0, 0, 0)]
	[DataRow("local a = 1\nlocal b = 2", 18, 1, 6)]
	[DataRow("local a = 1\nlocal b = 2", 999, 1, 11)]
	[DataRow("local a = 1\nlocal b = 2", -7, 0, 0)]
	[DataRow("ab\r\ncd", 3, 0, 2)]
	public async Task SendAsync_ConvertsTheDescriptorOffsetToTheProtocolPositionOfTheDocumentText(string text, int offset, int line, int character)
	{
		using var client = new FakeLanguageServerClient { IsReady = false, SendRequestHandler = (_, _, _) => Task.FromResult<object?>("ok") };

		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		PositionPayload? sent = null;

		var request = new DocumentRequestDescriptor<string, string>(
			Method,

			buildParameters: (textDocument, position) =>
			{
				sent = position;
				return textDocument;
			},

			parseResponse: static (response, _) => response,
			fallbackValue: "fallback",
			offset: offset
		);

		string result = await provider.Composition.RequestPipeline.SendAsync(
			filePath: s_filePath,
			documentText: text,
			request,
			cancellationToken: CancellationToken.None
		);

		// A caret in the middle of a CRLF pair maps to the end of its line, and an offset outside the document clamps to it.
		Assert.AreEqual("ok", result);
		Assert.AreEqual(new PositionPayload(line, character), sent);
	}

	[TestMethod]
	public async Task SendAsync_WithoutAnOffset_SendsTheDefaultPosition()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, SendRequestHandler = (_, _, _) => Task.FromResult<object?>("ok") };

		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		PositionPayload? sent = null;

		var request = new DocumentRequestDescriptor<string, string>(
			Method,

			buildParameters: (textDocument, position) =>
			{
				sent = position;
				return textDocument;
			},

			parseResponse: static (response, _) => response,
			fallbackValue: "fallback"
		);

		await provider.Composition.RequestPipeline.SendAsync(
			filePath: s_filePath,
			documentText: "local a = 1\nlocal b = 2",
			request,
			cancellationToken: CancellationToken.None
		);

		Assert.AreEqual(new PositionPayload(0, 0), sent);
	}

	[TestMethod]
	public async Task SendAsync_HandsTheNormalizedPathAndTheLineMapOfTheDocumentTextToTheResponseParser()
	{
		const string text = "local a = 1\nlocal b = 2";

		using var client = new FakeLanguageServerClient { IsReady = false, SendRequestHandler = (_, _, _) => Task.FromResult<object?>("ok") };

		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		RequestedDocument? handed = null;

		var request = new DocumentRequestDescriptor<string, string>(
			Method,
			buildParameters: static (textDocument, _) => textDocument,

			parseResponse: (response, document) =>
			{
				handed = document;
				return response;
			},

			fallbackValue: "fallback",
			offset: 5
		);

		string unnormalizedPath = Path.Combine(s_workspaceRoot, "Scripts", "..", "Scripts", "test.test");

		await provider.Composition.RequestPipeline.SendAsync(
			filePath: unnormalizedPath,
			documentText: text,
			request,
			cancellationToken: CancellationToken.None
		);

		Assert.IsNotNull(handed);
		Assert.AreEqual(LocalPath.Normalize(s_filePath), handed.NormalizedFilePath);
		Assert.AreEqual(text.Length, handed.LineMap.TextLength);
		Assert.AreEqual(2, handed.LineMap.LineCount);
		Assert.AreSame(handed.LineMap, handed.LineMap);
	}

	[TestMethod]
	public async Task SendAsync_WithoutAClient_ReturnsTheFallbackAndReportsTheMissingClient()
	{
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client: null);

		string result = await SendAsync(provider, s_filePath, CreateDescriptor());

		Assert.AreEqual("fallback", result);
		Assert.AreEqual(LanguageServerFeatureProviderState.Failed, provider.State);
	}

	[TestMethod]
	public async Task SendAsync_WithAnUnusablePath_ReturnsTheFallbackWithoutTraffic()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		string result = await SendAsync(provider, "relative.lua", CreateDescriptor());

		Assert.AreEqual("fallback", result);
		Assert.IsEmpty(client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task SendAsync_WhenTheCapabilityGateRejects_ReturnsTheFallbackWithoutSynchronizing()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		string result = await SendAsync(provider, s_filePath, CreateDescriptor(static _ => false));

		Assert.AreEqual("fallback", result);
		Assert.IsEmpty(client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task SendAsync_WhenTheServerAnswersNothing_ReturnsTheFallback()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);

		string result = await SendAsync(provider, s_filePath, CreateDescriptor());

		Assert.AreEqual("fallback", result);
		Assert.Contains(Method, client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task SendAsync_WithACanceledToken_ThrowsBeforeTouchingTheServer()
	{
		using var client = new FakeLanguageServerClient { IsReady = false };
		using LanguageServerFeatureProvider provider = TestProviders.Create([s_workspaceRoot], client);
		using var canceled = new CancellationTokenSource();

		await canceled.CancelAsync();

		await Assert.ThrowsAsync<OperationCanceledException>(() =>
			SendAsync(provider, filePath: s_filePath, request: CreateDescriptor(), cancellationToken: canceled.Token)
		);

		Assert.IsEmpty(client.GetSentMethodNames());
	}
}
