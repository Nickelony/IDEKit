using Nickelony.KeyBindings.Testing;
using System.Reflection;
using System.Windows.Input;
using static Nickelony.KeyBindings.Testing.KeyBindingTestFixtures;

namespace Nickelony.KeyBindings.Wpf.Tests;

[STATestClass]
public class KeyBindingDispatcherExtensionsTests
{
	[TestMethod]
	public void HandleKeyDown_BoundAndExecutable_ExecutesAndReportsExecuted()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> checkedCommands) = CreateDispatcher();

		KeyEventArgs args = TestKeyEvents.CreateKeyDown(Key.S, Key.LeftCtrl);

		KeyChordDispatchResult result = dispatcher.HandleKeyDown(args, null);

		Assert.AreEqual(KeyChordDispatchResult.Executed, result);
		Assert.HasCount(1, executed);
		Assert.AreEqual(TestCommand.Save, executed[0]);
		Assert.HasCount(1, checkedCommands);
		Assert.AreEqual(TestCommand.Save, checkedCommands[0]);
		Assert.IsFalse(args.Handled);
	}

	[TestMethod]
	public void HandleKeyDown_BoundButCannotExecute_ReportsNotHandledAndPassesResolvedCommand()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> checkedCommands) = CreateDispatcher(canExecute: _ => false);

		KeyEventArgs args = TestKeyEvents.CreateKeyDown(Key.Z, Key.LeftCtrl);

		KeyChordDispatchResult result = dispatcher.HandleKeyDown(args, null);

		Assert.AreEqual(KeyChordDispatchResult.NotHandled, result);
		Assert.IsEmpty(executed);
		Assert.HasCount(1, checkedCommands);
		Assert.AreEqual(TestCommand.Undo, checkedCommands[0]);
	}

	[TestMethod]
	public void HandleKeyDown_UnboundOrDifferentlyModifiedKey_ReportsNotHandled()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> checkedCommands) = CreateDispatcher();

		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.HandleKeyDown(TestKeyEvents.CreateKeyDown(Key.Q, Key.LeftCtrl), null));
		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.HandleKeyDown(TestKeyEvents.CreateKeyDown(Key.S), null));
		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.HandleKeyDown(TestKeyEvents.CreateKeyDown(Key.S, Key.LeftCtrl, Key.LeftAlt), null));

		Assert.IsEmpty(executed);
		Assert.IsEmpty(checkedCommands);
	}

	[TestMethod]
	public void HandleKeyDown_ModifierOnlyAndImeKeys_ReportNotHandled()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> checkedCommands) = CreateDispatcher();

		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.HandleKeyDown(TestKeyEvents.CreateKeyDown(Key.LeftCtrl), null));
		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.HandleKeyDown(TestKeyEvents.CreateKeyDown(Key.ImeProcessed), null));

		Assert.IsEmpty(executed);
		Assert.IsEmpty(checkedCommands);
	}

	[TestMethod]
	public void HandleKeyDown_AlreadyHandledEvent_ReportsNotHandledWithoutDispatching()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> checkedCommands) = CreateDispatcher();

		KeyEventArgs args = TestKeyEvents.CreateKeyDown(Key.S, Key.LeftCtrl);
		args.Handled = true;

		// The caller owns the input route, so an event another handler already consumed must not be
		// dispatched a second time - not even to report that the combo is bound.
		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.HandleKeyDown(args, null));
		Assert.IsEmpty(executed);
		Assert.IsEmpty(checkedCommands);
	}

	[TestMethod]
	public void HandleKeyDown_AutoRepeat_IsIgnoredByDefaultAndDispatchesWhenOptedIn()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher();

		KeyEventArgs args = TestKeyEvents.CreateKeyDown(Key.S, Key.LeftCtrl);

		MarkRepeat(args);

		// A held key must not run the command once per repeat ...
		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.HandleKeyDown(args, null));
		Assert.IsEmpty(executed);

		// ... unless the host opts in for commands that are meant to repeat.
		Assert.AreEqual(KeyChordDispatchResult.Executed, dispatcher.HandleKeyDown(args, null, new KeyDownHandlingOptions { IgnoreAutoRepeat = false }));
		Assert.HasCount(1, executed);
		Assert.AreEqual(TestCommand.Save, executed[0]);
	}

	[TestMethod]
	public void HandleKeyDown_AltGr_IsIgnoredByDefaultAndDispatchesWhenOptedIn()
	{
		// Ctrl+Alt is the combo shape AltGr produces on a layout that has one, so the fixture binds Save to it.
		KeyBindingService<TestCommand> service = CreateService(
			CreateOverrides(nameof(TestCommand.Save), ("S", (int)(KeyModifierSet.Control | KeyModifierSet.Alt))));
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(service);

		KeyEventArgs args = TestKeyEvents.CreateKeyDown(Key.S, Key.LeftCtrl, Key.RightAlt);

		Assert.IsTrue(args.IsAltGr());

		// The stroke types a character on an AltGr layout, so it must not fire the Ctrl+Alt binding ...
		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.HandleKeyDown(args, null));
		Assert.IsEmpty(executed);

		// ... unless the host knows its layouts never report AltGr.
		Assert.AreEqual(KeyChordDispatchResult.Executed, dispatcher.HandleKeyDown(args, null, new KeyDownHandlingOptions { IgnoreAltGr = false }));
		Assert.HasCount(1, executed);
		Assert.AreEqual(TestCommand.Save, executed[0]);
	}

	/// <summary>
	/// Pins that the two filters are independent: opting out of one does not disable the other, so an
	/// event that is both an auto-repeat and an AltGr stroke still needs both opt-outs to dispatch.
	/// </summary>
	[TestMethod]
	public void HandleKeyDown_FilterFlags_AreIndependent()
	{
		KeyBindingService<TestCommand> service = CreateService(
			CreateOverrides(nameof(TestCommand.Save), ("S", (int)(KeyModifierSet.Control | KeyModifierSet.Alt))));
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(service);

		KeyEventArgs args = TestKeyEvents.CreateKeyDown(Key.S, Key.LeftCtrl, Key.RightAlt);
		MarkRepeat(args);

		Assert.IsTrue(args.IsAltGr());
		Assert.IsTrue(args.IsRepeat);

		// Each opt-out on its own leaves the other filter active ...
		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.HandleKeyDown(args, null, new KeyDownHandlingOptions { IgnoreAutoRepeat = false }));
		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.HandleKeyDown(args, null, new KeyDownHandlingOptions { IgnoreAltGr = false }));
		Assert.IsEmpty(executed);

		// ... and only opting out of both dispatches the event.
		Assert.AreEqual(
			KeyChordDispatchResult.Executed,
			dispatcher.HandleKeyDown(args, null, new KeyDownHandlingOptions { IgnoreAutoRepeat = false, IgnoreAltGr = false }));
		Assert.HasCount(1, executed);
		Assert.AreEqual(TestCommand.Save, executed[0]);
	}

	[TestMethod]
	public void HandleKeyDown_ChordPrefixAndCompletion_ReportPendingThenExecuted()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		KeyEventArgs first = TestKeyEvents.CreateKeyDown(Key.K, Key.LeftCtrl);
		KeyEventArgs second = TestKeyEvents.CreateKeyDown(Key.S, Key.LeftCtrl);

		Assert.AreEqual(KeyChordDispatchResult.Pending, dispatcher.HandleKeyDown(first, null));
		Assert.IsTrue(dispatcher.HasPendingChord);
		Assert.IsEmpty(executed);

		Assert.AreEqual(KeyChordDispatchResult.Executed, dispatcher.HandleKeyDown(second, null));
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.HasCount(1, executed);
		Assert.AreEqual(TestCommand.SaveAll, executed[0]);

		// The adapter reports rather than consumes for both non-failure results, so the caller decides.
		Assert.IsFalse(first.Handled);
		Assert.IsFalse(second.Handled);
	}

	[TestMethod]
	public void HandleKeyDown_ChordPrefix_StillRespectsAnAlreadyHandledEvent()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		KeyEventArgs args = TestKeyEvents.CreateKeyDown(Key.K, Key.LeftCtrl);
		args.Handled = true;

		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.HandleKeyDown(args, null));
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.IsEmpty(executed);
	}

	[TestMethod]
	public void HandleKeyDown_ContextScopedBinding_DispatchesOnlyInItsContext()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateContextService());

		// E is bound to different commands in the two contexts, so the host's token decides which one runs.
		Assert.AreEqual(KeyChordDispatchResult.Executed, dispatcher.HandleKeyDown(TestKeyEvents.CreateKeyDown(Key.E), GeometryContext));
		Assert.AreEqual(TestCommand.Build, executed[0]);

		Assert.AreEqual(KeyChordDispatchResult.Executed, dispatcher.HandleKeyDown(TestKeyEvents.CreateKeyDown(Key.E), TextureContext));
		Assert.AreEqual(TestCommand.Find, executed[1]);

		// A token the catalog does not declare matches no token-scoped binding.
		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.HandleKeyDown(TestKeyEvents.CreateKeyDown(Key.E), "lighting"));
		Assert.HasCount(2, executed);
	}

	[TestMethod]
	public void HandleKeyDown_NullEvent_Throws()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> _, List<TestCommand> _) = CreateDispatcher();

		Assert.ThrowsExactly<ArgumentNullException>(() => dispatcher.HandleKeyDown(null!, null));
	}

	/// <summary>
	/// Marks a synthesized key event as an auto-repeat. WPF exposes no public setter; a synthesized event
	/// reports <see cref="KeyEventArgs.IsRepeat"/> as <see langword="false"/> and only the internal
	/// <c>SetRepeat</c> can change it.
	/// </summary>
	private static void MarkRepeat(KeyEventArgs args)
	{
		MethodInfo? setRepeat = typeof(KeyEventArgs).GetMethod("SetRepeat", BindingFlags.Instance | BindingFlags.NonPublic);

		Assert.IsNotNull(setRepeat, "WPF's KeyEventArgs.SetRepeat internals changed.");
		setRepeat.Invoke(args, [true]);
	}
}
