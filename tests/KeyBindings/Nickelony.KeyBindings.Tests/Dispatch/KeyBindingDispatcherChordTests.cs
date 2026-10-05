using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestHelpers;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Covers the dispatcher's chord buffering: multi-stroke resolution, the pending-chord lifecycle, and the context rules a buffered chord follows.
/// </summary>
[TestClass]
public sealed class KeyBindingDispatcherChordTests
{
	private static KeyStroke Ctrl(KeyCode key) => new(key, KeyModifiers.Control);

	[TestMethod]
	public void Dispatch_FirstStrokeOfAChord_ReportsPending()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		KeyStrokeDispatchOutcome outcome = dispatcher.Dispatch(Ctrl(KeyCode.K), null);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Pending, outcome);
		Assert.IsTrue(dispatcher.HasPendingChord);
		Assert.AreEqual(new KeyChord(Ctrl(KeyCode.K)), dispatcher.PendingChord);
		Assert.IsEmpty(executed);
	}

	[TestMethod]
	public void Dispatch_CompletingStroke_ReportsExecuted()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		dispatcher.Dispatch(Ctrl(KeyCode.K), null);

		KeyStrokeDispatchOutcome outcome = dispatcher.Dispatch(Ctrl(KeyCode.S), null);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, outcome);
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.HasCount(1, executed);
		Assert.AreEqual(TestCommand.SaveAll, executed[0]);
	}

	/// <summary>
	/// Pins a chord of three or more strokes: every stroke is buffered exactly once, so the buffer stays a valid chord while the chord is in progress
	/// and the completing stroke reaches the command.
	/// </summary>
	[TestMethod]
	public void Dispatch_ThreeStrokeChord_BuffersEachStrokeOnceAndExecutes()
	{
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(
				TestCommand.SaveAll,
				nameof(TestCommand.SaveAll),
				CommandRemappingPolicy.Remappable,
				new KeyChord(Ctrl(KeyCode.K), Ctrl(KeyCode.S), Ctrl(KeyCode.F))
			),
		]);

		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(
			CreateService(catalog: catalog)
		);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Pending, dispatcher.Dispatch(Ctrl(KeyCode.K), null));
		Assert.AreEqual(new KeyChord(Ctrl(KeyCode.K)), dispatcher.PendingChord);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Pending, dispatcher.Dispatch(Ctrl(KeyCode.S), null));
		Assert.AreEqual(new KeyChord(Ctrl(KeyCode.K), Ctrl(KeyCode.S)), dispatcher.PendingChord);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, dispatcher.Dispatch(Ctrl(KeyCode.F), null));
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.HasCount(1, executed);
		Assert.AreEqual(TestCommand.SaveAll, executed[0]);
	}

	[TestMethod]
	public void Dispatch_SecondChordSharingThePrefix_ResolvesToItsOwnCommand()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		dispatcher.Dispatch(Ctrl(KeyCode.K), null);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, dispatcher.Dispatch(Ctrl(KeyCode.F), null));
		Assert.AreEqual(TestCommand.Find, executed[0]);
	}

	[TestMethod]
	public void Dispatch_WrongSecondStroke_DropsThePendingChord()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		dispatcher.Dispatch(Ctrl(KeyCode.K), null);

		KeyStrokeDispatchOutcome outcome = dispatcher.Dispatch(new KeyStroke(KeyCode.S, KeyModifiers.None), null);

		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, outcome);
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.IsEmpty(executed);
	}

	[TestMethod]
	public void Dispatch_SingleStrokeBinding_StillExecutesWithNoPendingChord()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		// Control+S is not a prefix of any chord in this catalog, so it is an ordinary binding.
		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, dispatcher.Dispatch(Ctrl(KeyCode.S), null));
		Assert.AreEqual(TestCommand.Save, executed[0]);
	}

	[TestMethod]
	public void Dispatch_RepeatingThePendingStroke_DropsThePendingChord()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		dispatcher.Dispatch(Ctrl(KeyCode.K), null);

		KeyStrokeDispatchOutcome outcome = dispatcher.Dispatch(Ctrl(KeyCode.K), null);

		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, outcome);
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.IsEmpty(executed);
	}

	[TestMethod]
	public void Dispatch_UninitializedStroke_LeavesThePendingChordUntouched()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		dispatcher.Dispatch(Ctrl(KeyCode.K), null);

		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, dispatcher.Dispatch(default, null));
		Assert.IsTrue(dispatcher.HasPendingChord);
		Assert.IsEmpty(executed);

		// The chord is still completable after the junk stroke.
		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, dispatcher.Dispatch(Ctrl(KeyCode.S), null));
	}

	[TestMethod]
	public void Dispatch_CompletingStrokeWhoseCommandCannotExecute_DropsTheChordWithoutExecuting()
	{
		// Find can still run, so the first stroke is buffered; SaveAll cannot, so the completing stroke is not.
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(
			CreateChordService(),
			canExecute: command => command != TestCommand.SaveAll
		);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Pending, dispatcher.Dispatch(Ctrl(KeyCode.K), null));

		KeyStrokeDispatchOutcome outcome = dispatcher.Dispatch(Ctrl(KeyCode.S), null);

		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, outcome);
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.IsEmpty(executed);
	}

	[TestMethod]
	public void Dispatch_FirstStrokeOfAChord_WhenNoCommandUnderItCanExecute_ReportsNotHandled()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> checkedCommands) = CreateDispatcher(
			CreateChordService(),
			canExecute: _ => false
		);

		KeyStrokeDispatchOutcome outcome = dispatcher.Dispatch(Ctrl(KeyCode.K), null);

		// The stroke is left to the host, and the predicate was asked about each command the prefix could still complete.
		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, outcome);
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.AreEqual(default, dispatcher.PendingChord);
		Assert.IsEmpty(executed);
		Assert.HasCount(2, checkedCommands);
		Assert.Contains(TestCommand.SaveAll, checkedCommands);
		Assert.Contains(TestCommand.Find, checkedCommands);
	}

	[TestMethod]
	public void Dispatch_FirstStrokeOfAChord_WhenOneCommandUnderItCanExecute_ReportsPendingAndCompletesIt()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(
			CreateChordService(),
			canExecute: command => command == TestCommand.Find
		);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Pending, dispatcher.Dispatch(Ctrl(KeyCode.K), null));
		Assert.IsTrue(dispatcher.HasPendingChord);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, dispatcher.Dispatch(Ctrl(KeyCode.F), null));
		Assert.HasCount(1, executed);
		Assert.AreEqual(TestCommand.Find, executed[0]);
	}

	[TestMethod]
	public void Dispatch_FirstStrokeOfAChord_StopsAskingTheCommandsOnceOneCanExecute()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> _, List<TestCommand> checkedCommands) = CreateDispatcher(
			CreateChordService()
		);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Pending, dispatcher.Dispatch(Ctrl(KeyCode.K), null));
		Assert.HasCount(1, checkedCommands);
	}

	[TestMethod]
	public void Dispatch_StrokeInsideAChord_WhenNoCommandUnderItCanExecuteAnymore_DropsTheChord()
	{
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(
				TestCommand.SaveAll,
				nameof(TestCommand.SaveAll),
				CommandRemappingPolicy.Remappable,
				new KeyChord(Ctrl(KeyCode.K), Ctrl(KeyCode.S), Ctrl(KeyCode.F))
			),
		]);

		bool canExecute = true;

		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(
			CreateService(catalog: catalog),
			canExecute: _ => canExecute
		);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Pending, dispatcher.Dispatch(Ctrl(KeyCode.K), null));

		// The host's conditions changed between the strokes, so the continuation is asked again and the chord ends.
		canExecute = false;

		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, dispatcher.Dispatch(Ctrl(KeyCode.S), null));
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.IsEmpty(executed);
	}

	[TestMethod]
	public void CancelPendingChord_DropsTheBufferedChordAndKeepsDispatching()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		dispatcher.Dispatch(Ctrl(KeyCode.K), null);
		dispatcher.CancelPendingChord();

		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.AreEqual(default, dispatcher.PendingChord);

		// Control+S is still an ordinary binding once the chord was abandoned.
		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, dispatcher.Dispatch(Ctrl(KeyCode.S), null));
		Assert.AreEqual(TestCommand.Save, executed[0]);
	}

	[TestMethod]
	public void CancelPendingChord_WithNoPendingChord_IsANoOp()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		dispatcher.CancelPendingChord();

		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.IsEmpty(executed);
	}

	// ---- Contexts ----

	[TestMethod]
	public void Dispatch_ChordDeclaredInAnotherContext_ReportsNotHandled()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateContextService());

		// E builds in the geometry context and finds in the texture context, so each context resolves its
		// own command and neither sees the other's binding.
		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, dispatcher.Dispatch(new KeyStroke(KeyCode.E, KeyModifiers.None), GeometryContext));
		Assert.AreEqual(TestCommand.Build, executed[0]);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, dispatcher.Dispatch(new KeyStroke(KeyCode.E, KeyModifiers.None), TextureContext));
		Assert.AreEqual(TestCommand.Find, executed[1]);

		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, dispatcher.Dispatch(new KeyStroke(KeyCode.E, KeyModifiers.None), "lighting"));
		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, dispatcher.Dispatch(new KeyStroke(KeyCode.E, KeyModifiers.None), null));
		Assert.HasCount(2, executed);
	}

	[TestMethod]
	public void Dispatch_ContextChangeBetweenStrokes_DropsTheBufferedChordAndStartsFresh()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateModeChordService());

		Assert.AreEqual(KeyStrokeDispatchOutcome.Pending, dispatcher.Dispatch(Ctrl(KeyCode.K), GeometryContext));

		// The same chord is bound to Find in the texture context, but the buffered strokes were started in
		// the geometry context, so they are abandoned instead of completing a command the user never
		// started in texture mode. The stroke is then considered on its own.
		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, dispatcher.Dispatch(Ctrl(KeyCode.S), TextureContext));
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.IsEmpty(executed);
	}

	[TestMethod]
	public void Dispatch_SameContextBetweenStrokes_CompletesTheChord()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateModeChordService());

		Assert.AreEqual(KeyStrokeDispatchOutcome.Pending, dispatcher.Dispatch(Ctrl(KeyCode.K), GeometryContext));
		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, dispatcher.Dispatch(Ctrl(KeyCode.S), GeometryContext));

		Assert.AreEqual(TestCommand.SaveAll, executed[0]);
	}

	[TestMethod]
	public void Dispatch_FirstStrokeOfAChord_OnlyTheCommandsOfTheActiveContextDecideWhetherItIsPending()
	{
		// The chord is bound to SaveAll in the geometry context and to Find in the texture context.
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> _, List<TestCommand> checkedCommands) = CreateDispatcher(
			CreateModeChordService(),
			canExecute: command => command == TestCommand.Find
		);

		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, dispatcher.Dispatch(Ctrl(KeyCode.K), GeometryContext));
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.HasCount(1, checkedCommands);
		Assert.AreEqual(TestCommand.SaveAll, checkedCommands[0]);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Pending, dispatcher.Dispatch(Ctrl(KeyCode.K), TextureContext));
		Assert.IsTrue(dispatcher.HasPendingChord);
	}
}
