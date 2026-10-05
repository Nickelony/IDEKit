using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Lua.Tests;

/// <summary>Pins the diagnostic mapping rules of the Lua profile by publishing LuaLS diagnostics through it.</summary>
[TestClass]
public sealed class LuaDiagnosticMappingPolicyTests
{
	[TestMethod]
	public async Task BlankMessage_IsShownAsTheLuaFallbackMessage()
	{
		IReadOnlyList<Diagnostic> diagnostics = await MapAsync(
				content: "local value = 1",
				range: new RangePayload(new PositionPayload(0, 0), new PositionPayload(0, 5)),
				message: "   "
			)
			.ConfigureAwait(false);

		Assert.HasCount(1, diagnostics);
		Assert.AreEqual("Unknown Lua diagnostic.", diagnostics[0].Message);
	}

	[TestMethod]
	[DataRow('.')]
	[DataRow(':')]
	[DataRow('\'')]
	[DataRow('"')]
	[DataRow('_')]
	[DataRow('a')]
	[DataRow('7')]
	public async Task WordRule_AcceptsLuaNameCharacters(char character)
	{
		// The rule extends the identifier rule with '.', ':', '\'', and '"', so member accesses and quoted
		// names resolve as one selectable run in the range anchor.
		Diagnostic diagnostic = await MapAnchoredAtTheLastLetterAsync(character).ConfigureAwait(false);

		Assert.AreEqual(0, diagnostic.Range.Offset);
		Assert.AreEqual(3, diagnostic.Range.EndOffset);
	}

	[TestMethod]
	[DataRow(' ')]
	[DataRow('\t')]
	[DataRow('(')]
	[DataRow('-')]
	public async Task WordRule_RejectsNonNameCharacters(char character)
	{
		Diagnostic diagnostic = await MapAnchoredAtTheLastLetterAsync(character).ConfigureAwait(false);

		Assert.AreEqual(2, diagnostic.Range.Offset);
		Assert.AreEqual(3, diagnostic.Range.EndOffset);
	}

	// A range that ends before it starts is malformed, so the diagnostic falls back to the word anchored at its start position: the last
	// letter of "a{character}b", which the word rule extends to the left only when the character belongs to a word.
	private static async Task<Diagnostic> MapAnchoredAtTheLastLetterAsync(char character)
	{
		IReadOnlyList<Diagnostic> diagnostics = await MapAsync(
				content: $"a{character}b",
				range: new RangePayload(new PositionPayload(0, 2), new PositionPayload(0, 1)),
				message: "Warning."
			)
			.ConfigureAwait(false);

		Assert.HasCount(1, diagnostics);

		return diagnostics[0];
	}

	private static async Task<IReadOnlyList<Diagnostic>> MapAsync(string content, RangePayload range, string message)
	{
		string filePath = TestPaths.Script("mapping.lua");

		using var client = new FakeStatefulLanguageServerClient();
		using var provider = LanguageServerFeatureProvider.Create(LuaLanguageServerProfileFactory.Create(), [TestPaths.Root], client);

		provider.OpenDocument(filePath, content);

		// The provider tracks the document at version 1, so the published diagnostics map against its content.
		Assert.IsTrue(await client.WaitForMethodCountAsync("textDocument/didOpen", 1, TestPolling.DefaultTimeout).ConfigureAwait(false));

		client.PublishDiagnostics(
			new PublishDiagnosticsParams(
				new Uri(filePath).AbsoluteUri,
				Version: 1,
				Diagnostics: [new DiagnosticPayload(range, Severity: Lsp.DiagnosticSeverity.Warning, message, Source: null, Code: null)]
			)
		);

		return provider.GetDiagnostics(filePath);
	}
}
