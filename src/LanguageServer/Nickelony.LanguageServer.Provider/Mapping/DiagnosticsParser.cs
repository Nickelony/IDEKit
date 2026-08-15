using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Infrastructure;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Parses language-server diagnostics payloads into document diagnostics tied to tracked document versions.
/// </summary>
/// <remarks>
/// The mapping is language-neutral; the two rules that vary per language - the blank-message fallback and the
/// word-character rule used when a malformed range falls back to a word anchor - arrive through
/// <see cref="DiagnosticMappingPolicy"/>.
/// </remarks>
internal static class DiagnosticsParser
{
	/// <summary>
	/// Parses a diagnostics notification into document diagnostics for a tracked document.
	/// </summary>
	/// <param name="parameters">The published diagnostics notification payload from the language server.</param>
	/// <param name="filePath">The normalized file path of the tracked document.</param>
	/// <param name="documentContent">The current document content.</param>
	/// <param name="documentVersion">The tracked document version to match against the diagnostics version.</param>
	/// <param name="policy">The language-specific mapping rules.</param>
	/// <param name="publishedDiagnostics">When this method returns <see langword="true"/>, contains the parsed diagnostics payload.</param>
	/// <returns>
	/// <see langword="true"/> when the payload can be accepted for the tracked document version; otherwise,
	/// <see langword="false"/> when the payload version is known and does not match the tracked version.
	/// </returns>
	/// <remarks>
	/// <para>
	/// A payload with an unknown version (<c>0</c>) is accepted; whether it is stored, and whether it advances
	/// the cached version, is decided by the tracked document store.
	/// </para>
	/// <para>
	/// Accepted diagnostics are ordered by start offset, then by severity rank (errors first), so the
	/// list order is deterministic for equal offsets.
	/// </para>
	/// </remarks>
	internal static bool TryParse(PublishDiagnosticsParams parameters, string filePath,
		string documentContent, int documentVersion, DiagnosticMappingPolicy policy,
		[NotNullWhen(true)] out PublishedDiagnostics? publishedDiagnostics)
	{
		publishedDiagnostics = null;

		int diagnosticsVersion = parameters.Version is > 0 ? parameters.Version.Value : 0;

		if (!DocumentVersionPolicy.IsPayloadCurrent(documentVersion, diagnosticsVersion))
			return false;

		IReadOnlyList<DiagnosticEntry> diagnostics = parameters.Diagnostics is { Count: > 0 }
			? BuildDiagnostics(documentContent, parameters.Diagnostics, policy)
			: [];

		publishedDiagnostics = new PublishedDiagnostics(filePath, diagnostics, diagnosticsVersion);
		return true;
	}

	private static List<DiagnosticEntry> BuildDiagnostics(string content, IReadOnlyList<DiagnosticPayload> payloads,
		DiagnosticMappingPolicy policy)
	{
		TextLineMap lineMap = TextLineMap.Build(content);
		var diagnostics = new List<DiagnosticEntry>();

		foreach (DiagnosticPayload diagnosticPayload in payloads)
		{
			TextDiagnosticSeverity severity = GetDiagnosticSeverity(diagnosticPayload);

			if (!TryCreateDiagnostic(lineMap, diagnosticPayload, severity, policy, out TextDiagnostic? diagnostic))
				continue;

			// The whole raw payload is cached so a code-action context can echo the fields the shared
			// diagnostic does not model (data, tags, codeDescription, relatedInformation).
			diagnostics.Add(new DiagnosticEntry(diagnostic, diagnosticPayload));
		}

		// Sorted in place with a static comparer instead of an OrderBy/ThenBy chain: this path runs per published
		// payload, and the chained operators would allocate one iterator per ordering key.
		diagnostics.Sort(static (left, right) =>
		{
			int offsetComparison = left.Diagnostic.StartOffset.CompareTo(right.Diagnostic.StartOffset);

			return offsetComparison != 0
				? offsetComparison
				: GetSeverityRank(left.Diagnostic.Severity).CompareTo(GetSeverityRank(right.Diagnostic.Severity));
		});

		return diagnostics;
	}

	/// <summary>
	/// Ranks a diagnostic severity for deterministic ordering. The enum values are stable identifiers
	/// rather than a ranking, so the order is defined explicitly (error first).
	/// </summary>
	private static int GetSeverityRank(TextDiagnosticSeverity severity)
	{
		return severity switch
		{
			TextDiagnosticSeverity.Error => 0,
			TextDiagnosticSeverity.Warning => 1,
			TextDiagnosticSeverity.Information => 2,
			TextDiagnosticSeverity.Hint => 3,
			_ => 4
		};
	}

	/// <summary>
	/// Creates one shared diagnostic from a protocol payload.
	/// </summary>
	/// <remarks>
	/// Coordinates that exceed the current snapshot are clamped so a stale range cannot drop the diagnostic.
	/// A server-inverted range (end before start) is not clamped into a fabricated order: it collapses to a
	/// zero-length range at its start position and falls back to the word or content anchor there. Only a
	/// position that cannot be mapped at all is skipped.
	/// </remarks>
	/// <param name="lineMap">The line map of the document content.</param>
	/// <param name="diagnosticPayload">The protocol diagnostic payload.</param>
	/// <param name="severity">The mapped severity.</param>
	/// <param name="policy">The language-specific mapping rules.</param>
	/// <param name="diagnostic">Receives the created diagnostic when successful.</param>
	/// <returns><see langword="true"/> when a diagnostic was created; otherwise, <see langword="false"/>.</returns>
	private static bool TryCreateDiagnostic(TextLineMap lineMap, DiagnosticPayload diagnosticPayload,
		TextDiagnosticSeverity severity, DiagnosticMappingPolicy policy, [NotNullWhen(true)] out TextDiagnostic? diagnostic)
	{
		diagnostic = null;

		if (diagnosticPayload.Range is not { } rangePayload)
			return false;

		// A server-inverted range (end before start) is malformed. Reject it against the raw protocol
		// coordinates: clamping its endpoints would force end >= start and fabricate a valid-looking
		// range, so the range is collapsed to a zero-length anchor at its start position instead.
		bool isInverted = !ResponseParser.IsOrderedRange(rangePayload.Start, rangePayload.End);

		// A range always carries both endpoints (see ProtocolRangePayload). Server coordinates that
		// exceed the current snapshot are clamped so a stale range cannot drop the diagnostic; negative
		// coordinates collapse to the line start. For an inverted range only the start is kept, because
		// the server sent its end before its start.
		int lineIndex = Math.Max(0, Math.Min(rangePayload.Start.Line, lineMap.LineCount - 1));
		int startCharacter = Math.Max(0, rangePayload.Start.Character);
		int endLineIndex = isInverted ? lineIndex : Math.Max(lineIndex, Math.Min(rangePayload.End.Line, lineMap.LineCount - 1));
		int endCharacter = isInverted ? startCharacter : Math.Max(0, rangePayload.End.Character);

		var positionRange = new TextPositionRange(
			new TextPosition(lineIndex, startCharacter),
			new TextPosition(endLineIndex, endCharacter));

		// A position-only diagnostic (a legal zero-length range such as "expected ')' here") keeps its
		// zero length: the exact span is the server's answer and widening it would echo a range the server
		// never published back to it in code-action requests. A malformed range - sent inverted (a
		// zero-length range that must not keep its fabricated length), or turned reversed by clamping an
		// out-of-range end (which cannot be mapped to offsets) - instead falls back to the word or content
		// anchor of its start position, because that span is a truthful anchor whereas clamping the end
		// would fabricate a range the server never sent.
		if (!lineMap.TryGetOffsets(positionRange, out TextRange resolvedRange) || isInverted)
		{
			var anchorRange = new TextPositionRange(positionRange.Start, positionRange.Start);

			if (!TextRangeOffsetResolver.TryResolveOffsets(lineMap, anchorRange, policy.IsWordCharacter, out resolvedRange))
				return false;
		}

		diagnostic = CreateDiagnostic(severity, diagnosticPayload, resolvedRange, policy);
		return true;
	}

	private static TextDiagnostic CreateDiagnostic(TextDiagnosticSeverity severity, DiagnosticPayload diagnosticPayload,
		TextRange range, DiagnosticMappingPolicy policy)
		=> new(severity, GetDiagnosticMessage(diagnosticPayload, policy), range.Offset, range.EndOffset)
		{
			Source = GetDiagnosticSource(diagnosticPayload),
			Code = GetDiagnosticCode(diagnosticPayload)
		};

	private static TextDiagnosticSeverity GetDiagnosticSeverity(DiagnosticPayload diagnosticPayload)
	{
		// The mapping is explicit instead of a cast, so an unknown protocol value - including future
		// protocol members - cannot leak an undefined enum member into severity ordering. The protocol
		// defines no default severity, so an omitted severity falls back to an error (the highest-signal
		// choice) and an unrecognized value to a warning.
		return diagnosticPayload.Severity switch
		{
			DiagnosticSeverity.Error => TextDiagnosticSeverity.Error,
			DiagnosticSeverity.Warning => TextDiagnosticSeverity.Warning,
			DiagnosticSeverity.Information => TextDiagnosticSeverity.Information,
			DiagnosticSeverity.Hint => TextDiagnosticSeverity.Hint,
			null => TextDiagnosticSeverity.Error,
			_ => TextDiagnosticSeverity.Warning,
		};
	}

	/// <summary>
	/// Gets the displayed diagnostic message: the server message trimmed, or the policy's fixed fallback for a blank one.
	/// </summary>
	/// <param name="diagnosticPayload">The protocol diagnostic payload.</param>
	/// <param name="policy">The language-specific mapping rules.</param>
	/// <returns>The display message.</returns>
	private static string GetDiagnosticMessage(DiagnosticPayload diagnosticPayload, DiagnosticMappingPolicy policy)
	{
		string? message = diagnosticPayload.Message?.Trim();

		return string.IsNullOrWhiteSpace(message) ? policy.UnknownMessage : message;
	}

	/// <summary>
	/// Gets the diagnostic source attribution, or <see langword="null"/> when the server supplied none.
	/// </summary>
	/// <param name="diagnosticPayload">The protocol diagnostic payload.</param>
	/// <returns>The trimmed source, or <see langword="null"/>.</returns>
	private static string? GetDiagnosticSource(DiagnosticPayload diagnosticPayload)
		=> OptionalText.Normalize(diagnosticPayload.Source);

	/// <summary>
	/// Gets the diagnostic code attribution, or <see langword="null"/> when the server supplied none.
	/// A string code is trimmed and a numeric code keeps its raw JSON text, so the value stays stable
	/// across number formats.
	/// </summary>
	/// <param name="diagnosticPayload">The protocol diagnostic payload.</param>
	/// <returns>The trimmed code, or <see langword="null"/>.</returns>
	private static string? GetDiagnosticCode(DiagnosticPayload diagnosticPayload)
	{
		string? code = diagnosticPayload.Code switch
		{
			{ ValueKind: JsonValueKind.String } codeElement => codeElement.GetString(),
			{ ValueKind: JsonValueKind.Number } codeElement => codeElement.GetRawText(),
			_ => null
		};

		return OptionalText.Normalize(code);
	}
}
