using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>
/// Represents the <c>textDocument/signatureHelp</c> provider capability, which servers advertise as an options object or a boolean.
/// </summary>
/// <param name="IsSupported">Whether the server supports <c>textDocument/signatureHelp</c>.</param>
/// <param name="TriggerCharacters">The characters that open signature help when typed.</param>
/// <param name="RetriggerCharacters">The characters that update signature help that is already open.</param>
[JsonConverter(typeof(SignatureHelpProviderCapabilityJsonConverter))]
internal readonly record struct SignatureHelpProviderCapability(
	bool IsSupported,
	IReadOnlyList<string> TriggerCharacters,
	IReadOnlyList<string> RetriggerCharacters
);
