using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.LanguageServer.Provider;

public abstract partial class LanguageServerIntelliSenseProviderBase
{
	/// <inheritdoc/>
	/// <remarks>
	/// The request record cannot carry protocol diagnostics context, so the provider reconstructs it from the
	/// diagnostics it currently has cached for the requested range, and only when the cached snapshot matches the
	/// request's <c>DocumentText</c> exactly. Because a server derives its quick-fix family from that context, quick
	/// fixes become available once diagnostics for the request's exact content have been published, and an empty
	/// context is sent when they have not; the context cannot be forced through the request.
	/// </remarks>
	public virtual Task<IReadOnlyList<TextCodeAction>> GetCodeActionsAsync(LanguageServerCodeActionRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		return SendDocumentRequestAsync<CodeActionsResponse?, IReadOnlyList<TextCodeAction>>(
			request.FilePath, request.DocumentText, LspMethodNames.CodeAction,
			supportsRequest: static client => client.SupportsCodeActions,
			buildParameters: textDocument => new CodeActionParams(
				textDocument,
				new ProtocolRangePayload(
					new ProtocolPosition(request.Range.Start.Line, request.Range.Start.Character),
					new ProtocolPosition(request.Range.End.Line, request.Range.End.Character)),
				// The protocol context carries the diagnostics currently reported for the requested
				// range, because a server derives its quick-fix family from them, so an empty context
				// would suppress it. The build runs inside the parameter factory, after the request gates.
				new CodeActionContextPayload(BuildCodeActionContextDiagnostics(request))),
			parseResponse: response => ResponseParser.ParseCodeActions(response, Logger),
			fallbackValue: [],
			cancellationToken);
	}

	private List<DiagnosticPayload> BuildCodeActionContextDiagnostics(LanguageServerCodeActionRequest request)
	{
		// A reversed request range cannot describe a selection; the context stays empty instead of
		// guessing which diagnostics were meant.
		if (IsPositionAfter(request.Range.Start, request.Range.End))
			return [];

		(IReadOnlyList<DiagnosticEntry> diagnostics, string? sourceContent) = GetDiagnosticsSnapshot(request.FilePath);

		if (diagnostics.Count == 0)
			return [];

		// The cached diagnostics' offsets refer to the snapshot they were parsed against (the server's
		// synchronized view when the payload arrived). The request pipeline synchronizes the server to
		// the request's text before this factory runs, so a snapshot that differs from that text
		// describes a document state the server no longer holds: its positions neither match the
		// document the request identifies nor intersect the requested range reliably. The context then
		// stays empty until a publish for the matching content arrives, instead of sending the server
		// coordinates for different text.
		if (!string.Equals(sourceContent, request.DocumentText, StringComparison.Ordinal))
			return [];

		TextLineMap lineMap = TextLineMap.Build(request.DocumentText);
		var result = new List<DiagnosticPayload>();

		for (int i = 0; i < diagnostics.Count; i++)
		{
			DiagnosticEntry entry = diagnostics[i];
			TextDiagnostic diagnostic = entry.Diagnostic;

			TextPositionRange diagnosticRange = new(
				lineMap.GetPosition(diagnostic.StartOffset),
				lineMap.GetPosition(diagnostic.EndOffset));

			// A diagnostic that does not intersect the requested range is not part of this request.
			if (!RangesIntersect(diagnosticRange, request.Range))
				continue;

			// Echo the server's own payload and remap only the range against the request snapshot. The
			// protocol fields the shared diagnostic does not model (data, tags, codeDescription,
			// relatedInformation) are exactly what a server derives its quick-fix families from, so
			// rebuilding the payload from the display fields would silently suppress them. The raw code
			// element keeps its original JSON kind; serializing the display string instead would turn a
			// numeric code into a string the server cannot match, and a code kind the display attribution
			// does not model is omitted rather than echoed back unrepresentable.
			result.Add(entry.Payload with
			{
				Range = new ProtocolRangePayload(
					new ProtocolPosition(diagnosticRange.Start.Line, diagnosticRange.Start.Character),
					new ProtocolPosition(diagnosticRange.End.Line, diagnosticRange.End.Character)),
				Code = entry.HasCode ? entry.Code : null
			});
		}

		return result;
	}

	// LSP ranges are half-open, so a diagnostic that only touches the requested range's boundary (its end
	// equals the range start, or its start equals the range end) does not intersect it.
	private static bool RangesIntersect(TextPositionRange left, TextPositionRange right)
		=> IsPositionBefore(left.Start, right.End) && IsPositionBefore(right.Start, left.End);

	private static bool IsPositionBefore(TextPosition left, TextPosition right)
		=> left.Line < right.Line || (left.Line == right.Line && left.Character < right.Character);

	private static bool IsPositionAfter(TextPosition left, TextPosition right)
		=> left.Line > right.Line || (left.Line == right.Line && left.Character > right.Character);
}
