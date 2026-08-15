namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Opens the connection a <see cref="LanguageServerClient"/> uses to exchange protocol messages with a language server.
/// </summary>
/// <remarks>
/// <para>
/// The default implementation, <see cref="StdioLanguageServerTransport"/>, launches the configured server
/// executable as a child process and speaks the protocol over its standard streams. Assign a custom transport to
/// <see cref="LanguageServerClientOptions.Transport"/> to connect over a different channel, for example to a server
/// process the host already owns, a local socket, or an in-memory channel.
/// </para>
/// <para>
/// The transport owns every resource it creates until it returns the connection: when
/// <see cref="ConnectAsync"/> throws or is canceled, it must release whatever it already created. After it
/// returns, the <see cref="LanguageServerClient"/> owns the connection and disposes it during teardown.
/// </para>
/// <para>
/// A connection attempt that fails is reported by the client as a failed startup with the thrown exception
/// exposed through <see cref="LanguageServerClient.LastStartupException"/>. Implementations may run on a
/// background thread during startup and must be thread-safe when one instance is shared by several clients.
/// </para>
/// </remarks>
public interface ILanguageServerTransport
{
	/// <summary>
	/// Opens a new connection to the language server described by <paramref name="context"/>.
	/// </summary>
	/// <param name="context">The server configuration, workspace roots, and logger for this connection.</param>
	/// <param name="cancellationToken">
	/// A token that can cancel the connection attempt. A canceled attempt must release everything it already created.
	/// </param>
	/// <returns>The open connection to the language server.</returns>
	Task<ILanguageServerConnection> ConnectAsync(LanguageServerTransportContext context, CancellationToken cancellationToken);
}
