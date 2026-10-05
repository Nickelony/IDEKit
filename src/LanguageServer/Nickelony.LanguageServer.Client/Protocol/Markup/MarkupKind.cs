using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Describes how markup text is interpreted, using the LSP <c>MarkupKind</c> mapping.</summary>
/// <remarks>
/// The protocol spells the values <c>plaintext</c> and <c>markdown</c>, and they serialize that way. A value the protocol does not define
/// reads as <see cref="PlainText"/>, the protocol's default for an absent kind.
/// </remarks>
[JsonConverter(typeof(MarkupKindJsonConverter))]
public enum MarkupKind
{
	PlainText = 0,
	Markdown = 1,
}
