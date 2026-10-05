namespace Nickelony.LanguageServer.Client;

/// <summary>Opens the connection a client uses to exchange protocol messages with a language server.</summary>
/// <remarks>
/// <para>
/// <see cref="StdioLanguageServerTransport"/> launches a server executable as a child process and speaks the protocol over its standard streams.
/// A custom transport connects over a different channel, for example to a server process the host already owns, a local socket,
/// or an in-memory channel. The transport holds what it connects to; the client passes it only a <see cref="LanguageServerConnectRequest"/>.
/// </para>
/// <para>
/// The transport owns every resource it creates until it returns the connection: when <see cref="ConnectAsync"/> throws or is canceled,
/// it must release whatever it already created. After it returns, the client owns the connection and disposes it during teardown.
/// </para>
/// <para>
/// A connection attempt that fails is reported by the client as a failed startup with the thrown
/// exception exposed through <c>ILanguageServerClient.LastStartupException</c>, and the client's startup-failure log names the transport
/// through its <see cref="object.ToString"/>. Implementations may run on a background thread during startup and must be thread-safe when one instance
/// is shared by several clients.
/// </para>
/// </remarks>
public interface ILanguageServerTransport
{
	/// <summary>Opens a new connection to the language server.</summary>
	/// <param name="request">What the client supplies for this connection.</param>
	/// <param name="cancellationToken">
	/// A token that can cancel the connection attempt. A canceled attempt must release everything it already created.
	/// </param>
	/// <returns>The open connection to the language server.</returns>
	Task<ILanguageServerConnection> ConnectAsync(LanguageServerConnectRequest request, CancellationToken cancellationToken);
}
