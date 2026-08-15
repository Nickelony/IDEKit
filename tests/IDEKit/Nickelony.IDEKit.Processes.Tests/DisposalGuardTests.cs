using System.ComponentModel;

namespace Nickelony.IDEKit.Processes.Tests;

/// <summary>
/// Verifies the claim-once disposal and the failure translation of <see cref="DisposalGuard"/>.
/// </summary>
[TestClass]
public class DisposalGuardTests
{
	[TestMethod]
	public void Execute_NotDisposed_RunsTheOperation()
	{
		var guard = new DisposalGuard();

		Assert.AreEqual(42, guard.Execute(new object(), () => 42));
	}

	[TestMethod]
	public void Execute_NotDisposed_PropagatesTheFailure()
	{
		var guard = new DisposalGuard();

		Assert.ThrowsExactly<InvalidOperationException>(
			() => guard.Execute<int>(new object(), () => throw new InvalidOperationException()));
	}

	[TestMethod]
	public void Execute_Disposed_ThrowsObjectDisposedException()
	{
		var guard = new DisposalGuard();
		var instance = new object();
		guard.TryBeginDispose();

		Assert.ThrowsExactly<ObjectDisposedException>(() => guard.Execute(instance, () => 42));
		Assert.ThrowsExactly<ObjectDisposedException>(() => guard.Execute(instance, static () => { }));
		Assert.ThrowsExactly<ObjectDisposedException>(() => guard.ThrowIfDisposed(instance));
	}

	[TestMethod]
	public void Execute_DisposalDuringOperation_TranslatesTheFailure()
	{
		var guard = new DisposalGuard();

		// The operation claims disposal and then fails the way the inner adapter would; the guard must report
		// the documented ObjectDisposedException instead of the inner failure.
		Assert.ThrowsExactly<ObjectDisposedException>(() => guard.Execute(new object(), () =>
		{
			guard.TryBeginDispose();
			throw new InvalidOperationException("inner failure");
		}));
	}

	[TestMethod]
	public void Execute_DisposalDuringOperation_KeepsAnInnerDisposedException()
	{
		var guard = new DisposalGuard();

		ObjectDisposedException exception = Assert.ThrowsExactly<ObjectDisposedException>(() => guard.Execute<int>(new object(), () =>
		{
			guard.TryBeginDispose();
			throw new ObjectDisposedException("inner");
		}));

		Assert.AreEqual("inner", exception.ObjectName);
	}

	[TestMethod]
	public void Execute_DisposalDuringOperation_KeepsANonDisposalFailure()
	{
		// A racing failure that is not the released resource's own reaction keeps its type, so disposal never
		// masks a genuine fault: a racing Kill's Win32Exception, a read fault, or a caller's cancellation.
		Assert.ThrowsExactly<Win32Exception>(() => Race(new Win32Exception(5)));
		Assert.ThrowsExactly<IOException>(() => Race(new IOException("The racing write failed.")));
		Assert.ThrowsExactly<OperationCanceledException>(() => Race(new OperationCanceledException()));

		static void Race(Exception planned)
		{
			// Each case claims disposal on its own guard: a guard that is already disposed rejects the call
			// before the operation runs, which is a different path from the racing one under test.
			var guard = new DisposalGuard();
			_ = guard.Execute<int>(new object(), () =>
			{
				guard.TryBeginDispose();
				throw planned;
			});
		}
	}

	[TestMethod]
	public void ExecuteUnmasked_NotDisposed_RunsTheOperation()
	{
		var guard = new DisposalGuard();

		Assert.AreEqual(42, guard.ExecuteUnmasked(new object(), () => 42));
	}

	[TestMethod]
	public void ExecuteUnmasked_Disposed_ThrowsObjectDisposedException()
	{
		var guard = new DisposalGuard();
		var instance = new object();
		guard.TryBeginDispose();

		Assert.ThrowsExactly<ObjectDisposedException>(() => guard.ExecuteUnmasked(instance, () => 42));
	}

	[TestMethod]
	public void ExecuteUnmasked_DisposalDuringOperation_KeepsTheGenuineFailure()
	{
		var guard = new DisposalGuard();

		// A member whose own failure is meaningful before disposal - an exit code read before the process
		// exited - must not have that failure reported as the disposal result.
		InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(
			() => guard.ExecuteUnmasked<int>(new object(), () =>
			{
				guard.TryBeginDispose();
				throw new InvalidOperationException("The process has not exited.");
			}));

		Assert.AreEqual("The process has not exited.", exception.Message);
	}

	[TestMethod]
	public void TryBeginDispose_IsClaimedOnce()
	{
		var guard = new DisposalGuard();

		Assert.IsFalse(guard.IsDisposed);
		Assert.IsTrue(guard.TryBeginDispose());
		Assert.IsTrue(guard.IsDisposed);
		Assert.IsFalse(guard.TryBeginDispose());
	}
}
