using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Wraps a Windows job object configured with <c>JobObjectLimitKillOnJobClose</c> so that any child processes assigned to it are forcibly terminated
/// when the host application crashes or the last handle to the job is released.
/// </summary>
/// <remarks>
/// <para>
/// The job is a best-effort lifetime guard for stranded server processes, so the children are terminated even when the host
/// never runs its disposal path. The job handle is process-wide state shared by every client instance: all language-server processes of one process
/// are assigned to the same job object and the handle intentionally lives until the process ends. The diagnostics are not shared state -
/// <see cref="Bind"/> reports through the calling client's own logger, so one client never silences another's job-object diagnostics.
/// </para>
/// <para>
/// This is the Windows implementation of <see cref="IChildProcessLifetimeGuard"/>; every other platform uses the no-op guard
/// in <see cref="PlatformChildProcessGuard"/>.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsJobObject : IChildProcessLifetimeGuard, IDisposable
{
	private const uint JobObjectLimitKillOnJobClose = 0x2000;

	private readonly object _syncRoot = new();
	private IntPtr _jobHandle = IntPtr.Zero;

	private bool _initializationFailed;
	private bool _isDisposed;

	private EventHandler? _processExitHandler;

	internal WindowsJobObject() { }

	/// <summary>The job object that every client of the process shares.</summary>
	/// <remarks>
	/// Nothing disposes it, so its handle lives until the process ends. A test that needs a job of its own creates an instance.
	/// </remarks>
	internal static WindowsJobObject Instance { get; } = new();

	/// <summary>Releases the job handle, which terminates every process still assigned to the job (kill-on-close).</summary>
	/// <remarks>
	/// A disposed instance assigns no process, so a late assignment cannot create a job that nothing releases.
	/// </remarks>
	public void Dispose()
	{
		lock (_syncRoot)
		{
			_isDisposed = true;

			if (_processExitHandler is { } handler)
			{
				AppDomain.CurrentDomain.ProcessExit -= handler;
				_processExitHandler = null;
			}

			ReleaseJobHandle();
		}
	}

	/// <summary>Tries to assign the supplied process to the shared kill-on-close Windows job object.</summary>
	/// <param name="process">The process to attach.</param>
	/// <param name="logger">The logger that receives this assignment's job-object diagnostics, or <see langword="null"/> for a no-op logger.</param>
	/// <remarks>
	/// The job object itself is process-wide, but its diagnostics are not: every message this assignment produces
	/// is written to <paramref name="logger"/>, so an assignment failure reaches the client that made it rather than the client that initialized
	/// the shared job.
	/// </remarks>
	public void Bind(Process process, ILogger? logger)
	{
		ILogger diagnosticsLogger = logger ?? NullLogger.Instance;

		try
		{
			IntPtr jobHandle = EnsureJobHandle(diagnosticsLogger);

			if (jobHandle == IntPtr.Zero)
				return;

			if (!AssignProcessToJobObject(jobHandle, process.Handle))
			{
				int errorCode = Marshal.GetLastWin32Error();

				// ERROR_ACCESS_DENIED is expected when the process is already inside an unbreakable job.
				diagnosticsLogger.LogDebug(
					"AssignProcessToJobObject failed with Win32 error {ErrorCode} for the language-server process.",
					errorCode
				);
			}
		}
		catch (Exception exception)
		{
			diagnosticsLogger.LogDebug(exception, "Failed to bind the language-server process to the kill-on-close job object.");
		}
	}

	/// <summary>Creates or returns the shared kill-on-close job-object handle.</summary>
	/// <param name="logger">The logger that receives the shared-state diagnostics for this call.</param>
	/// <returns>The shared job handle, or <see cref="IntPtr.Zero"/> when initialization failed.</returns>
	private IntPtr EnsureJobHandle(ILogger logger)
	{
		// Double-checked initialization: the lock publishes the handle and the failure flag, while concurrent
		// callers read them with acquire semantics so a half-initialized state is never observed.
		IntPtr jobHandle = Volatile.Read(ref _jobHandle);

		if (jobHandle != IntPtr.Zero)
			return jobHandle;

		if (Volatile.Read(ref _initializationFailed))
			return IntPtr.Zero;

		lock (_syncRoot)
		{
			if (_jobHandle != IntPtr.Zero)
				return _jobHandle;

			if (_initializationFailed || _isDisposed)
				return IntPtr.Zero;

			IntPtr handle = CreateJobObject(IntPtr.Zero, lpName: null);

			if (handle == IntPtr.Zero)
			{
				_initializationFailed = true;

				logger.LogDebug(
					"CreateJobObject returned NULL (Win32 error {ErrorCode}); the language server will rely on graceful shutdown.",
					Marshal.GetLastWin32Error()
				);

				return IntPtr.Zero;
			}

			JobObjectExtendedLimitInformation extendedLimit = default;
			extendedLimit.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;

			int payloadSize = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
			IntPtr payloadPointer = Marshal.AllocHGlobal(payloadSize);

			try
			{
				Marshal.StructureToPtr(extendedLimit, payloadPointer, fDeleteOld: false);

				if (
					!SetInformationJobObject(
						hJob: handle,
						infoType: JobObjectInformationClass.ExtendedLimitInformation,
						lpJobObjectInfo: payloadPointer,
						cbJobObjectInfoLength: (uint)payloadSize
					)
				)
				{
					int errorCode = Marshal.GetLastWin32Error();

					CloseHandle(handle);
					_initializationFailed = true;

					logger.LogDebug(
						"SetInformationJobObject failed with Win32 error {ErrorCode}; the language server will rely on graceful shutdown.",
						errorCode
					);

					return IntPtr.Zero;
				}
			}
			finally
			{
				Marshal.FreeHGlobal(payloadPointer);
			}

			_jobHandle = handle;
			_processExitHandler = (_, _) => ReleaseJobHandle();
			AppDomain.CurrentDomain.ProcessExit += _processExitHandler;

			return handle;
		}
	}

	/// <summary>Closes the shared job handle, if one exists, and terminates every process still assigned to it.</summary>
	private void ReleaseJobHandle()
	{
		// Exchanging the field first makes the release idempotent: a repeated release (the process-exit handler and
		// a test reset can each run) observes IntPtr.Zero and never closes a handle whose value was reused.
		IntPtr handle = Interlocked.Exchange(ref _jobHandle, IntPtr.Zero);

		if (handle != IntPtr.Zero)
			CloseHandle(handle);
	}

	private enum JobObjectInformationClass
	{
		ExtendedLimitInformation = 9,
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct IoCounters
	{
		internal ulong ReadOperationCount;
		internal ulong WriteOperationCount;
		internal ulong OtherOperationCount;
		internal ulong ReadTransferCount;
		internal ulong WriteTransferCount;
		internal ulong OtherTransferCount;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct JobObjectBasicLimitInformation
	{
		internal long PerProcessUserTimeLimit;
		internal long PerJobUserTimeLimit;

		internal uint LimitFlags;

		internal UIntPtr MinimumWorkingSetSize;
		internal UIntPtr MaximumWorkingSetSize;

		internal uint ActiveProcessLimit;
		internal UIntPtr Affinity;

		internal uint PriorityClass;
		internal uint SchedulingClass;
	}

	/// <summary>Mirrors the native <c>JOBOBJECT_EXTENDED_LIMIT_INFORMATION</c> layout; field order and size must match.</summary>
	[StructLayout(LayoutKind.Sequential)]
	private struct JobObjectExtendedLimitInformation
	{
		internal JobObjectBasicLimitInformation BasicLimitInformation;
		internal IoCounters IoInfo;

		internal UIntPtr ProcessMemoryLimit;
		internal UIntPtr JobMemoryLimit;
		internal UIntPtr PeakProcessMemoryUsed;
		internal UIntPtr PeakJobMemoryUsed;
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool SetInformationJobObject(
		IntPtr hJob,
		JobObjectInformationClass infoType,
		IntPtr lpJobObjectInfo,
		uint cbJobObjectInfoLength
	);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseHandle(IntPtr hObject);
}
