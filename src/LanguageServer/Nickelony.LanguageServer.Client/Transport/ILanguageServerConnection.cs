using System.Diagnostics;

namespace Nickelony.LanguageServer.Client;

/// <summary>Represents one open connection to a language server.</summary>
/// <remarks>
/// <para>
/// The connection owns the streams, and the server process when it launched one, that carry the protocol. The client disposes it during teardown,
/// after the graceful shutdown sequence and before it waits for its background read loops.
/// </para>
/// <para>
/// <see cref="IAsyncDisposable.DisposeAsync"/> must release the streams and any owned process so those read loops can finish, and it must tolerate
/// being called more than once.
/// </para>
/// </remarks>
public interface ILanguageServerConnection : IAsyncDisposable
{
	/// <summary>The stream the client reads server responses and notifications from.</summary>
	Stream ReadStream { get; }

	/// <summary>The stream the client writes requests and notifications to.</summary>
	Stream WriteStream { get; }

	/// <summary>The server process when the connection is backed by a spawned process; otherwise, <see langword="null"/>.</summary>
	/// <remarks>
	/// The client binds the process lifetime to the connection: it watches the process for an unexpected exit and terminates it during teardown
	/// when the graceful shutdown sequence did not end it. A connection without a process reports no liveness signal, so after the graceful shutdown
	/// sequence the client relies on <see cref="IAsyncDisposable.DisposeAsync"/> alone to end the connection. The client reads the server's standard
	/// error from the process, so a connection without one gets no standard-error diagnostics.
	/// </remarks>
	Process? Process { get; }
}
