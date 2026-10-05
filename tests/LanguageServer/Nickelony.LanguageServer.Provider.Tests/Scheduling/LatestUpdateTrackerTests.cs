using System.Runtime.CompilerServices;
using UpdateHandle = Nickelony.LanguageServer.Provider.LatestUpdateTracker.UpdateHandle;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Covers which latest-only update may start, which updates a cancellation reaches, and that cancellation callbacks run outside the scheduler lock.
/// </summary>
[TestClass]
public sealed class LatestUpdateTrackerTests
{
	private static readonly string s_path = TestPaths.Script("Latest.lua");
	private static readonly string s_otherPath = TestPaths.Script("Other.lua");

	private readonly object _syncRoot = new();

	[TestMethod]
	public void Start_NewestRegistration_HandsOutAnUncanceledSource()
	{
		LatestUpdateTracker registry = CreateRegistry();
		UpdateHandle registration = Register(registry, s_path);

		CancellationTokenSource? source = TryStart(registry, s_path, registration);

		Assert.IsNotNull(source);
		Assert.IsFalse(source.IsCancellationRequested);
	}

	[TestMethod]
	public void Start_SupersededRegistration_RefusesToStartWhileTheNewerOneMay()
	{
		LatestUpdateTracker registry = CreateRegistry();

		UpdateHandle first = Register(registry, s_path);
		UpdateHandle second = Register(registry, s_path);

		Assert.IsNull(TryStart(registry, s_path, first));
		Assert.IsNotNull(TryStart(registry, s_path, second));
	}

	[TestMethod]
	public void Start_RegistrationsOfOtherPaths_AreNotSuperseded()
	{
		LatestUpdateTracker registry = CreateRegistry();

		UpdateHandle first = Register(registry, s_path);
		UpdateHandle second = Register(registry, s_otherPath);

		Assert.IsNotNull(TryStart(registry, s_path, first));
		Assert.IsNotNull(TryStart(registry, s_otherPath, second));
	}

	[TestMethod]
	public void Start_RegistrationMadeAfterACancellation_Starts()
	{
		LatestUpdateTracker registry = CreateRegistry();

		_ = Register(registry, s_path);

		registry.Cancel(s_path);

		UpdateHandle registration = Register(registry, s_path);

		Assert.IsNotNull(TryStart(registry, s_path, registration));
	}

	[TestMethod]
	public void Start_UnderACaseInsensitiveComparison_TreatsACaseVariantAsTheSamePath()
	{
		var registry = new LatestUpdateTracker(_syncRoot, LocalPathComparisonPolicy.CaseInsensitive);

		UpdateHandle first = Register(registry, TestPaths.Script("Case.lua"));
		UpdateHandle second = Register(registry, TestPaths.Script("CASE.lua"));

		Assert.IsNull(TryStart(registry, TestPaths.Script("Case.lua"), first));
		Assert.IsNotNull(TryStart(registry, TestPaths.Script("Case.lua"), second));
	}

	[TestMethod]
	public void Start_UnderACaseSensitiveComparison_KeepsCaseVariantsApart()
	{
		var registry = new LatestUpdateTracker(_syncRoot, LocalPathComparisonPolicy.CaseSensitive);

		UpdateHandle first = Register(registry, TestPaths.Script("Case.lua"));
		UpdateHandle second = Register(registry, TestPaths.Script("CASE.lua"));

		Assert.IsNotNull(TryStart(registry, TestPaths.Script("Case.lua"), first));
		Assert.IsNotNull(TryStart(registry, TestPaths.Script("CASE.lua"), second));
	}

	[TestMethod]
	public void Start_AfterTheRegistrationWasCanceledWhilePending_RefusesToStart()
	{
		LatestUpdateTracker registry = CreateRegistry();
		UpdateHandle registration = Register(registry, s_path);

		registry.Cancel(s_path);

		Assert.IsNull(TryStart(registry, s_path, registration));
	}

	[TestMethod]
	public void Start_AfterTheRegistrationCompleted_RefusesToStart()
	{
		LatestUpdateTracker registry = CreateRegistry();
		UpdateHandle registration = Register(registry, s_path);

		Complete(registry, s_path, registration);

		Assert.IsNull(TryStart(registry, s_path, registration));
	}

	[TestMethod]
	public void Cancel_RunningUpdate_CancelsItsSource()
	{
		LatestUpdateTracker registry = CreateRegistry();
		UpdateHandle registration = Register(registry, s_path);
		CancellationTokenSource source = Start(registry, s_path, registration);

		registry.Cancel(s_path);

		Assert.IsTrue(source.IsCancellationRequested);
	}

	[TestMethod]
	public void Cancel_RunningUpdateThatANewerUpdateSuperseded_StillCancelsItAndStopsTheNewerOneFromStarting()
	{
		LatestUpdateTracker registry = CreateRegistry();
		UpdateHandle running = Register(registry, s_path);
		CancellationTokenSource runningSource = Start(registry, s_path, running);
		UpdateHandle pending = Register(registry, s_path);

		registry.Cancel(s_path);

		Assert.IsTrue(runningSource.IsCancellationRequested);
		Assert.IsNull(TryStart(registry, s_path, pending));
	}

	[TestMethod]
	public void Cancel_ForOnePath_LeavesOtherPathsUntouched()
	{
		LatestUpdateTracker registry = CreateRegistry();

		UpdateHandle registration = Register(registry, s_path);
		UpdateHandle otherRegistration = Register(registry, s_otherPath);

		CancellationTokenSource source = Start(registry, s_path, registration);
		CancellationTokenSource otherSource = Start(registry, s_otherPath, otherRegistration);

		registry.Cancel(s_path);

		Assert.IsTrue(source.IsCancellationRequested);
		Assert.IsFalse(otherSource.IsCancellationRequested);
	}

	[TestMethod]
	public void Cancel_RunsCancellationCallbacksOutsideTheSchedulerLock()
	{
		LatestUpdateTracker registry = CreateRegistry();
		UpdateHandle registration = Register(registry, s_path);
		CancellationTokenSource source = Start(registry, s_path, registration);

		bool? lockHeldByCallback = null;

		source.Token.Register(() => lockHeldByCallback = Monitor.IsEntered(_syncRoot));

		registry.Cancel(s_path);

		Assert.IsFalse(lockHeldByCallback);
	}

	[TestMethod]
	public void CancelAll_CancelsEveryRunningUpdateAndStopsEveryPendingOneFromStarting()
	{
		LatestUpdateTracker registry = CreateRegistry();
		string pendingPath = TestPaths.Script("Pending.lua");

		UpdateHandle running = Register(registry, s_path);
		CancellationTokenSource runningSource = Start(registry, s_path, running);
		UpdateHandle supersedingPending = Register(registry, s_path);

		UpdateHandle otherRunning = Register(registry, s_otherPath);
		CancellationTokenSource otherSource = Start(registry, s_otherPath, otherRunning);

		UpdateHandle pending = Register(registry, pendingPath);

		registry.CancelAll();

		Assert.IsTrue(runningSource.IsCancellationRequested);
		Assert.IsTrue(otherSource.IsCancellationRequested);
		Assert.IsNull(TryStart(registry, s_path, supersedingPending));
		Assert.IsNull(TryStart(registry, pendingPath, pending));
	}

	[TestMethod]
	public void CancelAll_RunsCancellationCallbacksOutsideTheSchedulerLock()
	{
		LatestUpdateTracker registry = CreateRegistry();

		UpdateHandle registration = Register(registry, s_path);
		UpdateHandle otherRegistration = Register(registry, s_otherPath);

		CancellationTokenSource source = Start(registry, s_path, registration);
		CancellationTokenSource otherSource = Start(registry, s_otherPath, otherRegistration);

		var lockHeldByCallbacks = new List<bool>();

		source.Token.Register(() => lockHeldByCallbacks.Add(Monitor.IsEntered(_syncRoot)));
		otherSource.Token.Register(() => lockHeldByCallbacks.Add(Monitor.IsEntered(_syncRoot)));

		registry.CancelAll();

		Assert.AreSequenceEqual([false, false], lockHeldByCallbacks);
	}

	[TestMethod]
	public void Complete_SupersededRegistration_KeepsTheNewerOne()
	{
		LatestUpdateTracker registry = CreateRegistry();

		UpdateHandle first = Register(registry, s_path);
		UpdateHandle second = Register(registry, s_path);

		Complete(registry, s_path, first);

		Assert.IsNotNull(TryStart(registry, s_path, second));
	}

	[TestMethod]
	public void Complete_RunningRegistration_StopsCancelFromReachingItsSource()
	{
		LatestUpdateTracker registry = CreateRegistry();
		UpdateHandle first = Register(registry, s_path);
		CancellationTokenSource firstSource = Start(registry, s_path, first);

		_ = Register(registry, s_path);

		Complete(registry, s_path, first);

		registry.Cancel(s_path);

		Assert.IsFalse(firstSource.IsCancellationRequested);
	}

	[TestMethod]
	public void Complete_ReleasesTheSettledRegistrationAndItsPath()
	{
		LatestUpdateTracker registry = CreateRegistry();
		(WeakReference registration, WeakReference path) = RunToCompletion(registry);

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		Assert.IsFalse(registration.IsAlive, "The registry must not retain a settled registration.");
		Assert.IsFalse(path.IsAlive, "The registry must not retain the path of a settled update.");

		// The registry stays reachable during the collection, so the assertions cannot pass because the whole registry was collected.
		GC.KeepAlive(registry);
	}

	[TestMethod]
	public void Registration_Cancel_WhenACancellationCallbackThrows_CancelsWithoutThrowing()
	{
		var registration = new UpdateHandle();
		CancellationTokenSource source = registration.Start();

		source.Token.Register(() => throw new InvalidOperationException("The callback failed on purpose."));

		registration.Cancel();

		Assert.IsTrue(source.IsCancellationRequested);
	}

	[TestMethod]
	public void Registration_Cancel_BeforeTheStart_DoesNotPreventIt()
	{
		var registration = new UpdateHandle();

		registration.Cancel();

		Assert.IsFalse(registration.Start().IsCancellationRequested);
	}

	[TestMethod]
	public void Registration_Cancel_AfterDispose_LeavesTheReleasedSourceAlone()
	{
		var registration = new UpdateHandle();
		CancellationTokenSource source = registration.Start();

		registration.Dispose();
		registration.Cancel();

		Assert.IsFalse(source.IsCancellationRequested);
	}

	[TestMethod]
	public void Registration_Dispose_ReleasesTheCancellationSource()
	{
		var registration = new UpdateHandle();
		CancellationTokenSource source = registration.Start();

		registration.Dispose();

		Assert.ThrowsExactly<ObjectDisposedException>(() => _ = source.Token);
	}

	private LatestUpdateTracker CreateRegistry() => new(_syncRoot, TestPaths.PathComparison);

	private UpdateHandle Register(LatestUpdateTracker registry, string normalizedFilePath)
	{
		lock (_syncRoot)
			return registry.Register(normalizedFilePath);
	}

	private CancellationTokenSource? TryStart(LatestUpdateTracker registry, string normalizedFilePath, UpdateHandle registration)
	{
		lock (_syncRoot)
			return registry.Start(normalizedFilePath, registration);
	}

	private CancellationTokenSource Start(LatestUpdateTracker registry, string normalizedFilePath, UpdateHandle registration)
	{
		CancellationTokenSource? source = TryStart(registry, normalizedFilePath, registration);
		Assert.IsNotNull(source);

		return source;
	}

	private void Complete(LatestUpdateTracker registry, string normalizedFilePath, UpdateHandle registration)
	{
		lock (_syncRoot)
			registry.Complete(normalizedFilePath, registration);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private (WeakReference UpdateHandle, WeakReference Path) RunToCompletion(LatestUpdateTracker registry)
	{
		string path = TestPaths.Script("Settled.lua");
		UpdateHandle registration = Register(registry, path);

		_ = Start(registry, path, registration);

		Complete(registry, path, registration);
		registration.Dispose();

		return (new WeakReference(registration), new WeakReference(path));
	}
}
