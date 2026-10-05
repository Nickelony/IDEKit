using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents the typed top-level workspace-symbol payload returned by a language server.</summary>
/// <remarks>
/// Contains every usable entry, in response order, whether the server answered with <c>SymbolInformation[]</c> or with <c>WorkspaceSymbol[]</c>.
/// An entry without a usable name, kind and document URI is skipped with a warning, and a payload that is not an array yields an empty response.
/// </remarks>
[JsonConverter(typeof(WorkspaceSymbolResponseJsonConverter))]
public sealed class WorkspaceSymbolResponse
{
	public WorkspaceSymbolResponse()
		: this(null) { }

	/// <param name="symbols">The usable symbol entries returned by the server.</param>
	public WorkspaceSymbolResponse(IReadOnlyList<WorkspaceSymbolPayload>? symbols) => Symbols = symbols is null ? [] : Array.AsReadOnly([.. symbols]);

	/// <summary>The usable symbol entries returned by the server, in response order.</summary>
	public IReadOnlyList<WorkspaceSymbolPayload> Symbols { get; }
}
