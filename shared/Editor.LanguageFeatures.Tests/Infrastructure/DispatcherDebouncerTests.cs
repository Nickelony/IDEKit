#if AVALONIAEDIT
using Nickelony.IDEKit.Infrastructure;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using Nickelony.IDEKit.Infrastructure;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

/// <summary>
/// Pins the unit contract of the dispatcher debouncer: re-arming replaces the callback, canceling
/// drops it, and a disposed debouncer ignores further arming.
/// </summary>
[STATestClass]
public sealed class DispatcherDebouncerTests
{
	[TestMethod]
	public void Arm_Repeatedly_RunsOnlyTheLastCallback()
	{
		int firstCount = 0;
		int secondCount = 0;
		using var debouncer = new DispatcherDebouncer(TimeSpan.FromMilliseconds(10.0));

		debouncer.Arm(() => firstCount++);
		debouncer.Arm(() => secondCount++);

		DispatcherTestUtils.PumpUntil(() => secondCount > 0);
		DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(20.0));

		// Arming again restarts the delay with the newest callback, so the replaced one never runs.
		Assert.AreEqual(0, firstCount);
		Assert.AreEqual(1, secondCount);
		Assert.IsFalse(debouncer.IsPending);
	}

	[TestMethod]
	public void Cancel_ArmedCallback_DoesNotRun()
	{
		int count = 0;
		using var debouncer = new DispatcherDebouncer(TimeSpan.FromMilliseconds(10.0));

		debouncer.Arm(() => count++);
		Assert.IsTrue(debouncer.IsPending);

		debouncer.Cancel();
		Assert.IsFalse(debouncer.IsPending);

		DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(20.0));

		Assert.AreEqual(0, count);
	}

	[TestMethod]
	public void Dispose_ThenArm_IsIgnoredAndIdempotent()
	{
		int count = 0;
		var debouncer = new DispatcherDebouncer(TimeSpan.FromMilliseconds(10.0));

		debouncer.Dispose();
		debouncer.Dispose();

		// A disposed debouncer stays silent: arming is ignored and no callback can run.
		debouncer.Arm(() => count++);

		DispatcherTestUtils.PumpFrames(TimeSpan.FromMilliseconds(20.0));

		Assert.AreEqual(0, count);
		Assert.IsFalse(debouncer.IsPending);
	}
}
