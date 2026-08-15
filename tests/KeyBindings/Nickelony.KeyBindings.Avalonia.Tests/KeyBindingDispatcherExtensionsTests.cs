using Avalonia.Input;
using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestFixtures;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;

namespace Nickelony.KeyBindings.Avalonia.Tests;

[TestClass]
public class KeyBindingDispatcherExtensionsTests
{
	[TestMethod]
	public void HandleKeyDown_BoundAndExecutable_ExecutesAndReportsExecuted()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> checkedCommands) = CreateDispatcher();

		KeyEventArgs args = TestKeyEvents.CreateKeyDown(Key.S, AvaloniaKeyModifiers.Control);

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

		KeyEventArgs args = TestKeyEvents.CreateKeyDown(Key.Z, AvaloniaKeyModifiers.Control);

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

		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.HandleKeyDown(TestKeyEvents.CreateKeyDown(Key.Q, AvaloniaKeyModifiers.Control), null));
		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.HandleKeyDown(TestKeyEvents.CreateKeyDown(Key.S), null));
		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.HandleKeyDown(
			TestKeyEvents.CreateKeyDown(Key.S, AvaloniaKeyModifiers.Control | AvaloniaKeyModifiers.Alt), null));

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

		KeyEventArgs args = TestKeyEvents.CreateKeyDown(Key.S, AvaloniaKeyModifiers.Control);
		args.Handled = true;

		// The caller owns the input route, so an event another handler already consumed must not be
		// dispatched a second time - not even to report that the combo is bound.
		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.HandleKeyDown(args, null));
		Assert.IsEmpty(executed);
		Assert.IsEmpty(checkedCommands);
	}

	[TestMethod]
	public void HandleKeyDown_ChordPrefixAndCompletion_ReportPendingThenExecuted()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		KeyEventArgs first = TestKeyEvents.CreateKeyDown(Key.K, AvaloniaKeyModifiers.Control);
		KeyEventArgs second = TestKeyEvents.CreateKeyDown(Key.S, AvaloniaKeyModifiers.Control);

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

		KeyEventArgs args = TestKeyEvents.CreateKeyDown(Key.K, AvaloniaKeyModifiers.Control);
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
}
