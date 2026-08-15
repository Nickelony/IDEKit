using Nickelony.IDEKit.Core.Diagnostics;
using System.Text.Json;

namespace Nickelony.LanguageServer.Provider.Tests;

[TestClass]
public sealed class DiagnosticsParserTests
{
	// The parser carries the path through to PublishedDiagnostics without normalizing it, so a plain literal
	// is sufficient here.
	private const string FilePath = "test.lua";

	[TestMethod]
	public void TryParse_PreservesZeroWidthDiagnosticOnEmptyLine()
	{
		const string content = "local value = 1\n\nnextLine = 2";

		bool parsed = DiagnosticsParser.TryParse(
			CreateDiagnostics(line: 1, startCharacter: 0, endLine: 1, endCharacter: 0),
			FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out PublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(1, publishedDiagnostics.Diagnostics.Count);

		// A zero-width diagnostic keeps its exact position (line 1, character 0) instead of being
		// anchored to visible text; widening an empty range is the rendering layer's concern.
		Assert.AreEqual(content.IndexOf("nextLine", StringComparison.Ordinal) - 1, publishedDiagnostics.Diagnostics[0].StartOffset);
		Assert.AreEqual(publishedDiagnostics.Diagnostics[0].StartOffset, publishedDiagnostics.Diagnostics[0].EndOffset);
	}

	[TestMethod]
	public void TryParse_PreservesZeroWidthDiagnosticOnTrailingEmptyLine()
	{
		const string content = "return value\n";

		bool parsed = DiagnosticsParser.TryParse(
			CreateDiagnostics(line: 1, startCharacter: 0, endLine: 1, endCharacter: 0),
			FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out PublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(1, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual(content.Length, publishedDiagnostics.Diagnostics[0].StartOffset);
		Assert.AreEqual(publishedDiagnostics.Diagnostics[0].StartOffset, publishedDiagnostics.Diagnostics[0].EndOffset);
	}

	[TestMethod]
	public void TryParse_PreservesZeroWidthDiagnosticInsideWordWithoutWidening()
	{
		const string content = "local value = compute(1)";

		bool parsed = DiagnosticsParser.TryParse(
			CreateDiagnostics(line: 0, startCharacter: 21, endLine: 0, endCharacter: 21),
			FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out PublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(1, publishedDiagnostics.Diagnostics.Count);

		// A position-only diagnostic such as "expected ')' here" must keep the range the server
		// published: widening it to the enclosing word would echo a range the server never sent back to
		// it in code-action requests.
		Assert.AreEqual(21, publishedDiagnostics.Diagnostics[0].StartOffset);
		Assert.AreEqual(21, publishedDiagnostics.Diagnostics[0].EndOffset);
	}

	[TestMethod]
	public void TryParse_IgnoresMalformedDiagnosticEntriesAndPreservesValidEntries()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						Range: null,
						Severity: DiagnosticSeverity.Error,
						Message: "Broken payload.",
						Source: null,
						Code: null),
					new DiagnosticPayload(
						new ProtocolRangePayload(
							new ProtocolPosition(0, 6),
							new ProtocolPosition(0, 11)),
						DiagnosticSeverity.Error,
						"Valid payload.",
						null,
						null)
				]),
			FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out PublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(1, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual(6, publishedDiagnostics.Diagnostics[0].StartOffset);
		Assert.AreEqual(11, publishedDiagnostics.Diagnostics[0].EndOffset);
	}

	[TestMethod]
	public void TryParse_PreservesInformationAndHintDiagnostics()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(
							new ProtocolPosition(0, 0),
							new ProtocolPosition(0, 5)),
						DiagnosticSeverity.Information,
						"Informational payload.",
						null,
						null),
					new DiagnosticPayload(
						new ProtocolRangePayload(
							new ProtocolPosition(0, 6),
							new ProtocolPosition(0, 11)),
						DiagnosticSeverity.Hint,
						"Hint payload.",
						null,
						null)
				]),
			FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out PublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(2, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual(TextDiagnosticSeverity.Information, publishedDiagnostics.Diagnostics[0].Severity);
		Assert.AreEqual(TextDiagnosticSeverity.Hint, publishedDiagnostics.Diagnostics[1].Severity);
	}

	[TestMethod]
	public void TryParse_StoresRawMessageAndFillsSourceAndCode()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(
							new ProtocolPosition(0, 0),
							new ProtocolPosition(0, 5)),
						DiagnosticSeverity.Warning,
						"  unused variable  ",
						"  LuaLS  ",
						JsonSerializer.SerializeToElement("W211"))
				]),
			FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out PublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(1, publishedDiagnostics.Diagnostics.Count);
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
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(
							new ProtocolPosition(0, 0),
							new ProtocolPosition(0, 5)),
						DiagnosticSeverity.Warning,
						"Unused variable.",
						null,
						JsonSerializer.SerializeToElement(211))
				]),
			FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out PublishedDiagnostics? publishedDiagnostics);

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
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(
							new ProtocolPosition(0, 0),
							new ProtocolPosition(0, 5)),
						DiagnosticSeverity.Warning,
						"   ",
						"  ",
						JsonSerializer.SerializeToElement(string.Empty))
				]),
			FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out PublishedDiagnostics? publishedDiagnostics);

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
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(
							new ProtocolPosition(0, 0),
							new ProtocolPosition(0, 5)),
						DiagnosticSeverity.Warning,
						"Unused variable.",
						null,
						JsonSerializer.SerializeToElement(true))
				]),
			FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out PublishedDiagnostics? publishedDiagnostics);

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
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 5)),
						(DiagnosticSeverity)9,
						"Future severity.",
						null,
						null),
					new DiagnosticPayload(
						new ProtocolRangePayload(new ProtocolPosition(0, 6), new ProtocolPosition(0, 11)),
						(DiagnosticSeverity)0,
						"Zero severity.",
						null,
						null),
					new DiagnosticPayload(
						new ProtocolRangePayload(new ProtocolPosition(0, 12), new ProtocolPosition(0, 13)),
						(DiagnosticSeverity)(-3),
						"Negative severity.",
						null,
						null)
				]),
			FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out PublishedDiagnostics? publishedDiagnostics);

		// Values outside the LSP 1-4 range must never leak undefined enum members into ordering.
		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(3, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual(TextDiagnosticSeverity.Warning, publishedDiagnostics.Diagnostics[0].Severity);
		Assert.AreEqual(TextDiagnosticSeverity.Warning, publishedDiagnostics.Diagnostics[1].Severity);
		Assert.AreEqual(TextDiagnosticSeverity.Warning, publishedDiagnostics.Diagnostics[2].Severity);
	}

	[TestMethod]
	public void TryParse_MissingSeverity_FallsBackToError()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 5)),
						Severity: null,
						"Unclassified payload.",
						null,
						null)
				]),
			FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out PublishedDiagnostics? publishedDiagnostics);

		// The protocol defines no severity default; the provider presents an omitted severity as an
		// error (the highest-signal fallback).
		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(1, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual(TextDiagnosticSeverity.Error, publishedDiagnostics.Diagnostics[0].Severity);
	}

	[TestMethod]
	public void TryParse_OrdersDiagnosticsByStartOffset()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(new ProtocolPosition(0, 6), new ProtocolPosition(0, 11)),
						DiagnosticSeverity.Warning,
						"Later warning.",
						null,
						null),
					new DiagnosticPayload(
						new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 5)),
						DiagnosticSeverity.Hint,
						"Earlier hint.",
						null,
						null)
				]),
			FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out PublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(2, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual(0, publishedDiagnostics.Diagnostics[0].StartOffset);
		Assert.AreEqual(6, publishedDiagnostics.Diagnostics[1].StartOffset);
	}

	[TestMethod]
	public void TryParse_OutOfRangePositions_AreClampedToTheDocument()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(new ProtocolPosition(99, -4), new ProtocolPosition(120, -2)),
						DiagnosticSeverity.Warning,
						"Clamped warning.",
						null,
						null)
				]),
			FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out PublishedDiagnostics? publishedDiagnostics);

		// The last line and a zero column are used instead of failing the payload.
		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(1, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual(0, publishedDiagnostics.Diagnostics[0].StartOffset);
		Assert.AreEqual(0, publishedDiagnostics.Diagnostics[0].EndOffset);
	}

	[TestMethod]
	public void TryParse_UnmappableRange_FallsBackToTheWordAnchor()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						// The end line lies beyond the document, so clamping collapses the range into an
						// inverted one; the diagnostic must survive through the word anchor instead of
						// being dropped.
						new ProtocolRangePayload(new ProtocolPosition(0, 6), new ProtocolPosition(5, 3)),
						DiagnosticSeverity.Warning,
						"Word anchored warning.",
						null,
						null)
				]),
			FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out PublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(1, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual("local value = 1".IndexOf("value", StringComparison.Ordinal), publishedDiagnostics.Diagnostics[0].StartOffset);
		Assert.AreEqual("local value = 1".IndexOf("value", StringComparison.Ordinal) + "value".Length, publishedDiagnostics.Diagnostics[0].EndOffset);
	}

	[TestMethod]
	public void TryParse_InvertedRange_AnchorsAtTheStartInsteadOfFabricatingAnOrder()
	{
		const string content = "local value = 1\nprint(value)\n";

		// start=(1,0) lies after end=(0,5): the payload is inverted. Clamping the endpoints would force
		// end >= start and fabricate a valid-looking range, so the diagnostic must anchor to the word at
		// its start position instead.
		bool parsed = DiagnosticsParser.TryParse(
			CreateDiagnostics(line: 1, startCharacter: 0, endLine: 0, endCharacter: 5),
			FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out PublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(1, publishedDiagnostics.Diagnostics.Count);

		var diagnostic = publishedDiagnostics.Diagnostics[0];
		Assert.AreEqual(content.IndexOf("print", StringComparison.Ordinal), diagnostic.StartOffset);
		Assert.AreEqual("print".Length, diagnostic.EndOffset - diagnostic.StartOffset);
	}

	[TestMethod]
	public void TryParse_SameStartOffset_OrdersErrorBeforeWarning()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 5)),
						DiagnosticSeverity.Warning,
						"Warning first.",
						null,
						null),
					new DiagnosticPayload(
						new ProtocolRangePayload(new ProtocolPosition(0, 0), new ProtocolPosition(0, 5)),
						DiagnosticSeverity.Error,
						"Error second.",
						null,
						null)
				]),
			FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out PublishedDiagnostics? publishedDiagnostics);

		// Diagnostics with the same start offset are ordered by severity rank, error first.
		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(2, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual("Error second.", publishedDiagnostics.Diagnostics[0].Message);
		Assert.AreEqual("Warning first.", publishedDiagnostics.Diagnostics[1].Message);
	}

	[TestMethod]
	public void TryParse_CustomPolicy_AppliesMessageFallbackAndWordRule()
	{
		const string content = "print(\"hi\")";

		var customPolicy = new DiagnosticMappingPolicy(
			"Custom fallback message.",
			character => char.IsLetterOrDigit(character) || character is '_' or '"');

		// start=(0,7) lies after end=(0,0): the payload is inverted, so the diagnostic collapses to the
		// word anchor at its start. The custom rule treats '"' as a word character, so the anchor covers
		// the whole quoted run instead of only its inner letters.
		bool parsed = DiagnosticsParser.TryParse(
			new PublishDiagnosticsParams(
				Uri: null,
				Version: 1,
				Diagnostics:
				[
					new DiagnosticPayload(
						new ProtocolRangePayload(new ProtocolPosition(0, 7), new ProtocolPosition(0, 0)),
						DiagnosticSeverity.Warning,
						"   ",
						null,
						null)
				]),
			FilePath,
			content,
			documentVersion: 1,
			policy: customPolicy,
			out PublishedDiagnostics? publishedDiagnostics);

		Assert.IsTrue(parsed);
		Assert.IsNotNull(publishedDiagnostics);
		Assert.AreEqual(1, publishedDiagnostics.Diagnostics.Count);
		Assert.AreEqual("Custom fallback message.", publishedDiagnostics.Diagnostics[0].Message);
		Assert.AreEqual(6, publishedDiagnostics.Diagnostics[0].StartOffset);
		Assert.AreEqual(10, publishedDiagnostics.Diagnostics[0].EndOffset);
	}

	[TestMethod]
	public void TryParse_StalePayloadVersion_DropsThePayload()
	{
		const string content = "local value = 1";

		bool parsed = DiagnosticsParser.TryParse(
			CreateDiagnostics(line: 0, startCharacter: 6, endLine: 0, endCharacter: 11, version: 2),
			FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out PublishedDiagnostics? publishedDiagnostics);

		// The parser-level fence rejects a payload that reports a different positive document
		// version before any offsets are mapped.
		Assert.IsFalse(parsed);
		Assert.IsNull(publishedDiagnostics);

		// An unversioned payload stays acceptable for the same document version.
		bool unknownVersionParsed = DiagnosticsParser.TryParse(
			CreateDiagnostics(line: 0, startCharacter: 6, endLine: 0, endCharacter: 11, version: 0),
			FilePath,
			content,
			documentVersion: 1,
			policy: DiagnosticMappingPolicy.Default,
			out PublishedDiagnostics? unknownVersionPayload);

		Assert.IsTrue(unknownVersionParsed);
		Assert.IsNotNull(unknownVersionPayload);
		Assert.AreEqual(0, unknownVersionPayload.Version);
	}

	private static PublishDiagnosticsParams CreateDiagnostics(int line, int startCharacter, int endLine, int endCharacter, int version = 1) => new(
		Uri: null,
		Version: version,
		Diagnostics:
		[
			new DiagnosticPayload(
				new ProtocolRangePayload(
					new ProtocolPosition(line, startCharacter),
					new ProtocolPosition(endLine, endCharacter)),
				DiagnosticSeverity.Error,
				"Syntax error.",
				null,
				null)
		]);
}
