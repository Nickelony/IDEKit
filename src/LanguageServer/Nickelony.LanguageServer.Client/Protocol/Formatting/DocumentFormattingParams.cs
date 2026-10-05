using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents the formatting options sent with a document-formatting request.</summary>
/// <param name="TabSize">The indentation size in columns.</param>
/// <param name="InsertSpaces">Whether indentation should use spaces instead of tabs.</param>
/// <remarks>
/// Carries only <c>tabSize</c> and <c>insertSpaces</c>; the optional LSP options are not modeled, and no formatting defaults are applied here.
/// </remarks>
public readonly record struct FormattingOptionsPayload(
	[property: JsonPropertyName("tabSize")] int TabSize,
	[property: JsonPropertyName("insertSpaces")] bool InsertSpaces
);

/// <param name="TextDocument">The targeted document.</param>
/// <param name="Options">The formatting options to apply.</param>
public readonly record struct DocumentFormattingParams(
	[property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
	[property: JsonPropertyName("options")] FormattingOptionsPayload Options
);
