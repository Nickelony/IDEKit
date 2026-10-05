using System.Reflection;
using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;

namespace Nickelony.LanguageServer.Roslyn.Tests;

/// <summary>Covers each shipped language end to end: the provider it configures serves the language through the fake client.</summary>
[TestClass]
public sealed class RoslynLanguageScenarioTests
{
	private const string DidOpenMethod = "textDocument/didOpen";
	private const string Content = "Module M\r\nEnd Module";

	private static readonly string s_workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-roslyn-language-tests-" + Guid.NewGuid().ToString("N"));

	public static string GetLanguageDisplayName(MethodInfo methodInfo, object[] values) => $"{methodInfo.Name} ({values[0]})";

	[TestMethod]
	[DynamicData(
		nameof(TestRoslynLanguages.ShippedLanguageRows),
		typeof(TestRoslynLanguages),
		DynamicDataDisplayName = nameof(GetLanguageDisplayName)
	)]
	public async Task OpenDocument_SendsTheDidOpenPayloadWithTheLanguageId(RoslynLanguage language)
	{
		string filePath = GetFilePath(language);

		using var client = new FakeLanguageServerClient { ConnectionGeneration = 1 };
		using var provider = LanguageServerFeatureProvider.Create(RoslynLanguageServerProfileFactory.Create(language), [s_workspaceRoot], client);

		provider.OpenDocument(filePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync(DidOpenMethod, 1, TestPolling.DefaultTimeout));

		// The document opens with the language's own identifier, which is what makes the Roslyn language server
		// analyze it as that language rather than the other one.
		JsonElement textDocument = client.GetLastNotificationParameters(DidOpenMethod).GetProperty("textDocument");

		Assert.AreEqual(RoslynLanguageDescriptor.For(language).LanguageId, textDocument.GetProperty("languageId").GetString());
		Assert.AreEqual(new Uri(filePath).AbsoluteUri, textDocument.GetProperty("uri").GetString());
	}

	[TestMethod]
	[DynamicData(
		nameof(TestRoslynLanguages.ShippedLanguageRows),
		typeof(TestRoslynLanguages),
		DynamicDataDisplayName = nameof(GetLanguageDisplayName)
	)]
	public async Task MissingServerExecutable_RaisesOnePersistentStartupFailureNamingTheLanguage(RoslynLanguage language)
	{
		using var provider = new LanguageServerFeatureProvider(
			RoslynLanguageServerProfileFactory.Create(language),
			[s_workspaceRoot],
			serverExecutablePath: null
		);

		var failures = new List<StartupFailure>();

		provider.StartupFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

		Assert.IsNull(
			await provider
				.GetHoverAsync(new HoverRequest(GetFilePath(language), Content, hoveredOffset: 0), TestContext.CancellationToken)
				.ConfigureAwait(false)
		);

		Assert.IsFalse(provider.IsAvailable);
		Assert.HasCount(1, failures);
		Assert.IsTrue(failures[0].IsPersistent);

		// The display name is the language's contribution to the failure wording.
		string display = RoslynLanguageDescriptor.For(language).DisplayName;

		Assert.AreEqual(
			$"The Roslyn language server executable is unavailable, so {display} language features are disabled until the provider is recreated with a valid executable path.",
			failures[0].Message
		);
	}

	[TestMethod]
	[DynamicData(
		nameof(TestRoslynLanguages.ShippedLanguageRows),
		typeof(TestRoslynLanguages),
		DynamicDataDisplayName = nameof(GetLanguageDisplayName)
	)]
	public async Task OpenCompleteAndPullDiagnostics_ThroughTheShippedProvider_Succeeds(RoslynLanguage language)
	{
		string filePath = GetFilePath(language);

		using var client = new FakeLanguageServerClient { ConnectionGeneration = 1 };
		using var provider = LanguageServerFeatureProvider.Create(RoslynLanguageServerProfileFactory.Create(language), [s_workspaceRoot], client);

		provider.OpenDocument(filePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync(DidOpenMethod, 1, TestPolling.DefaultTimeout));
		Assert.IsTrue(provider.IsAvailable);

		// Completion and pull diagnostics both flow through the shipped pipeline. The fake client answers no
		// request, so each feature returns its documented empty fallback; the smoke test proves the constructor
		// wired the language, the watch specifications and the client ownership without a throw.
		IReadOnlyList<CompletionItem> completions = await provider
			.GetCompletionItemsAsync(new CompletionRequest(filePath, Content, caretOffset: 0), TestContext.CancellationToken)
			.ConfigureAwait(false);

		Assert.AreEqual(0, completions.Count);

		IReadOnlyList<Diagnostic> diagnostics = provider.GetDiagnostics(filePath);

		Assert.AreEqual(0, diagnostics.Count);
	}

	public TestContext? TestContext { get; set; }

	private static string GetFilePath(RoslynLanguage language) =>
		Path.Combine(s_workspaceRoot, "Document" + RoslynLanguageDescriptor.For(language).WatchSpecifications[0].Filter.TrimStart('*'));
}
