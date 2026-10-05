using Nickelony.IDEKit.Core.Text;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Converts between the editor's offsets and positions and the line and character position the protocol addresses.</summary>
internal static class ProtocolPositionConversion
{
	/// <summary>Converts an editor position to a protocol position, clamping negative coordinates to zero.</summary>
	/// <param name="position">The editor position to convert.</param>
	/// <returns>The protocol position with non-negative coordinates.</returns>
	internal static PositionPayload ToProtocolPosition(TextPosition position)
	{
		TextPosition clampedPosition = position.ClampNegativeToZero();
		return new PositionPayload(clampedPosition.Line, clampedPosition.Character);
	}

	/// <summary>Converts a protocol position into a zero-based text position.</summary>
	/// <param name="position">The protocol position to convert.</param>
	/// <param name="textPosition">Receives the converted text position.</param>
	/// <returns><see langword="true"/> when the protocol position contained non-negative line and character values.</returns>
	internal static bool TryGetTextPosition(PositionPayload position, out TextPosition textPosition)
	{
		textPosition = default;

		if (position.Line < 0 || position.Character < 0)
			return false;

		textPosition = new TextPosition(position.Line, position.Character);
		return true;
	}
}
