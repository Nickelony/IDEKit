using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestHelpers;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;

namespace Nickelony.KeyBindings.Avalonia.Tests;

[TestClass]
public sealed class KeyRepeatGateTests
{
	[TestMethod]
	public void Admit_TheFirstPressOfAKey_IsAdmitted()
	{
		AvaloniaToolkitTestHost.Run(() =>
		{
			using var rig = new GateRig();
			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
		});
	}

	[TestMethod]
	public void Admit_ARepeatedPressOfAHeldKey_IsIgnored()
	{
		AvaloniaToolkitTestHost.Run(() =>
		{
			using var rig = new GateRig();

			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
			Assert.IsFalse(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
			Assert.IsFalse(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
		});
	}

	[TestMethod]
	public void Admit_APressAfterTheRelease_IsAdmitted()
	{
		AvaloniaToolkitTestHost.Run(() =>
		{
			using var rig = new GateRig();

			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
			rig.Release(Key.A);

			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
		});
	}

	[TestMethod]
	public void Admit_TwoHeldKeys_AreTrackedApart()
	{
		AvaloniaToolkitTestHost.Run(() =>
		{
			using var rig = new GateRig();

			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.B)));

			rig.Release(Key.A);

			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
			Assert.IsFalse(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.B)));
		});
	}

	[TestMethod]
	public void Admit_AReleaseThatAnotherHandlerMarkedHandled_StillForgetsTheKey()
	{
		AvaloniaToolkitTestHost.Run(() =>
		{
			using var rig = new GateRig();

			// The window sits outside the scope, so its tunnel handler marks the release handled before the scope sees it.
			rig.Window.AddHandler(InputElement.KeyUpEvent, (_, e) => e.Handled = true, RoutingStrategies.Tunnel);

			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
			rig.Release(Key.A);

			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
		});
	}

	[TestMethod]
	public void Admit_AfterTheFocusLeavesTheScope_AdmitsTheHeldKeyAgain()
	{
		AvaloniaToolkitTestHost.Run(() =>
		{
			using var rig = new GateRig();

			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));

			rig.Outside.Focus();

			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
		});
	}

	[TestMethod]
	public void Admit_AfterTheFocusMovesWithinTheScope_KeepsTheKeyHeld()
	{
		AvaloniaToolkitTestHost.Run(() =>
		{
			using var rig = new GateRig();

			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));

			rig.AlsoInside.Focus();

			Assert.IsFalse(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
		});
	}

	[TestMethod]
	public void Admit_AfterTheWindowIsDeactivated_AdmitsTheHeldKeyAgain()
	{
		AvaloniaToolkitTestHost.Run(() =>
		{
			using var rig = new GateRig();

			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));

			GateRig.Deactivate(rig.Window);

			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
		});
	}

	[TestMethod]
	public void Admit_AfterTheScopeLeavesTheVisualTree_AdmitsTheHeldKeyAgain()
	{
		AvaloniaToolkitTestHost.Run(() =>
		{
			using var rig = new GateRig();

			// The focus is outside the scope, so the focus loss is not what forgets the key.
			rig.Outside.Focus();
			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));

			((StackPanel)rig.Window.Content!).Children.Remove(rig.Scope);
			Dispatcher.UIThread.RunJobs();

			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
		});
	}

	[TestMethod]
	public void Admit_AfterTheScopeMovesToAnotherWindow_ResetsOnlyOnTheDeactivationOfThatWindow()
	{
		AvaloniaToolkitTestHost.Run(() =>
		{
			using var rig = new GateRig();
			var other = new Window();

			other.Show();

			((StackPanel)rig.Window.Content!).Children.Remove(rig.Scope);
			Dispatcher.UIThread.RunJobs();
			other.Content = rig.Scope;
			Dispatcher.UIThread.RunJobs();

			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));

			GateRig.Deactivate(rig.Window);
			Assert.IsFalse(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)), "The first window no longer contains the scope.");

			GateRig.Deactivate(other);
			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)), "The window that contains the scope was deactivated.");

			other.Content = null;
			other.Close();
		});
	}

	[TestMethod]
	public void Reset_ForgetsEveryHeldKey()
	{
		AvaloniaToolkitTestHost.Run(() =>
		{
			using var rig = new GateRig();

			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.B)));

			rig.Gate.Reset();

			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.B)));
		});
	}

	[TestMethod]
	public void Admit_AReleaseDeliveredAfterTheReset_LeavesTheOtherHeldKeysHeld()
	{
		AvaloniaToolkitTestHost.Run(() =>
		{
			using var rig = new GateRig();

			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
			GateRig.Deactivate(rig.Window);

			// B goes down after the reset, then the release of A that the focus loss delayed arrives.
			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.B)));
			rig.Release(Key.A);

			Assert.IsFalse(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.B)));
			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
		});
	}

	[TestMethod]
	public void Admit_APressAfterTheReset_IsAdmittedOnceAndThenTreatedAsHeld()
	{
		AvaloniaToolkitTestHost.Run(() =>
		{
			using var rig = new GateRig();

			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
			rig.Gate.Reset();

			Assert.IsTrue(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
			Assert.IsFalse(rig.Gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
		});
	}

	[TestMethod]
	public void Dispose_DetachesEveryHandler_SoTheScopeAndTheWindowDoNotKeepTheGateAlive()
	{
		AvaloniaToolkitTestHost.Run(() =>
		{
			using var rig = new GateRig();
			WeakReference gate = CreateAndDisposeGate(rig.Scope);

			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();

			Assert.IsFalse(gate.IsAlive);
		});
	}

	[TestMethod]
	public void Admit_AfterTheDisposal_Throws()
	{
		AvaloniaToolkitTestHost.Run(() =>
		{
			using var rig = new GateRig();
			var gate = new KeyRepeatGate(rig.Scope);

			gate.Dispose();

			Assert.ThrowsExactly<ObjectDisposedException>(() => gate.Admit(TestKeyEvents.CreateKeyDown(Key.A)));
		});
	}

	[TestMethod]
	public void Dispose_Twice_DoesNotThrow()
	{
		AvaloniaToolkitTestHost.Run(() =>
		{
			using var rig = new GateRig();
			var gate = new KeyRepeatGate(rig.Scope);

			gate.Dispose();
			gate.Dispose();
		});
	}

	[TestMethod]
	public void Constructor_NullScope_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => new KeyRepeatGate(null!));

	[TestMethod]
	public void Admit_NullEvent_Throws()
	{
		AvaloniaToolkitTestHost.Run(() =>
		{
			using var rig = new GateRig();
			Assert.ThrowsExactly<ArgumentNullException>(() => rig.Gate.Admit(null!));
		});
	}

	[TestMethod]
	public void HandleKeyDown_WithTheGate_DispatchesAHeldKeyOnce()
	{
		AvaloniaToolkitTestHost.Run(() =>
		{
			using var rig = new GateRig();
			(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher();

			Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, dispatcher.HandleKeyDown(SaveKeyDown(), null, rig.Gate));
			Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, dispatcher.HandleKeyDown(SaveKeyDown(), null, rig.Gate));
			Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, dispatcher.HandleKeyDown(SaveKeyDown(), null, rig.Gate));
			Assert.HasCount(1, executed);

			rig.Release(Key.S);

			Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, dispatcher.HandleKeyDown(SaveKeyDown(), null, rig.Gate));
			Assert.HasCount(2, executed);
		});
	}

	[TestMethod]
	public void HandleKeyDown_WithoutAGate_DispatchesEveryPress()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher();

		_ = dispatcher.HandleKeyDown(SaveKeyDown(), null);
		_ = dispatcher.HandleKeyDown(SaveKeyDown(), null, repeatGate: null);

		Assert.HasCount(2, executed);
	}

	[TestMethod]
	public void HandleKeyDown_AHandledEvent_DoesNotRecordTheKey()
	{
		AvaloniaToolkitTestHost.Run(() =>
		{
			using var rig = new GateRig();
			(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher();
			KeyEventArgs handled = SaveKeyDown();
			handled.Handled = true;

			Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, dispatcher.HandleKeyDown(handled, null, rig.Gate));
			Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, dispatcher.HandleKeyDown(SaveKeyDown(), null, rig.Gate));
			Assert.HasCount(1, executed);
		});
	}

	private static KeyEventArgs SaveKeyDown() => TestKeyEvents.CreateKeyDown(Key.S, AvaloniaKeyModifiers.Control);

	/// <summary>Creates a gate over the scope and disposes it, in a frame of its own so that no local keeps the gate reachable.</summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference CreateAndDisposeGate(Control scope)
	{
		var gate = new KeyRepeatGate(scope);
		var reference = new WeakReference(gate);

		gate.Dispose();

		return reference;
	}

	/// <summary>A shown window whose content holds a scope of two text boxes and a text box outside the scope, and a gate over the scope.</summary>
	private sealed class GateRig : IDisposable
	{
		internal GateRig()
		{
			Inside = new TextBox();
			AlsoInside = new TextBox();
			Outside = new TextBox();
			Scope = new StackPanel { Children = { Inside, AlsoInside } };
			Window = new Window { Content = new StackPanel { Children = { Scope, Outside } } };
			Window.Show();
			Inside.Focus();
			Gate = new KeyRepeatGate(Scope);
		}

		internal Window Window { get; }

		internal StackPanel Scope { get; }

		internal TextBox Inside { get; }

		internal TextBox AlsoInside { get; }

		internal TextBox Outside { get; }

		internal KeyRepeatGate Gate { get; }

		/// <summary>Delivers the release of a key to the focused element of the window, as the platform does.</summary>
		internal void Release(Key key) => Window.KeyRelease(key, modifiers: RawInputModifiers.None, physicalKey: PhysicalKey.None, keySymbol: null);

		/// <summary>
		/// Raises the deactivation that the platform reports when the user switches to another window. The headless platform moves the focus
		/// on an activation instead, so the platform callback is invoked directly and leaves the focus where it is.
		/// </summary>
		internal static void Deactivate(Window window)
		{
			PropertyInfo callback = typeof(IWindowBaseImpl).GetProperty(
				"Deactivated",
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
			)!;

			((Action)callback.GetValue(window.PlatformImpl)!).Invoke();
		}

		public void Dispose()
		{
			Gate.Dispose();
			Window.Close();
		}
	}
}
