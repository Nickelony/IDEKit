using System.Diagnostics;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Binds a freshly spawned child process to a platform-specific lifetime guard so a host crash or an abrupt
/// teardown cannot strand it.
/// </summary>
/// <remarks>
/// <para>
/// On Windows the child is assigned to a shared kill-on-close job object (<see cref="WindowsJobObject"/>), so
/// the operating system terminates it when the host process exits or the last handle to the job is released.
/// No other platform exposes a comparable process-wide crash guard through this package, so there the child
/// relies on the transport's graceful shutdown/exit handshake and the forced
/// <see cref="Process.Kill(bool)"/> teardown, and the guard is a documented no-op.
/// </para>
/// <para>
/// The guard is selected once, when this type is first used, and is process-wide state; every client in the
/// process shares it.
/// </para>
/// </remarks>
internal static class ChildProcessLifetime
{
	private static readonly IChildProcessLifetimeGuard s_guard =
		OperatingSystem.IsWindows() ? WindowsJobObject.Instance : NoOpChildProcessLifetimeGuard.Instance;

	/// <summary>
	/// Gets the platform-default guard the transport uses when the host does not supply one.
	/// </summary>
	internal static IChildProcessLifetimeGuard DefaultGuard => s_guard;

	/// <summary>
	/// Restores the platform guard's process-wide state so a test can start from a clean slate.
	/// </summary>
	internal static void ResetForTests()
	{
		// The platform check keeps the Windows-only guard call reachable only on Windows (CA1416); s_guard is the
		// Windows guard exactly when the platform is Windows.
		if (OperatingSystem.IsWindows() && s_guard is WindowsJobObject windowsJobObject)
			windowsJobObject.ResetForTests();
	}

	private sealed class NoOpChildProcessLifetimeGuard : IChildProcessLifetimeGuard
	{
		internal static NoOpChildProcessLifetimeGuard Instance { get; } = new();

		public void Bind(Process process, ILogger? logger)
		{ }
	}
}
