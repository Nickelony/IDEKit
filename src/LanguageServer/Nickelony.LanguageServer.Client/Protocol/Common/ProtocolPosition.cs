using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a zero-based protocol position within a text document.
/// </summary>
/// <param name="Line">The zero-based line index.</param>
/// <param name="Character">The zero-based character index on the line, measured in UTF-16 code units.</param>
public readonly record struct ProtocolPosition(
	[property: JsonPropertyName("line")] int Line,
	[property: JsonPropertyName("character")] int Character);
