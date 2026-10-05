using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Groups the state and owner callbacks shared by every connection collaborator, so each collaborator takes one context instead of the full set.
/// </summary>
/// <param name="State">Provides session lookup and connection-generation fencing.</param>
/// <param name="Logger">The logger used for connection lifecycle and server-message diagnostics.</param>
/// <param name="CanAcceptServerMessagesForGeneration">Reports whether a connection generation still accepts server messages.</param>
/// <param name="TryMarkConnectionUnhealthy">Marks one observed connection generation unhealthy and reports whether it was still current.</param>
/// <param name="RaiseConnectionUnavailable">Raises the client connection-unavailable event for a lost ready connection.</param>
/// <param name="WorkspaceFolders">
/// The workspace folder descriptors advertised to the language server and used in the answers to server requests.
/// </param>
/// <param name="WorkspaceRootsDisplayText">The comma-joined normalized workspace root paths used in diagnostics.</param>
/// <param name="Lifetime">The lifetime of the owning client: its disposed state, its token and its disposal budget.</param>
/// <param name="Hooks">The host delegates that receive the server messages the client does not surface by itself.</param>
/// <remarks>
/// The hosting client builds one instance in its constructor and passes it to the connection collaborators; a collaborator adds
/// only the pieces unique to it (for example the connection timeouts, the settings provider, or the per-generation RPC target). This is distinct
/// from the public <see cref="LanguageServerConnectRequest"/>, which carries the complete input of one connect call to a transport.
/// </remarks>
internal sealed record ConnectionCollaboratorContext(
	ConnectionState State,
	ILogger Logger,
	Func<long, bool> CanAcceptServerMessagesForGeneration,
	Func<long, bool> TryMarkConnectionUnhealthy,
	Action<long> RaiseConnectionUnavailable,
	IReadOnlyList<WorkspaceFolder> WorkspaceFolders,
	string WorkspaceRootsDisplayText,
	ClientLifetime Lifetime,
	LanguageServerClientHooks Hooks
);
