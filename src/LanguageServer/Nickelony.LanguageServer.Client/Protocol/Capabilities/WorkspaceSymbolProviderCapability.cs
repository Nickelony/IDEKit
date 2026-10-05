using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>
/// Represents the <c>workspace/symbol</c> provider capability, which the server may advertise as either a boolean or an options object.
/// </summary>
/// <param name="IsSupported">Whether the server supports <c>workspace/symbol</c>.</param>
/// <param name="ResolveProvider">
/// Whether the server supports <c>workspaceSymbol/resolve</c>, or <see langword="null"/> when the server did not advertise the flag.
/// </param>
[JsonConverter(typeof(WorkspaceSymbolProviderCapabilityJsonConverter))]
internal readonly record struct WorkspaceSymbolProviderCapability(bool IsSupported, bool? ResolveProvider);
