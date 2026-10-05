using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Lsp = Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Parses language-server diagnostics payloads into document diagnostics tied to tracked document versions.</summary>
/// <remarks>
/// The mapping is language-neutral; the language-specific rules - the blank-message fallback and the word-character rule used when a malformed range
/// falls back to a word anchor - arrive through <see cref="DiagnosticMappingPolicy"/>.
/// </remarks>
internal static class DiagnosticsParser
{
	/// <summary>Parses a diagnostics notification into document diagnostics for a tracked document.</summary>
	/// <param name="parameters">The published diagnostics notification payload from the language server.</param>
	/// <param name="normalizedFilePath">The normalized file path of the tracked document.</param>
	/// <param name="content">The current document content.</param>
	/// <param name="documentVersion">The tracked document version to match against the diagnostics version.</param>
	/// <param name="policy">The language-specific mapping rules.</param>
	/// <param name="publishedDiagnostics">When this method returns <see langword="true"/>, contains the parsed diagnostics payload.</param>
	/// <returns>
	/// <see langword="true"/> when the payload can be accepted for the tracked document version; otherwise, <see langword="false"/> when the payload
	/// version is known and does not match the tracked version.
	/// </returns>
	/// <remarks>
	/// <para>
	/// A payload with an unknown version (<c>0</c>) is accepted; whether it is stored, and whether it advances the cached version,
	/// is decided by the document tracker.
	/// </para>
	/// <para>
	/// Accepted diagnostics are ordered by start offset, then by severity rank (errors first), and finally by the order
	/// the server published them, so diagnostics that share an offset and a severity keep their relative sequence.
	/// </para>
	/// </remarks>
	internal static bool TryParse(
		Lsp.PublishDiagnosticsParams parameters,
		string normalizedFilePath,
		string content,
		int documentVersion,
		DiagnosticMappingPolicy policy,
		[NotNullWhen(true)] out ParsedDiagnostics? publishedDiagnostics
	)
	{
		publishedDiagnostics = null;

		int diagnosticsVersion = parameters.Version is > 0 ? parameters.Version.Value : 0;

		if (!DocumentVersionPolicy.IsPayloadCurrent(documentVersion, diagnosticsVersion))
			return false;

		IReadOnlyList<DiagnosticEntry> diagnostics = parameters.Diagnostics is { Count: > 0 }
			? BuildEntries(content, parameters.Diagnostics, policy)
			: [];

		publishedDiagnostics = new ParsedDiagnostics(normalizedFilePath, diagnostics, diagnosticsVersion);
		return true;
	}

	private static List<DiagnosticEntry> BuildEntries(string content, IReadOnlyList<Lsp.DiagnosticPayload> payloads, DiagnosticMappingPolicy policy)
	{
		var lineMap = TextLineMap.Build(content);

		// Each entry carries its server order so the sort below can preserve it among equal (offset, severity)
		// pairs: List<T>.Sort is unstable, so without the order key, diagnostics that share an offset and a
		// severity could swap places and a code-action round trip would see a different sequence than the server.
		var orderedDiagnostics = new List<(DiagnosticEntry Entry, int ServerOrder)>(payloads.Count);

		foreach (Lsp.DiagnosticPayload diagnosticPayload in payloads)
		{
			DiagnosticSeverity severity = DiagnosticSeverityConversion.FromProtocol(diagnosticPayload.Severity);

			if (!TryCreateDiagnostic(lineMap, diagnosticPayload, severity, policy, out Diagnostic? diagnostic))
				continue;

			// The whole raw payload is cached so a code-action context can echo the fields the shared
			// diagnostic does not model (data, tags, codeDescription, relatedInformation).
			orderedDiagnostics.Add((new DiagnosticEntry(diagnostic, diagnosticPayload), orderedDiagnostics.Count));
		}

		// Sorted in place with a static comparer instead of an OrderBy/ThenBy chain: this path runs per published
		// payload, and the chained operators would allocate one iterator per ordering key.
		orderedDiagnostics.Sort(
			static (left, right) =>
			{
				int offsetComparison = left.Entry.Diagnostic.Range.Offset.CompareTo(right.Entry.Diagnostic.Range.Offset);

				if (offsetComparison != 0)
					return offsetComparison;

				int severityComparison = GetSeverityRank(left.Entry.Diagnostic.Severity).CompareTo(GetSeverityRank(right.Entry.Diagnostic.Severity));

				return severityComparison != 0 ? severityComparison : left.ServerOrder.CompareTo(right.ServerOrder);
			}
		);

		var diagnostics = new List<DiagnosticEntry>(orderedDiagnostics.Count);

		foreach ((DiagnosticEntry entry, _) in orderedDiagnostics)
			diagnostics.Add(entry);

		return diagnostics;
	}

	/// <summary>Ranks a diagnostic severity for deterministic ordering.</summary>
	/// <remarks>
	/// The enum values are stable identifiers rather than a ranking, so the order is defined explicitly (error first).
	/// </remarks>
	private static int GetSeverityRank(DiagnosticSeverity severity)
	{
		return severity switch
		{
			DiagnosticSeverity.Error => 0,
			DiagnosticSeverity.Warning => 1,
			DiagnosticSeverity.Information => 2,
			DiagnosticSeverity.Hint => 3,
			_ => 4,
		};
	}

	/// <summary>Creates one shared diagnostic from a protocol payload.</summary>
	/// <param name="lineMap">The line map of the document content.</param>
	/// <param name="diagnosticPayload">The protocol diagnostic payload.</param>
	/// <param name="severity">The mapped severity.</param>
	/// <param name="policy">The language-specific mapping rules.</param>
	/// <param name="diagnostic">Receives the created diagnostic when successful.</param>
	/// <returns><see langword="true"/> when a diagnostic was created; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// Coordinates that exceed the current snapshot are clamped so a stale range cannot drop the diagnostic. A server-inverted range
	/// (end before start) is not clamped into a fabricated order: it collapses to a zero-length range at its start position and falls
	/// back to the word or content anchor there. A diagnostic without a range, or a position that cannot be mapped at all, is skipped.
	/// </remarks>
	private static bool TryCreateDiagnostic(
		TextLineMap lineMap,
		Lsp.DiagnosticPayload diagnosticPayload,
		DiagnosticSeverity severity,
		DiagnosticMappingPolicy policy,
		[NotNullWhen(true)] out Diagnostic? diagnostic
	)
	{
		diagnostic = null;

		if (diagnosticPayload.Range is not { } rangePayload)
			return false;

		// A server-inverted range (end before start) is malformed. Reject it against the raw protocol
		// coordinates: clamping its endpoints would force end >= start and fabricate a valid-looking
		// range, so the range is collapsed to a zero-length anchor at its start position instead.
		bool isInverted = rangePayload.IsReversed;

		// A range always carries its start and end (see RangePayload). Server coordinates that
		// exceed the current snapshot are clamped so a stale range cannot drop the diagnostic; negative
		// coordinates collapse to the line start. For an inverted range only the start is kept, because
		// the server sent its end before its start.
		int lineIndex = Math.Max(0, Math.Min(rangePayload.Start.Line, lineMap.LineCount - 1));
		int startCharacter = Math.Max(0, rangePayload.Start.Character);
		int endLineIndex = isInverted ? lineIndex : Math.Max(lineIndex, Math.Min(rangePayload.End.Line, lineMap.LineCount - 1));
		int endCharacter = isInverted ? startCharacter : Math.Max(0, rangePayload.End.Character);

		var positionRange = new TextPositionRange(new TextPosition(lineIndex, startCharacter), new TextPosition(endLineIndex, endCharacter));

		// A position-only diagnostic (a legal zero-length range such as "expected ')' here") keeps its zero
		// length: the exact span is the server's answer, and widening it would echo a range the server never
		// published back to it in code-action requests. A malformed range instead falls back to the word or
		// content anchor of its start position: an inverted range, whose zero length is fabricated, or a range
		// that is reversed once clamped. That span is truthful, whereas clamping the end would fabricate a range
		// the server never sent.
		if (!lineMap.TryGetOffsets(positionRange, out TextRange resolvedRange) || isInverted)
		{
			var anchorRange = new TextPositionRange(positionRange.Start, positionRange.Start);

			if (!DiagnosticAnchorResolver.TryResolveOffsets(lineMap, range: anchorRange, wordCharacters: policy.WordCharacters, out resolvedRange))
				return false;
		}

		diagnostic = CreateDiagnostic(severity, diagnosticPayload, range: resolvedRange, policy);
		return true;
	}

	private static Diagnostic CreateDiagnostic(
		DiagnosticSeverity severity,
		Lsp.DiagnosticPayload diagnosticPayload,
		TextRange range,
		DiagnosticMappingPolicy policy
	)
	{
		return new(severity, message: GetMessage(diagnosticPayload, policy), range)
		{
			Source = GetSource(diagnosticPayload),
			Code = GetCode(diagnosticPayload),
		};
	}

	/// <summary>Returns the displayed diagnostic message: the server message trimmed, or the policy's fixed fallback for a blank one.</summary>
	/// <param name="diagnosticPayload">The protocol diagnostic payload.</param>
	/// <param name="policy">The language-specific mapping rules.</param>
	/// <returns>The display message.</returns>
	private static string GetMessage(Lsp.DiagnosticPayload diagnosticPayload, DiagnosticMappingPolicy policy)
	{
		string? message = diagnosticPayload.Message?.Trim();
		return string.IsNullOrWhiteSpace(message) ? policy.UnknownMessage : message;
	}

	/// <summary>Returns the diagnostic source attribution, or <see langword="null"/> when the server supplied none.</summary>
	/// <param name="diagnosticPayload">The protocol diagnostic payload.</param>
	/// <returns>The trimmed source, or <see langword="null"/>.</returns>
	private static string? GetSource(Lsp.DiagnosticPayload diagnosticPayload) => OptionalTextNormalizer.Normalize(diagnosticPayload.Source);

	/// <summary>Returns the diagnostic code attribution, or <see langword="null"/> when the server supplied none.</summary>
	/// <param name="diagnosticPayload">The protocol diagnostic payload.</param>
	/// <returns>The trimmed code, or <see langword="null"/>.</returns>
	/// <remarks>
	/// A string code is trimmed and a numeric code keeps its raw JSON text, so the value stays stable across number formats.
	/// </remarks>
	private static string? GetCode(Lsp.DiagnosticPayload diagnosticPayload)
	{
		string? code = diagnosticPayload.Code switch
		{
			{ ValueKind: JsonValueKind.String } codeElement => codeElement.GetString(),
			{ ValueKind: JsonValueKind.Number } codeElement => codeElement.GetRawText(),
			_ => null,
		};

		return OptionalTextNormalizer.Normalize(code);
	}
}
