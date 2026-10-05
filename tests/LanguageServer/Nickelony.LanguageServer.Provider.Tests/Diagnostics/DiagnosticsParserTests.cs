using System.Text.Json;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class DiagnosticsParserTests
{
	// The parser carries the path through to ParsedDiagnostics without normalizing it, so a plain literal
	// is sufficient here.
	private const string FilePath = "test.lua";

	[TestMethod]
	public void TryParse_PreservesZeroWidthDiagnosticOnEmptyLine()
	{
		const string content = "local value = 1\n\nnextLine = 2";

		bool parsed = DiagnosticsParser.TryParse(
			parameters: CreateDiagnostics(line: 1, startCharacter: 0, endLine: 1, endCharacter: 0),
			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out ParsedDiagnostics? publishedDiagnostics
		);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.HasCount(1, publishedDiagnostics.Diagnostics);

		// A zero-width diagnostic keeps its exact position (line 1, character 0) instead of being
		// anchored to visible text; widening an empty range is the rendering layer's concern.
		Assert.AreEqual(content.IndexOf("nextLine", StringComparison.Ordinal) - 1, publishedDiagnostics.Diagnostics[0].Range.Offset);
		Assert.AreEqual(publishedDiagnostics.Diagnostics[0].Range.Offset, publishedDiagnostics.Diagnostics[0].Range.EndOffset);
	}

	[TestMethod]
	public void TryParse_PreservesZeroWidthDiagnosticOnTrailingEmptyLine()
	{
		const string content = "return value\n";

		bool parsed = DiagnosticsParser.TryParse(
			parameters: CreateDiagnostics(line: 1, startCharacter: 0, endLine: 1, endCharacter: 0),
			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out ParsedDiagnostics? publishedDiagnostics
		);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.HasCount(1, publishedDiagnostics.Diagnostics);
		Assert.AreEqual(content.Length, publishedDiagnostics.Diagnostics[0].Range.Offset);
		Assert.AreEqual(publishedDiagnostics.Diagnostics[0].Range.Offset, publishedDiagnostics.Diagnostics[0].Range.EndOffset);
	}

	[TestMethod]
	public void TryParse_PreservesZeroWidthDiagnosticInsideWordWithoutWidening()
	{
		const string content = "local value = compute(1)";

		bool parsed = DiagnosticsParser.TryParse(
			parameters: CreateDiagnostics(line: 0, startCharacter: 21, endLine: 0, endCharacter: 21),
			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out ParsedDiagnostics? publishedDiagnostics
		);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.HasCount(1, publishedDiagnostics.Diagnostics);

		// A position-only diagnostic such as "expected ')' here" must keep the range the server
		// published: widening it to the enclosing word would echo a range the server never sent back to
		// it in code-action requests.
		Assert.AreEqual(21, publishedDiagnostics.Diagnostics[0].Range.Offset);
		Assert.AreEqual(21, publishedDiagnostics.Diagnostics[0].Range.EndOffset);
	}

	[TestMethod]
	public void TryParse_IgnoresMalformedDiagnosticEntriesAndPreservesValidEntries()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			parameters: new Lsp.PublishDiagnosticsParams(
				Uri: null,
				Version: 1,

				Diagnostics:
				[
					new Lsp.DiagnosticPayload(
						Range: null,
						Severity: Lsp.DiagnosticSeverity.Error,
						Message: "Broken payload.",
						Source: null,
						Code: null
					),

					new Lsp.DiagnosticPayload(
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(0, 6), new Lsp.PositionPayload(0, 11)),
						Severity: Lsp.DiagnosticSeverity.Error,
						Message: "Valid payload.",
						Source: null,
						Code: null
					),
				]
			),

			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out ParsedDiagnostics? publishedDiagnostics
		);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.HasCount(1, publishedDiagnostics.Diagnostics);
		Assert.AreEqual(6, publishedDiagnostics.Diagnostics[0].Range.Offset);
		Assert.AreEqual(11, publishedDiagnostics.Diagnostics[0].Range.EndOffset);
	}

	[TestMethod]
	public void TryParse_PreservesInformationAndHintDiagnostics()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			parameters: new Lsp.PublishDiagnosticsParams(
				Uri: null,
				Version: 1,

				Diagnostics:
				[
					new Lsp.DiagnosticPayload(
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(0, 0), new Lsp.PositionPayload(0, 5)),
						Severity: Lsp.DiagnosticSeverity.Information,
						Message: "Informational payload.",
						Source: null,
						Code: null
					),

					new Lsp.DiagnosticPayload(
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(0, 6), new Lsp.PositionPayload(0, 11)),
						Severity: Lsp.DiagnosticSeverity.Hint,
						Message: "Hint payload.",
						Source: null,
						Code: null
					),
				]
			),

			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out ParsedDiagnostics? publishedDiagnostics
		);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.HasCount(2, publishedDiagnostics.Diagnostics);
		Assert.AreEqual(DiagnosticSeverity.Information, publishedDiagnostics.Diagnostics[0].Severity);
		Assert.AreEqual(DiagnosticSeverity.Hint, publishedDiagnostics.Diagnostics[1].Severity);
	}

	[TestMethod]
	public void TryParse_StoresRawMessageAndFillsSourceAndCode()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			parameters: new Lsp.PublishDiagnosticsParams(
				Uri: null,
				Version: 1,

				Diagnostics:
				[
					new Lsp.DiagnosticPayload(
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(0, 0), new Lsp.PositionPayload(0, 5)),
						Severity: Lsp.DiagnosticSeverity.Warning,
						Message: "  unused variable  ",
						Source: "  LuaLS  ",
						Code: JsonSerializer.SerializeToElement("W211")
					),
				]
			),

			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out ParsedDiagnostics? publishedDiagnostics
		);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.HasCount(1, publishedDiagnostics.Diagnostics);
		Assert.AreEqual("unused variable", publishedDiagnostics.Diagnostics[0].Message);
		Assert.AreEqual("LuaLS", publishedDiagnostics.Diagnostics[0].Source);
		Assert.AreEqual("W211", publishedDiagnostics.Diagnostics[0].Code);

		// The raw element preserves the string kind and the value exactly as received, for protocol reconstruction.
		Assert.IsTrue(publishedDiagnostics.Entries[0].HasCode);
		Assert.AreEqual(JsonValueKind.String, publishedDiagnostics.Entries[0].Code.ValueKind);
		Assert.AreEqual("W211", publishedDiagnostics.Entries[0].Code.GetString());
	}

	[TestMethod]
	public void TryParse_StoresNumericCodeAsRawText()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			parameters: new Lsp.PublishDiagnosticsParams(
				Uri: null,
				Version: 1,

				Diagnostics:
				[
					new Lsp.DiagnosticPayload(
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(0, 0), new Lsp.PositionPayload(0, 5)),
						Severity: Lsp.DiagnosticSeverity.Warning,
						Message: "Unused variable.",
						Source: null,
						Code: JsonSerializer.SerializeToElement(211)
					),
				]
			),

			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out ParsedDiagnostics? publishedDiagnostics
		);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual("211", publishedDiagnostics.Diagnostics[0].Code);

		// The raw element keeps the numeric JSON kind, so a rebuilt protocol payload stays a number
		// instead of degrading to the display string.
		Assert.IsTrue(publishedDiagnostics.Entries[0].HasCode);
		Assert.AreEqual(JsonValueKind.Number, publishedDiagnostics.Entries[0].Code.ValueKind);
		Assert.AreEqual(211, publishedDiagnostics.Entries[0].Code.GetInt32());
	}

	[TestMethod]
	public void TryParse_NormalizesBlankMessageSourceAndCode()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			parameters: new Lsp.PublishDiagnosticsParams(
				Uri: null,
				Version: 1,

				Diagnostics:
				[
					new Lsp.DiagnosticPayload(
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(0, 0), new Lsp.PositionPayload(0, 5)),
						Severity: Lsp.DiagnosticSeverity.Warning,
						Message: "   ",
						Source: "  ",
						Code: JsonSerializer.SerializeToElement(string.Empty)
					),
				]
			),

			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out ParsedDiagnostics? publishedDiagnostics
		);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual("Unknown diagnostic.", publishedDiagnostics.Diagnostics[0].Message);
		Assert.IsNull(publishedDiagnostics.Diagnostics[0].Source);
		Assert.IsNull(publishedDiagnostics.Diagnostics[0].Code);

		// Only the display attribution normalizes; the raw element stays exactly as received.
		Assert.IsTrue(publishedDiagnostics.Entries[0].HasCode);
		Assert.AreEqual(string.Empty, publishedDiagnostics.Entries[0].Code.GetString());
	}

	[TestMethod]
	public void TryParse_IgnoresCodeValuesOutsideStringAndNumber()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			parameters: new Lsp.PublishDiagnosticsParams(
				Uri: null,
				Version: 1,

				Diagnostics:
				[
					new Lsp.DiagnosticPayload(
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(0, 0), new Lsp.PositionPayload(0, 5)),
						Severity: Lsp.DiagnosticSeverity.Warning,
						Message: "Unused variable.",
						Source: null,
						Code: JsonSerializer.SerializeToElement(true)
					),
				]
			),

			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out ParsedDiagnostics? publishedDiagnostics
		);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.IsNull(publishedDiagnostics.Diagnostics[0].Code);

		// A kind the display attribution does not model is dropped from the raw entry too, so the
		// code-action context omits the member instead of echoing an unrepresentable value.
		Assert.IsFalse(publishedDiagnostics.Entries[0].HasCode);
	}

	[TestMethod]
	public void TryParse_UndefinedSeverities_FallBackToWarning()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			parameters: new Lsp.PublishDiagnosticsParams(
				Uri: null,
				Version: 1,

				Diagnostics:
				[
					new Lsp.DiagnosticPayload(
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(0, 0), new Lsp.PositionPayload(0, 5)),
						Severity: (Lsp.DiagnosticSeverity)9,
						Message: "Future severity.",
						Source: null,
						Code: null
					),

					new Lsp.DiagnosticPayload(
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(0, 6), new Lsp.PositionPayload(0, 11)),
						Severity: (Lsp.DiagnosticSeverity)0,
						Message: "Zero severity.",
						Source: null,
						Code: null
					),

					new Lsp.DiagnosticPayload(
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(0, 12), new Lsp.PositionPayload(0, 13)),
						Severity: (Lsp.DiagnosticSeverity)(-3),
						Message: "Negative severity.",
						Source: null,
						Code: null
					),
				]
			),

			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out ParsedDiagnostics? publishedDiagnostics
		);

		// Values outside the LSP 1-4 range must never leak undefined enum members into ordering.
		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.HasCount(3, publishedDiagnostics.Diagnostics);
		Assert.AreEqual(DiagnosticSeverity.Warning, publishedDiagnostics.Diagnostics[0].Severity);
		Assert.AreEqual(DiagnosticSeverity.Warning, publishedDiagnostics.Diagnostics[1].Severity);
		Assert.AreEqual(DiagnosticSeverity.Warning, publishedDiagnostics.Diagnostics[2].Severity);
	}

	[TestMethod]
	public void TryParse_MissingSeverity_FallsBackToError()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			parameters: new Lsp.PublishDiagnosticsParams(
				Uri: null,
				Version: 1,

				Diagnostics:
				[
					new Lsp.DiagnosticPayload(
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(0, 0), new Lsp.PositionPayload(0, 5)),
						Severity: null,
						Message: "Unclassified payload.",
						Source: null,
						Code: null
					),
				]
			),

			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out ParsedDiagnostics? publishedDiagnostics
		);

		// The protocol defines no severity default; the provider presents an omitted severity as an
		// error (the highest-signal fallback).
		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.HasCount(1, publishedDiagnostics.Diagnostics);
		Assert.AreEqual(DiagnosticSeverity.Error, publishedDiagnostics.Diagnostics[0].Severity);
	}

	[TestMethod]
	public void TryParse_OrdersDiagnosticsByStartOffset()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			parameters: new Lsp.PublishDiagnosticsParams(
				Uri: null,
				Version: 1,

				Diagnostics:
				[
					new Lsp.DiagnosticPayload(
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(0, 6), new Lsp.PositionPayload(0, 11)),
						Severity: Lsp.DiagnosticSeverity.Warning,
						Message: "Later warning.",
						Source: null,
						Code: null
					),

					new Lsp.DiagnosticPayload(
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(0, 0), new Lsp.PositionPayload(0, 5)),
						Severity: Lsp.DiagnosticSeverity.Hint,
						Message: "Earlier hint.",
						Source: null,
						Code: null
					),
				]
			),

			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out ParsedDiagnostics? publishedDiagnostics
		);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.HasCount(2, publishedDiagnostics.Diagnostics);
		Assert.AreEqual(0, publishedDiagnostics.Diagnostics[0].Range.Offset);
		Assert.AreEqual(6, publishedDiagnostics.Diagnostics[1].Range.Offset);
	}

	[TestMethod]
	public void TryParse_OutOfRangePositions_AreClampedToTheDocument()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			parameters: new Lsp.PublishDiagnosticsParams(
				Uri: null,
				Version: 1,

				Diagnostics:
				[
					new Lsp.DiagnosticPayload(
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(99, -4), new Lsp.PositionPayload(120, -2)),
						Severity: Lsp.DiagnosticSeverity.Warning,
						Message: "Clamped warning.",
						Source: null,
						Code: null
					),
				]
			),

			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out ParsedDiagnostics? publishedDiagnostics
		);

		// The last line and a zero column are used instead of failing the payload.
		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.HasCount(1, publishedDiagnostics.Diagnostics);
		Assert.AreEqual(0, publishedDiagnostics.Diagnostics[0].Range.Offset);
		Assert.AreEqual(0, publishedDiagnostics.Diagnostics[0].Range.EndOffset);
	}

	[TestMethod]
	public void TryParse_UnmappableRange_FallsBackToTheWordAnchor()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			parameters: new Lsp.PublishDiagnosticsParams(
				Uri: null,
				Version: 1,

				Diagnostics:
				[
					new Lsp.DiagnosticPayload(
						// The end line lies beyond the document, so clamping collapses the range into an
						// inverted one; the diagnostic must survive through the word anchor instead of
						// being dropped.
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(0, 6), new Lsp.PositionPayload(5, 3)),
						Severity: Lsp.DiagnosticSeverity.Warning,
						Message: "Word anchored warning.",
						Source: null,
						Code: null
					),
				]
			),

			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out ParsedDiagnostics? publishedDiagnostics
		);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.HasCount(1, publishedDiagnostics.Diagnostics);
		Assert.AreEqual("local value = 1".IndexOf("value", StringComparison.Ordinal), publishedDiagnostics.Diagnostics[0].Range.Offset);

		Assert.AreEqual(
			"local value = 1".IndexOf("value", StringComparison.Ordinal) + "value".Length,
			publishedDiagnostics.Diagnostics[0].Range.EndOffset
		);
	}

	[TestMethod]
	public void TryParse_InvertedRange_AnchorsAtTheStartInsteadOfFabricatingAnOrder()
	{
		const string content = "local value = 1\nprint(value)\n";

		// start=(1,0) lies after end=(0,5): the payload is inverted. Clamping the endpoints would force
		// end >= start and fabricate a valid-looking range, so the diagnostic must anchor to the word at
		// its start position instead.
		bool parsed = DiagnosticsParser.TryParse(
			parameters: CreateDiagnostics(line: 1, startCharacter: 0, endLine: 0, endCharacter: 5),
			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out ParsedDiagnostics? publishedDiagnostics
		);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.HasCount(1, publishedDiagnostics.Diagnostics);

		Diagnostic diagnostic = publishedDiagnostics.Diagnostics[0];

		Assert.AreEqual(content.IndexOf("print", StringComparison.Ordinal), diagnostic.Range.Offset);
		Assert.AreEqual("print".Length, diagnostic.Range.EndOffset - diagnostic.Range.Offset);
	}

	[TestMethod]
	public void TryParse_SameStartOffset_OrdersErrorBeforeWarning()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			parameters: new Lsp.PublishDiagnosticsParams(
				Uri: null,
				Version: 1,

				Diagnostics:
				[
					new Lsp.DiagnosticPayload(
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(0, 0), new Lsp.PositionPayload(0, 5)),
						Severity: Lsp.DiagnosticSeverity.Warning,
						Message: "Warning first.",
						Source: null,
						Code: null
					),

					new Lsp.DiagnosticPayload(
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(0, 0), new Lsp.PositionPayload(0, 5)),
						Severity: Lsp.DiagnosticSeverity.Error,
						Message: "Error second.",
						Source: null,
						Code: null
					),
				]
			),

			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out ParsedDiagnostics? publishedDiagnostics
		);

		// Diagnostics with the same start offset are ordered by severity rank, error first.
		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.HasCount(2, publishedDiagnostics.Diagnostics);
		Assert.AreEqual("Error second.", publishedDiagnostics.Diagnostics[0].Message);
		Assert.AreEqual("Warning first.", publishedDiagnostics.Diagnostics[1].Message);
	}

	[TestMethod]
	public void TryParse_SameOffsetAndSeverity_PreservesServerOrder()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			parameters: new Lsp.PublishDiagnosticsParams(
				Uri: null,
				Version: 1,

				Diagnostics:
				[
					new Lsp.DiagnosticPayload(
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(0, 0), new Lsp.PositionPayload(0, 5)),
						Severity: Lsp.DiagnosticSeverity.Warning,
						Message: "Warning A.",
						Source: null,
						Code: null
					),

					new Lsp.DiagnosticPayload(
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(0, 0), new Lsp.PositionPayload(0, 5)),
						Severity: Lsp.DiagnosticSeverity.Warning,
						Message: "Warning B.",
						Source: null,
						Code: null
					),

					new Lsp.DiagnosticPayload(
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(0, 0), new Lsp.PositionPayload(0, 5)),
						Severity: Lsp.DiagnosticSeverity.Warning,
						Message: "Warning C.",
						Source: null,
						Code: null
					),
				]
			),

			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out ParsedDiagnostics? publishedDiagnostics
		);

		// Equal start offsets and equal severities leave no ordering key except the published sequence, so the
		// parser must not let an unstable sort reorder them.
		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.HasCount(3, publishedDiagnostics.Diagnostics);
		Assert.AreEqual("Warning A.", publishedDiagnostics.Diagnostics[0].Message);
		Assert.AreEqual("Warning B.", publishedDiagnostics.Diagnostics[1].Message);
		Assert.AreEqual("Warning C.", publishedDiagnostics.Diagnostics[2].Message);
	}

	[TestMethod]
	public void TryParse_CustomPolicy_AppliesMessageFallbackAndWordRule()
	{
		const string content = "print(\"hi\")";

		var customPolicy = new DiagnosticMappingPolicy
		{
			UnknownMessage = "Custom fallback message.",
			WordCharacters = IdentifierCharacterPolicy.Create(character => char.IsLetterOrDigit(character) || character is '_' or '"'),
		};

		// start=(0,7) lies after end=(0,0): the payload is inverted, so the diagnostic collapses to the
		// word anchor at its start. The custom rule treats '"' as a word character, so the anchor covers
		// the whole quoted run instead of only its inner letters.
		bool parsed = DiagnosticsParser.TryParse(
			parameters: new Lsp.PublishDiagnosticsParams(
				Uri: null,
				Version: 1,

				Diagnostics:
				[
					new Lsp.DiagnosticPayload(
						Range: new Lsp.RangePayload(new Lsp.PositionPayload(0, 7), new Lsp.PositionPayload(0, 0)),
						Severity: Lsp.DiagnosticSeverity.Warning,
						Message: "   ",
						Source: null,
						Code: null
					),
				]
			),

			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: customPolicy,
			out ParsedDiagnostics? publishedDiagnostics
		);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.HasCount(1, publishedDiagnostics.Diagnostics);
		Assert.AreEqual("Custom fallback message.", publishedDiagnostics.Diagnostics[0].Message);
		Assert.AreEqual(6, publishedDiagnostics.Diagnostics[0].Range.Offset);
		Assert.AreEqual(10, publishedDiagnostics.Diagnostics[0].Range.EndOffset);
	}

	[TestMethod]
	public void TryParse_StalePayloadVersion_DropsThePayload()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			parameters: CreateDiagnostics(line: 0, startCharacter: 6, endLine: 0, endCharacter: 11, version: 2),
			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out ParsedDiagnostics? publishedDiagnostics
		);

		// The parser-level fence rejects a payload that reports a different positive document
		// version before any offsets are mapped.
		Assert.IsFalse(parsed);
		Assert.IsNull(publishedDiagnostics);

		// An unversioned payload stays acceptable for the same document version.
		bool unknownVersionParsed = DiagnosticsParser.TryParse(
			parameters: CreateDiagnostics(line: 0, startCharacter: 6, endLine: 0, endCharacter: 11, version: 0),
			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			publishedDiagnostics: out ParsedDiagnostics? unknownVersionPayload
		);

		Assert.IsTrue(unknownVersionParsed);
		Assert.IsNotNull(unknownVersionPayload);
		Assert.AreEqual(0, unknownVersionPayload.Version);
	}

	[TestMethod]
	public void TryParse_SurrogatePair_CountsUtf16CodeUnits()
	{
		// The emoji is an astral character: two UTF-16 code units. LSP characters are code units, so the "print"
		// run starts at unit 2 and a range that starts there must map to offset 2, not to a code-point index.
		const string content = "\U0001F600print";

		bool parsed = DiagnosticsParser.TryParse(
			parameters: CreateDiagnostics(line: 0, startCharacter: 2, endLine: 0, endCharacter: 7),
			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out ParsedDiagnostics? publishedDiagnostics
		);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.HasCount(1, publishedDiagnostics.Diagnostics);
		Assert.AreEqual(2, publishedDiagnostics.Diagnostics[0].Range.Offset);
		Assert.AreEqual(7, publishedDiagnostics.Diagnostics[0].Range.EndOffset);
	}

	[TestMethod]
	public void TryParse_EmptyContent_MapsEveryRangeToOffsetZero()
	{
		const string content = "";

		bool parsed = DiagnosticsParser.TryParse(
			parameters: CreateDiagnostics(line: 0, startCharacter: 0, endLine: 0, endCharacter: 0),
			normalizedFilePath: FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out ParsedDiagnostics? publishedDiagnostics
		);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.HasCount(1, publishedDiagnostics.Diagnostics);
		Assert.AreEqual(0, publishedDiagnostics.Diagnostics[0].Range.Offset);
		Assert.AreEqual(0, publishedDiagnostics.Diagnostics[0].Range.EndOffset);
	}

	private static Lsp.PublishDiagnosticsParams CreateDiagnostics(int line, int startCharacter, int endLine, int endCharacter, int version = 1)
	{
		return new(
			Uri: null,
			version,

			Diagnostics:
			[
				new Lsp.DiagnosticPayload(
					Range: new Lsp.RangePayload(new Lsp.PositionPayload(line, startCharacter), new Lsp.PositionPayload(endLine, endCharacter)),
					Severity: Lsp.DiagnosticSeverity.Error,
					Message: "Syntax error.",
					Source: null,
					Code: null
				),
			]
		);
	}
}
