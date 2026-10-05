using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class CodeActionContextConversionTests
{
	private const string Text = "local a = 1\nlocal b = 2";

	private static DiagnosticEntry Entry(TextRange range, string message, Lsp.RangePayload staleRange, string? code = null)
	{
		var diagnostic = new Diagnostic(DiagnosticSeverity.Warning, message, range);

		var payload = new Lsp.DiagnosticPayload(
			Range: staleRange,
			Severity: Lsp.DiagnosticSeverity.Warning,
			message,
			Source: "test",
			Code: code is null ? null : System.Text.Json.JsonSerializer.SerializeToElement(code)
		);

		return new DiagnosticEntry(diagnostic, payload);
	}

	[TestMethod]
	public void ToProtocol_WithoutCachedDiagnostics_ReturnsAnEmptyContext()
	{
		var request = new CodeActionRequest("doc", Text, new TextRange(0, 5));

		Lsp.CodeActionContextPayload context = CodeActionContextConversion.ToProtocol(
			request,
			lineMap: TextLineMap.Build(Text),
			diagnostics: [],
			sourceContent: Text
		);

		Assert.IsEmpty(context.Diagnostics);
	}

	[TestMethod]
	public void ToProtocol_ForCachedDiagnosticsOfOtherText_ReturnsAnEmptyContext()
	{
		var request = new CodeActionRequest("doc", Text, new TextRange(0, 5));
		DiagnosticEntry entry = Entry(new TextRange(0, 5), "warn", new Lsp.RangePayload(new(0, 0), new(0, 5)));

		Lsp.CodeActionContextPayload context = CodeActionContextConversion.ToProtocol(
			request,
			lineMap: TextLineMap.Build(Text),
			diagnostics: [entry],
			sourceContent: "other text"
		);

		Assert.IsEmpty(context.Diagnostics);
	}

	[TestMethod]
	public void ToProtocol_KeepsOnlyTheDiagnosticsThatIntersectTheRequestedRange()
	{
		var request = new CodeActionRequest("doc", Text, new TextRange(0, 5));

		DiagnosticEntry inRange = Entry(new TextRange(2, 2), "inside", new Lsp.RangePayload(new(9, 9), new(9, 9)));
		DiagnosticEntry outOfRange = Entry(new TextRange(14, 3), "outside", new Lsp.RangePayload(new(1, 2), new(1, 5)));

		Lsp.CodeActionContextPayload context = CodeActionContextConversion.ToProtocol(
			request,
			lineMap: TextLineMap.Build(Text),
			diagnostics: [inRange, outOfRange],
			sourceContent: Text
		);

		Assert.HasCount(1, context.Diagnostics);
		Assert.AreEqual("inside", context.Diagnostics[0].Message);
	}

	[TestMethod]
	public void ToProtocol_CarriesTheKindFilterOfTheRequest()
	{
		var plain = new CodeActionRequest("doc", Text, new TextRange(0, 5));

		CodeActionRequest filtered = plain with { Only = ["quickfix", "refactor"] };
		DiagnosticEntry entry = Entry(new TextRange(2, 2), "inside", new Lsp.RangePayload(new(9, 9), new(9, 9)));

		Assert.IsNull(
			CodeActionContextConversion.ToProtocol(request: plain, lineMap: TextLineMap.Build(Text), diagnostics: [], sourceContent: Text).Only
		);

		Assert.AreSequenceEqual(
			["quickfix", "refactor"],
			CodeActionContextConversion.ToProtocol(request: filtered, lineMap: TextLineMap.Build(Text), diagnostics: [], sourceContent: Text).Only
		);

		Assert.AreSequenceEqual(
			["quickfix", "refactor"],

			CodeActionContextConversion
				.ToProtocol(request: filtered, lineMap: TextLineMap.Build(Text), diagnostics: [entry], sourceContent: Text)
				.Only
		);

		Assert.AreSequenceEqual(
			["quickfix", "refactor"],

			CodeActionContextConversion
				.ToProtocol(request: filtered, lineMap: TextLineMap.Build(Text), diagnostics: [entry], sourceContent: "other text")
				.Only
		);
	}

	[TestMethod]
	public void ToProtocol_WithDiagnosticsNamedByTheRequest_SendsThemInsteadOfTheCache()
	{
		var named = new Diagnostic(DiagnosticSeverity.Error, "host says", new TextRange(1, 3)) { Source = "host", Code = "H1" };
		var request = new CodeActionRequest("doc", Text, new TextRange(0, 5)) { Diagnostics = [named] };

		DiagnosticEntry cached = Entry(new TextRange(2, 2), "cached", new Lsp.RangePayload(new(9, 9), new(9, 9)));

		Lsp.CodeActionContextPayload context = CodeActionContextConversion.ToProtocol(
			request,
			lineMap: TextLineMap.Build(Text),
			diagnostics: [cached],
			sourceContent: Text
		);

		Assert.HasCount(1, context.Diagnostics);

		Lsp.DiagnosticPayload sent = context.Diagnostics[0];

		Assert.AreEqual("host says", sent.Message);
		Assert.AreEqual(Lsp.DiagnosticSeverity.Error, sent.Severity);
		Assert.AreEqual("host", sent.Source);
		Assert.AreEqual("H1", sent.Code?.GetString());
		Assert.AreEqual(new Lsp.RangePayload(new(0, 1), new(0, 4)), sent.Range);
	}

	[TestMethod]
	public void ToProtocol_WithAnEmptyDiagnosticListNamedByTheRequest_SendsNoneEvenWhenTheCacheHasSome()
	{
		var request = new CodeActionRequest("doc", Text, new TextRange(0, 5)) { Diagnostics = [] };
		DiagnosticEntry cached = Entry(new TextRange(2, 2), "cached", new Lsp.RangePayload(new(9, 9), new(9, 9)));

		Assert.IsEmpty(
			CodeActionContextConversion.ToProtocol(request, lineMap: TextLineMap.Build(Text), diagnostics: [cached], sourceContent: Text).Diagnostics
		);
	}

	[TestMethod]
	public void ToProtocol_WithANamedDiagnosticTheCacheHolds_EchoesTheServersOwnPayload()
	{
		DiagnosticEntry cached = Entry(
			range: new TextRange(2, 2),
			message: "inside",
			staleRange: new Lsp.RangePayload(new(9, 9), new(9, 9)),
			code: "W100"
		);

		var request = new CodeActionRequest("doc", Text, new TextRange(0, 5)) { Diagnostics = [cached.Diagnostic] };

		Lsp.DiagnosticPayload sent = CodeActionContextConversion
			.ToProtocol(request, lineMap: TextLineMap.Build(Text), diagnostics: [cached], sourceContent: Text)
			.Diagnostics[0];

		Assert.AreEqual(new Lsp.RangePayload(new(0, 2), new(0, 4)), sent.Range);
		Assert.AreEqual("test", sent.Source);
		Assert.AreEqual("W100", sent.Code?.GetString());
	}

	[TestMethod]
	public void ToProtocol_WithANamedDiagnosticTheCacheHoldsForOtherText_BuildsItFromItsOwnFields()
	{
		DiagnosticEntry cached = Entry(
			range: new TextRange(2, 2),
			message: "inside",
			staleRange: new Lsp.RangePayload(new(9, 9), new(9, 9)),
			code: "W100"
		);

		var request = new CodeActionRequest("doc", Text, new TextRange(0, 5)) { Diagnostics = [cached.Diagnostic] };

		Lsp.DiagnosticPayload sent = CodeActionContextConversion
			.ToProtocol(request, lineMap: TextLineMap.Build(Text), diagnostics: [cached], sourceContent: "other text")
			.Diagnostics[0];

		Assert.AreEqual(new Lsp.RangePayload(new(0, 2), new(0, 4)), sent.Range);
		Assert.AreEqual("inside", sent.Message);
		Assert.IsNull(sent.Code);
	}

	[TestMethod]
	public void ToProtocol_WithANamedDiagnosticOfNoSeverity_OmitsTheSeverity()
	{
		var named = new Diagnostic(DiagnosticSeverity.None, "plain", new TextRange(0, 1));
		var request = new CodeActionRequest("doc", Text, new TextRange(0, 5)) { Diagnostics = [named] };

		Assert.IsNull(
			CodeActionContextConversion
				.ToProtocol(request, lineMap: TextLineMap.Build(Text), diagnostics: [], sourceContent: Text)
				.Diagnostics[0]
				.Severity
		);
	}

	[TestMethod]
	public void ToProtocol_RemapsOnlyTheRangeAndEchoesTheServersOwnPayload()
	{
		var request = new CodeActionRequest("doc", Text, new TextRange(0, 5));

		DiagnosticEntry entry = Entry(
			range: new TextRange(2, 2),
			message: "inside",
			staleRange: new Lsp.RangePayload(new(9, 9), new(9, 9)),
			code: "W100"
		);

		Lsp.DiagnosticPayload echoed = CodeActionContextConversion
			.ToProtocol(request, lineMap: TextLineMap.Build(Text), diagnostics: [entry], sourceContent: Text)
			.Diagnostics[0];

		Assert.AreEqual(new Lsp.RangePayload(new(0, 2), new(0, 4)), echoed.Range);
		Assert.AreEqual("test", echoed.Source);
		Assert.AreEqual("W100", echoed.Code?.GetString());
	}
}
