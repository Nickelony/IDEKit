using System.Text.Json;

namespace Nickelony.LanguageServer.CSharp.Tests;

/// <summary>
/// Covers the C# package's language identity through the fake client: the language identifier an opened document
/// is synchronized with, and the display name the unavailable-client contract reports.
/// </summary>
[TestClass]
public sealed class CSharpLanguageServerIntelliSenseProviderTests
{
	private const string DidOpenMethod = "textDocument/didOpen";
	private const string Content = "class C\r\n{\r\n}";

	private static readonly string WorkspaceRoot = Path.Combine(Path.GetTempPath(), "ls-cs-provider-tests-" + Guid.NewGuid().ToString("N"));
	private static readonly string FilePath = Path.Combine(WorkspaceRoot, "Program.cs");

	[TestMethod]
	public async Task OpenDocument_SendsDidOpenPayloadWithCSharpLanguageId()
	{
		using var client = new FakeLanguageServerClient();
		using var provider = new CSharpLanguageServerIntelliSenseProvider([WorkspaceRoot], client);

		provider.OpenDocument(FilePath, Content);

		Assert.IsTrue(await client.WaitForMethodCountAsync(DidOpenMethod, 1, TestPolling.DefaultTimeout));

		// The document opens with the "csharp" language identifier, which is what makes the Roslyn language server
		// analyze it as C# rather than Visual Basic.
		JsonElement textDocument = client.GetLastNotificationParameters(DidOpenMethod).GetProperty("textDocument");

		Assert.AreEqual("csharp", textDocument.GetProperty("languageId").GetString());
		Assert.AreEqual(new Uri(FilePath).AbsoluteUri, textDocument.GetProperty("uri").GetString());
	}

	[TestMethod]
	public async Task MissingServerExecutable_RaisesOnePersistentStartupFailureNamingCSharp()
	{
		using var provider = new CSharpLanguageServerIntelliSenseProvider([WorkspaceRoot], serverExecutablePath: null);
		var failures = new List<LanguageServerStartupFailure>();

		provider.StartupFailed += (_, eventArgs) => failures.Add(eventArgs.Failure);

		Assert.IsNull(await provider.GetHoverAsync(new LanguageServerHoverRequest(FilePath, Content, new TextPosition(0, 0))).ConfigureAwait(false));

		Assert.AreEqual(LanguageServerProviderState.Failed, provider.State);
		Assert.AreEqual(1, failures.Count);
		Assert.IsTrue(failures[0].IsPersistent);

		// The display name is the language package's contribution to the shared failure wording.
		StringAssert.Contains(failures[0].Message, "C#");
	}
}
