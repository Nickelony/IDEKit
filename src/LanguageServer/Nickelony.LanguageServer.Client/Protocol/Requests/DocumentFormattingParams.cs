using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents the formatting options sent with a document-formatting request.
/// </summary>
/// <remarks>
/// This is the wire projection of the authoritative <c>TextFormattingOptions</c> contract in the abstractions
/// package; that contract owns the tab-size defaulting rule, and the protocol package cannot reference it, so
/// the mapping lives in the provider bridge. Do not add formatting defaults here.
/// </remarks>
/// <param name="TabSize">The indentation size in columns.</param>
/// <param name="InsertSpaces">Whether indentation should use spaces instead of tabs.</param>
public readonly record struct FormattingOptionsPayload(
	[property: JsonPropertyName("tabSize")] int TabSize,
	[property: JsonPropertyName("insertSpaces")] bool InsertSpaces);

/// <summary>
/// Represents a document-formatting request payload.
/// </summary>
/// <param name="TextDocument">The targeted document.</param>
/// <param name="Options">The formatting options to apply.</param>
public readonly record struct DocumentFormattingParams(
	[property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
	[property: JsonPropertyName("options")] FormattingOptionsPayload Options);
