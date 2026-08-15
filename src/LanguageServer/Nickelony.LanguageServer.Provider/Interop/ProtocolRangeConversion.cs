using Nickelony.IDEKit.Core.Text;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Converts LSP protocol positions and ranges into the zero-based text positions used by shared editor payloads.
/// </summary>
/// <remarks>
/// LSP positions are zero-based line and character coordinates, so the conversion validates the
/// protocol values and copies them without changing their basis.
/// </remarks>
public static class ProtocolRangeConversion
{
	/// <summary>
	/// Converts a protocol range payload into a zero-based text-position range.
	/// </summary>
	/// <param name="rangePayload">The protocol range payload to convert.</param>
	/// <param name="range">Receives the converted range.</param>
	/// <returns>
	/// <see langword="true"/> when the range payload contained non-negative start and end positions and the end
	/// is not positioned before the start; otherwise, <see langword="false"/>.
	/// </returns>
	public static bool TryGetTextPositionRange(ProtocolRangePayload? rangePayload, out TextPositionRange range)
	{
		range = default;

		if (rangePayload is not { } payload
			|| !TryGetTextPosition(payload.Start, out TextPosition start)
			|| !TryGetTextPosition(payload.End, out TextPosition end))
		{
			return false;
		}

		// An inverted range identifies no content; converting it would turn a malformed payload into a real edit.
		if (end.Line < start.Line || (end.Line == start.Line && end.Character < start.Character))
			return false;

		range = new TextPositionRange(start, end);
		return true;
	}

	/// <summary>
	/// Converts a protocol position into a zero-based text position.
	/// </summary>
	/// <param name="position">The protocol position to convert.</param>
	/// <param name="textPosition">Receives the converted text position.</param>
	/// <returns><see langword="true"/> when the protocol position contained non-negative line and character values.</returns>
	public static bool TryGetTextPosition(ProtocolPosition position, out TextPosition textPosition)
	{
		textPosition = default;

		if (position.Line < 0 || position.Character < 0)
			return false;

		textPosition = new TextPosition(position.Line, position.Character);
		return true;
	}
}
