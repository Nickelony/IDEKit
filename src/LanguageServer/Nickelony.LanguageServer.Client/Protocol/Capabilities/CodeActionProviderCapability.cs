using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents the <c>textDocument/codeAction</c> provider capability, which the server may advertise as either a
/// boolean or an options object.
/// </summary>
/// <param name="IsSupported">Whether the server supports <c>textDocument/codeAction</c>.</param>
/// <param name="ResolveProvider">Whether the server supports <c>codeAction/resolve</c>, or <see langword="null"/>
/// when the server did not advertise the flag.</param>
[JsonConverter(typeof(CodeActionProviderCapabilityJsonConverter))]
internal readonly record struct CodeActionProviderCapability(bool IsSupported, bool? ResolveProvider);
