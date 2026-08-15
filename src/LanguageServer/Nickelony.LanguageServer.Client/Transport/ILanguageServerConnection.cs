using System.Diagnostics;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents one open connection to a language server.
/// </summary>
/// <remarks>
/// <para>
/// The connection owns the streams, and the server process when it launched one, that carry the protocol. The
/// client disposes it during teardown, after the graceful shutdown sequence and before it waits for its background
/// read loops.
/// </para>
/// <para>
/// <see cref="IAsyncDisposable.DisposeAsync"/> must release the streams and any owned process so those read loops
/// can finish, and it must tolerate being called more than once.
/// </para>
/// </remarks>
public interface ILanguageServerConnection : IAsyncDisposable
{
	/// <summary>
	/// Gets the stream the client reads server responses and notifications from.
	/// </summary>
	Stream ReadStream { get; }

	/// <summary>
	/// Gets the stream the client writes requests and notifications to.
	/// </summary>
	Stream WriteStream { get; }

	/// <summary>
	/// Gets the stream carrying the server's standard-error output, or <see langword="null"/> when the connection
	/// has none.
	/// </summary>
	Stream? ErrorStream { get; }

	/// <summary>
	/// Gets the server process when the connection is backed by a spawned process; otherwise, <see langword="null"/>.
	/// </summary>
	/// <remarks>
	/// The client binds the process lifetime to the connection: it watches the process for an unexpected exit and
	/// terminates it during teardown when the graceful shutdown sequence did not end it. A connection without a
	/// process reports no liveness signal, so teardown relies on <see cref="IAsyncDisposable.DisposeAsync"/> alone.
	/// </remarks>
	Process? Process { get; }
}
