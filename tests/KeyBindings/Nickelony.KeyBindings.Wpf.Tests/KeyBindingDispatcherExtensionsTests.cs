using System.Windows.Input;
using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestHelpers;

namespace Nickelony.KeyBindings.Wpf.Tests;

[STATestClass]
public sealed class KeyBindingDispatcherExtensionsTests
{
	[TestMethod]
	public void HandleKeyDown_BoundAndExecutable_ExecutesAndReportsExecuted()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> checkedCommands) = CreateDispatcher();

		KeyEventArgs args = TestKeyEvents.CreateKeyDown(Key.S, Key.LeftCtrl);

		KeyStrokeDispatchOutcome outcome = dispatcher.HandleKeyDown(args, null);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, outcome);
		Assert.HasCount(1, executed);
		Assert.AreEqual(TestCommand.Save, executed[0]);
		Assert.HasCount(1, checkedCommands);
		Assert.AreEqual(TestCommand.Save, checkedCommands[0]);
		Assert.IsFalse(args.Handled);
	}

	[TestMethod]
	public void HandleKeyDown_OnADispatcherChain_ExecutesTheFirstContextThatHandlesTheStroke()
	{
		(KeyBindingDispatcher<TestCommand> editorDispatcher, List<TestCommand> editorExecuted, List<TestCommand> _) = CreateDispatcher();
		(KeyBindingDispatcher<TestCommand> globalDispatcher, List<TestCommand> globalExecuted, List<TestCommand> _) = CreateDispatcher();

		var chain = new KeyBindingDispatcherChain<TestCommand>([editorDispatcher, globalDispatcher]);

		KeyStrokeDispatchOutcome outcome = chain.HandleKeyDown(TestKeyEvents.CreateKeyDown(Key.S, Key.LeftCtrl), null);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, outcome);
		Assert.HasCount(1, editorExecuted);
		Assert.IsEmpty(globalExecuted);
	}

	[TestMethod]
	public void HandleKeyDown_BoundButCannotExecute_ReportsNotHandledAndPassesResolvedCommand()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> checkedCommands) = CreateDispatcher(
			canExecute: _ => false
		);

		KeyEventArgs args = TestKeyEvents.CreateKeyDown(Key.Z, Key.LeftCtrl);

		KeyStrokeDispatchOutcome outcome = dispatcher.HandleKeyDown(args, null);

		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, outcome);
		Assert.IsEmpty(executed);
		Assert.HasCount(1, checkedCommands);
		Assert.AreEqual(TestCommand.Undo, checkedCommands[0]);
	}

	[TestMethod]
	public void HandleKeyDown_UnboundOrDifferentlyModifiedKey_ReportsNotHandled()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> checkedCommands) = CreateDispatcher();

		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, dispatcher.HandleKeyDown(TestKeyEvents.CreateKeyDown(Key.Q, Key.LeftCtrl), null));
		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, dispatcher.HandleKeyDown(TestKeyEvents.CreateKeyDown(Key.S), null));

		Assert.AreEqual(
			KeyStrokeDispatchOutcome.NotHandled,
			dispatcher.HandleKeyDown(TestKeyEvents.CreateKeyDown(Key.S, Key.LeftCtrl, Key.LeftAlt), null)
		);

		Assert.IsEmpty(executed);
		Assert.IsEmpty(checkedCommands);
	}

	[TestMethod]
	public void HandleKeyDown_ModifierOnlyAndImeKeys_ReportNotHandled()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> checkedCommands) = CreateDispatcher();

		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, dispatcher.HandleKeyDown(TestKeyEvents.CreateKeyDown(Key.LeftCtrl), null));
		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, dispatcher.HandleKeyDown(TestKeyEvents.CreateKeyDown(Key.ImeProcessed), null));

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
		// dispatched a second time - not even to report that the stroke is bound.
		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, dispatcher.HandleKeyDown(args, null));
		Assert.IsEmpty(executed);
		Assert.IsEmpty(checkedCommands);
	}

	[TestMethod]
	public void HandleKeyDown_AutoRepeat_IsIgnoredByDefaultAndDispatchesWhenOptedIn()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher();

		KeyEventArgs args = TestKeyEvents.CreateKeyDown(Key.S, Key.LeftCtrl);

		WpfInternalsTestHelpers.MarkRepeat(args);

		// A held key must not run the command once per repeat ...
		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, dispatcher.HandleKeyDown(args, null));
		Assert.IsEmpty(executed);

		// ... unless the host opts in for commands that are meant to repeat.
		Assert.AreEqual(
			KeyStrokeDispatchOutcome.Executed,
			dispatcher.HandleKeyDown(args, null, new KeyDownHandlingOptions { IgnoreAutoRepeat = false })
		);

		Assert.HasCount(1, executed);
		Assert.AreEqual(TestCommand.Save, executed[0]);
	}

	[TestMethod]
	public void HandleKeyDown_AltGr_IsIgnoredByDefaultAndDispatchesWhenOptedIn()
	{
		// Ctrl+Alt is the stroke shape AltGr produces on a layout that has one, so the fixture binds Save to it.
		KeyBindingService<TestCommand> service = CreateService(
			CreateOverrides(nameof(TestCommand.Save), ("S", (int)(KeyModifiers.Control | KeyModifiers.Alt)))
		);

		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(service);

		KeyEventArgs args = TestKeyEvents.CreateKeyDown(Key.S, Key.LeftCtrl, Key.RightAlt);

		Assert.IsTrue(args.IsAltGr());

		// The stroke types a character on an AltGr layout, so it must not fire the Ctrl+Alt binding ...
		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, dispatcher.HandleKeyDown(args, null));
		Assert.IsEmpty(executed);

		// ... unless the host knows its layouts never report AltGr.
		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, dispatcher.HandleKeyDown(args, null, new KeyDownHandlingOptions { IgnoreAltGr = false }));
		Assert.HasCount(1, executed);
		Assert.AreEqual(TestCommand.Save, executed[0]);
	}

	/// <summary>Pins that a Ctrl+RightAlt chord on a non-character key is a genuine binding and is not dropped by the default AltGr filter.</summary>
	[TestMethod]
	public void HandleKeyDown_CtrlAltOnANonCharacterKey_IsNotTreatedAsAltGr()
	{
		KeyBindingService<TestCommand> service = CreateService(
			CreateOverrides(nameof(TestCommand.Save), ("F5", (int)(KeyModifiers.Control | KeyModifiers.Alt)))
		);

		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(service);

		KeyEventArgs args = TestKeyEvents.CreateKeyDown(Key.F5, Key.LeftCtrl, Key.RightAlt);

		Assert.IsFalse(args.IsAltGr());
		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, dispatcher.HandleKeyDown(args, null));
		Assert.HasCount(1, executed);
		Assert.AreEqual(TestCommand.Save, executed[0]);
	}

	/// <summary>
	/// Pins that the two filters are independent: opting out of one does not disable the other, so an event that is both an auto-repeat and an AltGr
	/// stroke still needs both opt-outs to dispatch.
	/// </summary>
	[TestMethod]
	public void HandleKeyDown_FilterFlags_AreIndependent()
	{
		KeyBindingService<TestCommand> service = CreateService(
			CreateOverrides(nameof(TestCommand.Save), ("S", (int)(KeyModifiers.Control | KeyModifiers.Alt)))
		);

		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(service);

		KeyEventArgs args = TestKeyEvents.CreateKeyDown(Key.S, Key.LeftCtrl, Key.RightAlt);
		WpfInternalsTestHelpers.MarkRepeat(args);

		Assert.IsTrue(args.IsAltGr());
		Assert.IsTrue(args.IsRepeat);

		// Each opt-out on its own leaves the other filter active ...
		Assert.AreEqual(
			KeyStrokeDispatchOutcome.NotHandled,
			dispatcher.HandleKeyDown(args, null, new KeyDownHandlingOptions { IgnoreAutoRepeat = false })
		);

		Assert.AreEqual(
			KeyStrokeDispatchOutcome.NotHandled,
			dispatcher.HandleKeyDown(args, null, new KeyDownHandlingOptions { IgnoreAltGr = false })
		);

		Assert.IsEmpty(executed);

		// ... and only opting out of both dispatches the event.
		Assert.AreEqual(
			KeyStrokeDispatchOutcome.Executed,
			dispatcher.HandleKeyDown(args, null, new KeyDownHandlingOptions { IgnoreAutoRepeat = false, IgnoreAltGr = false })
		);

		Assert.HasCount(1, executed);
		Assert.AreEqual(TestCommand.Save, executed[0]);
	}

	[TestMethod]
	public void HandleKeyDown_ChordPrefixAndCompletion_ReportPendingThenExecuted()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		KeyEventArgs first = TestKeyEvents.CreateKeyDown(Key.K, Key.LeftCtrl);
		KeyEventArgs second = TestKeyEvents.CreateKeyDown(Key.S, Key.LeftCtrl);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Pending, dispatcher.HandleKeyDown(first, null));
		Assert.IsTrue(dispatcher.HasPendingChord);
		Assert.IsEmpty(executed);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, dispatcher.HandleKeyDown(second, null));
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.HasCount(1, executed);
		Assert.AreEqual(TestCommand.SaveAll, executed[0]);

		// The adapter reports rather than consumes for both non-failure outcomes, so the caller decides.
		Assert.IsFalse(first.Handled);
		Assert.IsFalse(second.Handled);
	}

	[TestMethod]
	public void HandleKeyDown_ChordPrefix_StillRespectsAnAlreadyHandledEvent()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		KeyEventArgs args = TestKeyEvents.CreateKeyDown(Key.K, Key.LeftCtrl);
		args.Handled = true;

		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, dispatcher.HandleKeyDown(args, null));
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.IsEmpty(executed);
	}

	[TestMethod]
	public void HandleKeyDown_ContextScopedBinding_DispatchesOnlyInItsContext()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateContextService());

		// E is bound to different commands in the two contexts, so the host's token decides which one runs.
		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, dispatcher.HandleKeyDown(TestKeyEvents.CreateKeyDown(Key.E), GeometryContext));
		Assert.AreEqual(TestCommand.Build, executed[0]);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, dispatcher.HandleKeyDown(TestKeyEvents.CreateKeyDown(Key.E), TextureContext));
		Assert.AreEqual(TestCommand.Find, executed[1]);

		// A token the catalog does not declare matches no context-scoped binding.
		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, dispatcher.HandleKeyDown(TestKeyEvents.CreateKeyDown(Key.E), "lighting"));
		Assert.HasCount(2, executed);
	}

	[TestMethod]
	public void HandleKeyDown_NullEvent_Throws()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> _, List<TestCommand> _) = CreateDispatcher();
		Assert.ThrowsExactly<ArgumentNullException>(() => dispatcher.HandleKeyDown(null!, null));
	}
}
