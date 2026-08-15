using System.Diagnostics;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Binds a freshly spawned child process to a platform-specific lifetime guard so a host crash or an abrupt
/// teardown cannot strand it.
/// </summary>
/// <remarks>
/// <para>
/// The default guard is selected once per host: on Windows the child joins a shared kill-on-close job object, and
/// every other platform uses a documented no-op because no comparable process-wide crash guard exists through this
/// package. A host can replace the default by assigning
/// <see cref="LanguageServerClientOptions.ChildProcessLifetimeGuardFactory"/>; the resolved guard is carried to the
/// transport on <see cref="LanguageServerTransportContext.ChildProcessLifetimeGuard"/>.
/// </para>
/// <para>
/// A guard never throws: a failure to bind is reported through the supplied logger so the transport can continue.
/// Implementations are invoked on the transport's startup path, so a bind must be cheap and must not block.
/// </para>
/// </remarks>
public interface IChildProcessLifetimeGuard
{
	/// <summary>
	/// Tries to bind <paramref name="process"/> to the guard's lifetime mechanism.
	/// </summary>
	/// <param name="process">The child process to bind.</param>
	/// <param name="logger">The logger that receives this binding's diagnostics, or <see langword="null"/> for a no-op logger.</param>
	void Bind(Process process, ILogger? logger);
}
