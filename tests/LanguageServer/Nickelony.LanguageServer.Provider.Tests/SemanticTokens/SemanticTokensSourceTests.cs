using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Covers the semantic-tokens source's refresh, read and announce paths directly, without a provider.</summary>
[TestClass]
public sealed class SemanticTokensSourceTests
{
	private const string Content = "local a = 1";

	private static readonly TimeSpan s_waitTimeout = TimeSpan.FromSeconds(5);

	private static SemanticTokensSource CreateSource(SynchronizerTestHost fixture) =>
		new(context: fixture.Context, requestRunner: fixture.Runner, scheduler: fixture.Scheduler, events: fixture.Events);

	private static async Task<DocumentSnapshot> OpenAsync(SynchronizerTestHost fixture, string filePath)
	{
		fixture.Synchronizer.Open(filePath, Content);

		Assert.IsTrue(await fixture.Client.WaitForMethodCountAsync("textDocument/didOpen", 1, s_waitTimeout));

		return fixture.Documents.GetDocumentSnapshot(filePath) ?? throw new InvalidOperationException("The opened document should be tracked.");
	}

	[TestMethod]
	public async Task Trigger_DecodesTheTokensStoresThemAndRaisesTheEvent()
	{
		using var client = new FakeLanguageServerClient
		{
			IsReady = false,
			SupportsSemanticTokensFull = true,
			SemanticTokenTypes = ["keyword"],

			SendRequestHandler = (method, _, _) =>
				Task.FromResult<object?>(
					string.Equals(method, LspMethodNames.SemanticTokensFull, StringComparison.Ordinal)
						? new SemanticTokensResponse([0, 0, 5, 0, 0])
						: null
				),
		};

		using var fixture = new SynchronizerTestHost(client);
		SemanticTokensSource source = CreateSource(fixture);
		var raised = new List<SemanticTokensUpdatedEventArgs>();

		source.SemanticTokensUpdated += (_, eventArgs) =>
		{
			lock (raised)
				raised.Add(eventArgs);
		};

		string filePath = TestPaths.Script("a.lua");
		DocumentSnapshot document = await OpenAsync(fixture, filePath);

		source.Trigger(document);

		Assert.IsTrue(await TestPolling.ForConditionAsync(() => raised.Count == 1, s_waitTimeout));

		SemanticToken token = Assert.ContainsSingle(raised[0].SemanticTokens);

		Assert.AreEqual(filePath, raised[0].DocumentId);
		Assert.AreEqual(new TextRange(0, 5), token.Range);
		Assert.AreEqual("keyword", token.Type);
		Assert.AreEqual(token, Assert.ContainsSingle(fixture.Documents.GetSemanticTokens(filePath)));
	}

	[TestMethod]
	public async Task Trigger_OnAServerWithoutSemanticTokenSupport_SendsNoRequest()
	{
		using var fixture = new SynchronizerTestHost();

		SemanticTokensSource source = CreateSource(fixture);
		DocumentSnapshot document = await OpenAsync(fixture, TestPaths.Script("a.lua"));

		// The refresh decides on the negotiated capability before its first await.
		source.Trigger(document);

		Assert.DoesNotContain(LspMethodNames.SemanticTokensFull, fixture.Client.GetSentMethodNames());
	}

	[TestMethod]
	public async Task Trigger_OnAServerWhoseLegendIsEmpty_SendsNoRequest()
	{
		using var client = new FakeLanguageServerClient { IsReady = false, SupportsSemanticTokensFull = true };
		using var fixture = new SynchronizerTestHost(client);

		SemanticTokensSource source = CreateSource(fixture);
		DocumentSnapshot document = await OpenAsync(fixture, TestPaths.Script("a.lua"));

		// The refresh decides on the negotiated legend before its first await.
		source.Trigger(document);

		Assert.DoesNotContain(LspMethodNames.SemanticTokensFull, client.GetSentMethodNames());
	}

	[TestMethod]
	public void GetSemanticTokens_ForAnUnusablePathOrAClosedProvider_ReturnsAnEmptyList()
	{
		using var fixture = new SynchronizerTestHost();
		SemanticTokensSource source = CreateSource(fixture);

		Assert.IsEmpty(source.GetSemanticTokens("relative.lua"));

		fixture.Disposal.Signal();

		Assert.IsEmpty(source.GetSemanticTokens(TestPaths.Script("a.lua")));
	}

	[TestMethod]
	public void AnnounceRenamed_RaisesTheTokensCachedForTheNewPath()
	{
		using var fixture = new SynchronizerTestHost();
		SemanticTokensSource source = CreateSource(fixture);
		var raised = new List<SemanticTokensUpdatedEventArgs>();

		source.SemanticTokensUpdated += (_, eventArgs) => raised.Add(eventArgs);

		string newPath = TestPaths.Script("new.lua");

		source.AnnounceRenamed(newPath);

		SemanticTokensUpdatedEventArgs eventArguments = Assert.ContainsSingle(raised);

		Assert.AreEqual(newPath, eventArguments.DocumentId);
		Assert.IsEmpty(eventArguments.SemanticTokens);
	}
}
