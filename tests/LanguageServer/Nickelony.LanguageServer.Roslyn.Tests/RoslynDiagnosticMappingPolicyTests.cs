using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.LanguageServer.Roslyn.Tests;

/// <summary>Pins the diagnostic mapping rules of the Roslyn profile by pulling diagnostics through it.</summary>
[TestClass]
public sealed class RoslynDiagnosticMappingPolicyTests
{
	private const string DocumentDiagnosticMethod = "textDocument/diagnostic";

	private static readonly string s_workspaceRoot = Path.Combine(Path.GetTempPath(), "ls-roslyn-mapping-tests-" + Guid.NewGuid().ToString("N"));

	[TestMethod]
	public async Task BlankMessage_IsShownAsTheFallbackMessage()
	{
		IReadOnlyList<Diagnostic> diagnostics = await MapAsync(
				content: "Module M",
				range: new RangePayload(new PositionPayload(0, 0), new PositionPayload(0, 6)),
				message: "   "
			)
			.ConfigureAwait(false);

		Assert.HasCount(1, diagnostics);
		Assert.AreEqual("Unknown diagnostic.", diagnostics[0].Message);
	}

	[TestMethod]
	[DataRow('.')]
	[DataRow('_')]
	[DataRow('a')]
	[DataRow('7')]
	public async Task WordRule_AcceptsNameCharactersAndTheDot(char character)
	{
		// The rule extends the identifier rule with '.', so a dotted member access resolves as one selectable run in the range anchor.
		Diagnostic diagnostic = await MapAnchoredAtTheLastLetterAsync(character).ConfigureAwait(false);

		Assert.AreEqual(0, diagnostic.Range.Offset);
		Assert.AreEqual(3, diagnostic.Range.EndOffset);
	}

	[TestMethod]
	[DataRow(' ')]
	[DataRow('\t')]
	[DataRow(':')]
	[DataRow('\'')]
	[DataRow('"')]
	[DataRow('(')]
	[DataRow('-')]
	public async Task WordRule_RejectsOtherCharacters(char character)
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
		string filePath = Path.Combine(s_workspaceRoot, "Scripts", "Module.vb");

		using var client = new FakeLanguageServerClient { ConnectionGeneration = 1, SupportsPullDiagnostics = true };
		using var provider = LanguageServerFeatureProvider.Create(TestRoslynLanguages.TestProfile, [s_workspaceRoot], client);

		client.SendRequestHandler = (method, _, _) =>
			Task.FromResult<object?>(
				string.Equals(method, DocumentDiagnosticMethod, StringComparison.Ordinal)
					? new DocumentDiagnosticReportResponse(
						DocumentDiagnosticReportResponse.FullKind,
						"r1",
						[new DiagnosticPayload(range, Severity: DiagnosticSeverity.Warning, message, Source: null, Code: null)]
					)
					: null
			);

		provider.OpenDocument(filePath, content);

		// The pulled report flows through the same mapping as a pushed one.
		Assert.IsTrue(
			await TestPolling.ForConditionAsync(() => provider.GetDiagnostics(filePath).Count == 1, TestPolling.DefaultTimeout).ConfigureAwait(false)
		);

		return provider.GetDiagnostics(filePath);
	}
}
