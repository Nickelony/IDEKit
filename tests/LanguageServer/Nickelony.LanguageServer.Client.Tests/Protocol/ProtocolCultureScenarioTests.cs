using System.Globalization;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Covers that the converters and parsers of the client that read or write numbers produce the wire text of the invariant culture under cultures
/// whose number formatting differs from it.
/// </summary>
[TestClass]
public sealed class ProtocolCultureScenarioTests
{
	// A decimal comma, a decimal separator and a negative sign of their own script, a minus sign that is not the hyphen, and a culture whose
	// case mapping differs from the invariant one.
	private static readonly string[] s_cultureNames = ["de-DE", "fa-IR", "ar-SA", "sv-SE", "tr-TR"];

	private static readonly string s_range = """{"start":{"line":1000000,"character":0},"end":{"line":2147483647,"character":1234567}}""";

	private static readonly IReadOnlyList<Scenario> s_scenarios =
	[
		new("RangePayload", static () => RoundTrip<RangePayload>(s_range), s_range),
		new("PublishDiagnosticsParams", static () => RoundTrip<PublishDiagnosticsParams>(PublishDiagnosticsJson), PublishDiagnosticsJson),
		new("DocumentSymbolResponse", static () => RoundTrip<DocumentSymbolResponse>(DocumentSymbolJson), DocumentSymbolJson),
		new("SignatureHelpResponse", static () => RoundTrip<SignatureHelpResponse>(SignatureHelpJson), SignatureHelpJson),

		new(
			"TextDocumentSyncCapability",

			static () =>
				RoundTrip<TextDocumentSyncCapability>("2") + "|" + RoundTrip<TextDocumentSyncCapability>("""{"openClose":true,"change":1}"""),

			"""2|1"""
		),

		new("CompletionResponse", static () => RoundTrip<CompletionResponse>(CompletionJson), CompletionJson),

		new(
			"SemanticTokensResponse",
			static () => RoundTrip<SemanticTokensResponse>("""{"data":[2147483647,0,12,-7,255]}"""),
			"""{"data":[2147483647,0,12,-7,255]}"""
		),

		new(
			"DidChangeTextDocumentParams",

			static () =>
				WireJsonTestHelpers.Serialize(
					new DidChangeTextDocumentParams(
						new VersionedTextDocumentIdentifier("file:///C:/Workspace/a.lua", int.MaxValue),
						[new TextDocumentContentChangeEventPayload("x", WireJsonTestHelpers.Deserialize<RangePayload>(s_range))]
					)
				),

			$$"""{"textDocument":{"uri":"file:///C:/Workspace/a.lua","version":2147483647},"contentChanges":[{"text":"x","range":{{s_range}}}]}"""
		),

		new(
			"PayloadOptions",

			static () =>
				JsonSerializer.Serialize(
					new
					{
						ratio = 0.1,
						exponent = -2.5e-3,
						large = 1e21,
						count = -1234567,
						whole = 12345678901234L,
					},

					ProtocolSerialization.ForPayloads()
				),

			"""{"ratio":0.1,"exponent":-0.0025,"large":1E+21,"count":-1234567,"whole":12345678901234}"""
		),

		new(
			"ProgressParamsParser",

			static () =>
			{
				using var document = JsonDocument.Parse("""{"token":-123456789012,"value":{"kind":"report","message":"m","percentage":99}}""");

				Assert.IsTrue(ProgressParamsParser.TryParse(document.RootElement, out ProgressParams? progress));

				return string.Join("|", progress.Token, progress.Value.Kind, progress.Value.Percentage?.ToString(CultureInfo.InvariantCulture));
			},

			"-123456789012|report|99"
		),
	];

	private const string PublishDiagnosticsJson =
		"""{"uri":"file:///C:/Workspace/a.lua","version":-1234567,"diagnostics":[{"range":{"start":{"line":1000000,"character":2},"end":{"line":1000000,"character":1234567}},"severity":2,"message":"m","code":-1234567,"data":{"ratio":0.1,"exponent":-2.5e-3,"large":1E+21,"wide":123456789012345678901234567890,"signedZero":-0.0,"whole":12345678901}}]}""";

	private const string DocumentSymbolJson =
		"""[{"name":"f","kind":12,"tags":[1],"range":{"start":{"line":1000000,"character":0},"end":{"line":2147483647,"character":1234567}},"selectionRange":{"start":{"line":1000000,"character":9},"end":{"line":1000000,"character":10}}}]""";

	private const string SignatureHelpJson =
		"""{"activeSignature":7,"activeParameter":1234567,"signatures":[{"label":"f(a, b)","activeParameter":2147483647,"parameters":[{"label":[2,3]},{"label":[5,6]}]}]}""";

	private const string CompletionJson =
		"""{"isIncomplete":false,"items":[{"label":"f","kind":3,"insertTextFormat":2,"sortText":"1","textEdit":{"newText":"x","range":{"start":{"line":1000000,"character":2},"end":{"line":1000000,"character":1234567}}}}]}""";

	public static IEnumerable<object[]> ScenarioCultures()
	{
		foreach (Scenario scenario in s_scenarios)
		{
			foreach (string cultureName in s_cultureNames)
				yield return [scenario.Name, cultureName];
		}
	}

	public static string GetScenarioDisplayName(System.Reflection.MethodInfo methodInfo, object[] values) =>
		$"{methodInfo.Name} ({values[0]}, {values[1]})";

	[TestMethod]
	[DataRow("de-DE")]
	[DataRow("fa-IR")]
	[DataRow("ar-SA")]
	[DataRow("sv-SE")]
	[DataRow("tr-TR")]
	public void TheChosenCulture_FormatsNumbersDifferentlyFromTheInvariantCulture(string cultureName)
	{
		var culture = CultureInfo.GetCultureInfo(cultureName);

		const double Sample = -1234567.5;

		Assert.AreNotEqual(Sample.ToString(CultureInfo.InvariantCulture), Sample.ToString(culture));
	}

	[TestMethod]
	public void EveryScenario_UnderTheInvariantCulture_ProducesItsWireText()
	{
		foreach (Scenario scenario in s_scenarios)
			Assert.AreEqual(scenario.Expected, RunUnderCulture(CultureInfo.InvariantCulture, scenario), scenario.Name);
	}

	[TestMethod]
	[DynamicData(nameof(ScenarioCultures), DynamicDataDisplayName = nameof(GetScenarioDisplayName))]
	public void Scenario_UnderANonInvariantCulture_ProducesTheWireTextOfTheInvariantCulture(string scenarioName, string cultureName)
	{
		Scenario scenario = s_scenarios.Single(candidate => candidate.Name == scenarioName);
		Assert.AreEqual(scenario.Expected, RunUnderCulture(CultureInfo.GetCultureInfo(cultureName), scenario));
	}

	private static string RoundTrip<T>(string json) => WireJsonTestHelpers.Serialize(WireJsonTestHelpers.Deserialize<T>(json));

	// The test thread owns the culture for the duration of the conversion and gets its own back, so no other test sees the change.
	private static string RunUnderCulture(CultureInfo culture, Scenario scenario)
	{
		CultureInfo originalCulture = CultureInfo.CurrentCulture;
		CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;

		try
		{
			CultureInfo.CurrentCulture = culture;
			CultureInfo.CurrentUICulture = culture;

			return scenario.Convert();
		}
		finally
		{
			CultureInfo.CurrentCulture = originalCulture;
			CultureInfo.CurrentUICulture = originalUiCulture;
		}
	}

	private sealed record Scenario(string Name, Func<string> Convert, string Expected);
}
