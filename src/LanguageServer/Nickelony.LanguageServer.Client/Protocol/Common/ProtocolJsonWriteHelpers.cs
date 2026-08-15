using System.Text.Json;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Provides shared write helpers for protocol payload serialization.
/// </summary>
/// <remarks>
/// The helpers keep the wire shape of positions and ranges identical across every converter that round-trips the
/// shared <see cref="ProtocolRangePayload"/> and <see cref="ProtocolPosition"/> models.
/// </remarks>
internal static class ProtocolJsonWriteHelpers
{
	/// <summary>
	/// Writes one range object with its start and end positions.
	/// </summary>
	/// <param name="writer">The writer that receives the range object.</param>
	/// <param name="range">The range to write.</param>
	internal static void WriteRange(Utf8JsonWriter writer, ProtocolRangePayload range)
	{
		writer.WriteStartObject();
		WritePosition(writer, "start", range.Start);
		WritePosition(writer, "end", range.End);
		writer.WriteEndObject();
	}

	/// <summary>
	/// Writes one named position object.
	/// </summary>
	/// <param name="writer">The writer that receives the position object.</param>
	/// <param name="propertyName">The property name that carries the position.</param>
	/// <param name="position">The position to write.</param>
	internal static void WritePosition(Utf8JsonWriter writer, string propertyName, ProtocolPosition position)
	{
		writer.WritePropertyName(propertyName);
		writer.WriteStartObject();
		writer.WriteNumber("line", position.Line);
		writer.WriteNumber("character", position.Character);
		writer.WriteEndObject();
	}
}
