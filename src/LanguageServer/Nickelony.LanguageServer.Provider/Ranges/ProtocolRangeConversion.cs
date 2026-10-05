using Nickelony.IDEKit.Core.Text;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Converts between LSP protocol ranges and the zero-based text-position ranges used by shared editor payloads.</summary>
/// <remarks>
/// LSP positions are zero-based line and character coordinates, so the conversion validates the protocol values and copies them without changing
/// their basis.
/// </remarks>
internal static class ProtocolRangeConversion
{
	/// <summary>Converts a protocol range payload into a zero-based text-position range.</summary>
	/// <param name="rangePayload">The protocol range payload to convert.</param>
	/// <param name="range">Receives the converted range.</param>
	/// <returns>
	/// <see langword="true"/> when the range payload contained non-negative start and end positions and the end is not positioned
	/// before the start; otherwise, <see langword="false"/>.
	/// </returns>
	internal static bool TryGetTextPositionRange(RangePayload? rangePayload, out TextPositionRange range)
	{
		range = default;

		if (
			rangePayload is not { } payload
			|| !ProtocolPositionConversion.TryGetTextPosition(payload.Start, out TextPosition start)
			|| !ProtocolPositionConversion.TryGetTextPosition(payload.End, out TextPosition end)
		)
		{
			return false;
		}

		var converted = new TextPositionRange(start, end);

		// An inverted range identifies no content; converting it would turn a malformed payload into a real edit.
		if (converted.IsReversed)
			return false;

		range = converted;
		return true;
	}

	/// <summary>Converts a protocol range payload into a zero-based offset range against the document that a line map describes.</summary>
	/// <param name="rangePayload">The protocol range payload to convert.</param>
	/// <param name="lineMap">The line map of the document text the range addresses.</param>
	/// <param name="offsetRange">Receives the converted range.</param>
	/// <returns>
	/// <see langword="true"/> when the range has non-negative coordinates and its end does not precede its start, both on the raw coordinates
	/// and after the line map clamps them; otherwise, <see langword="false"/>.
	/// </returns>
	/// <remarks>
	/// <para>
	/// This is the rule of a range that only selects or highlights text, such as a hover range or an outline range: a malformed range is rejected
	/// on its raw coordinates, and a stale one is clamped to the document, a line past the last line to the last line and a character past the end
	/// of a line to the line end. A character is one UTF-16 code unit, as a .NET string index is, so no encoding conversion applies.
	/// </para>
	/// <para>
	/// Two other rules differ on purpose. A completion edit drops a range whose line lies beyond the document
	/// instead of clamping it, because the edit commits text and a clamped target would insert it at the wrong place. A diagnostic clamps
	/// a negative coordinate to zero and anchors an inverted range at its start instead of rejecting it, because a stale or malformed diagnostic
	/// must still be shown.
	/// </para>
	/// </remarks>
	internal static bool TryGetOffsetRange(RangePayload? rangePayload, TextLineMap lineMap, out TextRange offsetRange)
	{
		offsetRange = default;
		return TryGetTextPositionRange(rangePayload, out TextPositionRange positionRange) && lineMap.TryGetOffsets(positionRange, out offsetRange);
	}

	/// <summary>Converts a text-position range into a protocol range, clamping negative coordinates to zero.</summary>
	/// <param name="range">The text-position range to convert.</param>
	/// <returns>The protocol range with non-negative coordinates.</returns>
	internal static RangePayload ToProtocolRange(TextPositionRange range) =>
		new(ProtocolPositionConversion.ToProtocolPosition(range.Start), ProtocolPositionConversion.ToProtocolPosition(range.End));
}
