using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents the parameters of the LSP initialize request.</summary>
/// <param name="ProcessId">The identifier of the client process, which the server may watch to exit when the client does.</param>
/// <param name="InitializationOptions">The host's initialization options, or <see langword="null"/> when the host supplied none.</param>
/// <param name="RootUri">The URI of the primary workspace root, or <see langword="null"/> for a folderless session.</param>
/// <param name="WorkspaceFolders">The workspace folders in the caller's order, or <see langword="null"/> for a folderless session.</param>
/// <param name="Capabilities">The client capabilities after the client enforced the ones it cannot honor.</param>
/// <remarks>
/// A member that is <see langword="null"/> is written as a JSON <c>null</c>, which the protocol allows for each of them.
/// </remarks>
internal readonly record struct InitializeParams(
	[property: JsonPropertyName("processId")] int ProcessId,
	[property: JsonPropertyName("initializationOptions")] JsonNode? InitializationOptions,
	[property: JsonPropertyName("rootUri")] string? RootUri,
	[property: JsonPropertyName("workspaceFolders")] IReadOnlyList<WorkspaceFolder>? WorkspaceFolders,
	[property: JsonPropertyName("capabilities")] JsonObject Capabilities
);
