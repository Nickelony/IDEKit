using System.Diagnostics;

namespace Nickelony.LanguageServer.Client;

/// <summary>Provides the platform-default child-process lifetime guard used when a host configures none.</summary>
/// <remarks>
/// <para>
/// On Windows the child is assigned to a shared kill-on-close job object (<see cref="WindowsJobObject"/>),
/// so the operating system terminates it when the host process exits or the last handle to the job is released. No other platform
/// exposes a comparable process-wide crash guard through this package, so there the child relies on the client's graceful shutdown/exit handshake
/// and the forced <see cref="Process.Kill(bool)"/> teardown, and the guard is a documented no-op.
/// </para>
/// <para>
/// The guard is selected once, when this type is first used, and is process-wide state; every client in the process shares it.
/// </para>
/// </remarks>
internal static class PlatformChildProcessGuard
{
	/// <summary>The platform-default guard the transport uses when the host does not supply one.</summary>
	internal static IChildProcessLifetimeGuard Instance { get; } =
		OperatingSystem.IsWindows() ? WindowsJobObject.Instance : NoOpChildProcessLifetimeGuard.Instance;

	private sealed class NoOpChildProcessLifetimeGuard : IChildProcessLifetimeGuard
	{
		internal static NoOpChildProcessLifetimeGuard Instance { get; } = new();
		public void Bind(Process process, ILogger? logger) { }
	}
}
