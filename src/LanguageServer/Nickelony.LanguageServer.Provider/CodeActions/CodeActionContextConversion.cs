using System.Text.Json;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Projects the diagnostics and the kind filter that a code-action request reports onto the protocol context
/// of a <c>textDocument/codeAction</c> request.
/// </summary>
internal static class CodeActionContextConversion
{
	/// <summary>Builds the protocol context for the range that a code-action request names.</summary>
	/// <param name="request">The code-action request whose range selects the diagnostics.</param>
	/// <param name="lineMap">The line map of the request's text, which maps each diagnostic range back to line and character positions.</param>
	/// <param name="diagnostics">The cached diagnostics of the document.</param>
	/// <param name="sourceContent">The content that the cached diagnostics' offsets refer to, or <see langword="null"/> when none is cached.</param>
	/// <returns>
	/// A context of the diagnostics the request names, or, when it names none, those cached diagnostics that intersect the requested range,
	/// or an empty context when the cache describes other text. The context carries the request's kind filter.
	/// </returns>
	/// <remarks>
	/// A diagnostic the request names is echoed from the cache when the cache holds an equal diagnostic for the request's text, because the server
	/// derives its quick fixes from fields that the shared diagnostic does not model; otherwise, it is built from the fields the shared
	/// diagnostic has.
	/// </remarks>
	internal static CodeActionContextPayload ToProtocol(
		CodeActionRequest request,
		TextLineMap lineMap,
		IReadOnlyList<DiagnosticEntry> diagnostics,
		string? sourceContent
	)
	{
		// The cached diagnostics' offsets refer to the snapshot they were parsed against (the server's
		// synchronized view when the payload arrived). The request pipeline synchronizes the server to the
		// request's text before the parameters are built, so a snapshot that differs from that text describes a
		// document state the server does not hold: its positions neither match the document the request
		// identifies nor intersect the requested range reliably. The cache stays unused until a publish for
		// the matching content arrives, instead of sending coordinates for different text.
		bool cacheMatchesRequest = diagnostics.Count > 0 && string.Equals(sourceContent, request.DocumentText, StringComparison.Ordinal);

		if (request.Diagnostics is { } named)
		{
			return new CodeActionContextPayload(
				[.. named.Select(diagnostic => ToProtocol(diagnostic, lineMap, cacheMatchesRequest ? diagnostics : []))],
				request.Only
			);
		}

		if (!cacheMatchesRequest)
			return new CodeActionContextPayload([], request.Only);

		var result = new List<DiagnosticPayload>();

		for (int i = 0; i < diagnostics.Count; i++)
		{
			DiagnosticEntry entry = diagnostics[i];

			// A diagnostic that does not intersect the requested range is not part of this request.
			if (entry.Diagnostic.Range.Intersects(request.Range))
				result.Add(ToProtocol(entry, lineMap));
		}

		return new CodeActionContextPayload(result, request.Only);
	}

	private static DiagnosticPayload ToProtocol(Diagnostic diagnostic, TextLineMap lineMap, IReadOnlyList<DiagnosticEntry> cached)
	{
		for (int i = 0; i < cached.Count; i++)
		{
			if (cached[i].Diagnostic.Equals(diagnostic))
				return ToProtocol(cached[i], lineMap);
		}

		return new DiagnosticPayload(
			Range: ToProtocolRange(diagnostic.Range, lineMap),
			Severity: DiagnosticSeverityConversion.ToProtocol(diagnostic.Severity),
			Message: diagnostic.Message,
			Source: diagnostic.Source,
			Code: diagnostic.Code is { } code ? JsonSerializer.SerializeToElement(code) : null
		);
	}

	private static DiagnosticPayload ToProtocol(DiagnosticEntry entry, TextLineMap lineMap)
	{
		// Echo the server's own payload and remap only the range against the request snapshot. The protocol
		// fields the shared diagnostic does not model (data, tags, codeDescription, relatedInformation) are
		// exactly what a server derives its quick-fix families from, so rebuilding the payload from the
		// display fields would suppress them. The raw code keeps its original JSON kind; serializing the
		// display string would turn a numeric code into a string the server cannot match, and a code kind the
		// display attribution does not model is omitted rather than echoed unrepresentable.
		return entry.Payload with { Range = ToProtocolRange(entry.Diagnostic.Range, lineMap), Code = entry.HasCode ? entry.Code : null };
	}

	private static RangePayload ToProtocolRange(TextRange range, TextLineMap lineMap) =>
		ProtocolRangeConversion.ToProtocolRange(lineMap.GetPositionRange(range));
}
