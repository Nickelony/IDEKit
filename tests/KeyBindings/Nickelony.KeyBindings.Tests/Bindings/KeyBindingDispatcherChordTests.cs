using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestFixtures;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Covers the dispatcher's chord buffering: multi-stroke resolution, the pending-chord lifecycle, and
/// the context rules a buffered chord follows.
/// </summary>
[TestClass]
public class KeyBindingDispatcherChordTests
{
	private static KeyCombo Ctrl(KeyCode key) => new(key, KeyModifierSet.Control);

	/// <summary>
	/// Builds a service that binds a single Control-modified stroke to <see cref="TestCommand.Build"/>, so a
	/// context that must not steal a chord continuation has something of its own to run.
	/// </summary>
	private static KeyBindingService<TestCommand> CreateSingleStrokeOwnerService(KeyCode key)
	{
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.Build, nameof(TestCommand.Build), CommandRemappingPolicy.Remappable,
				new KeyCombo(key, KeyModifierSet.Control))
		]);

		return CreateService(catalog: catalog);
	}

	[TestMethod]
	public void Dispatch_FirstStrokeOfAChord_ReportsPending()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		KeyChordDispatchResult result = dispatcher.Dispatch(Ctrl(KeyCode.K), null);

		Assert.AreEqual(KeyChordDispatchResult.Pending, result);
		Assert.IsTrue(dispatcher.HasPendingChord);
		Assert.AreEqual(new KeyChord(Ctrl(KeyCode.K)), dispatcher.PendingChord);
		Assert.IsEmpty(executed);
	}

	[TestMethod]
	public void Dispatch_CompletingStroke_ReportsExecuted()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		dispatcher.Dispatch(Ctrl(KeyCode.K), null);
		KeyChordDispatchResult result = dispatcher.Dispatch(Ctrl(KeyCode.S), null);

		Assert.AreEqual(KeyChordDispatchResult.Executed, result);
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.HasCount(1, executed);
		Assert.AreEqual(TestCommand.SaveAll, executed[0]);
	}

	/// <summary>
	/// Pins a chord of three or more strokes: every stroke is buffered exactly once, so the buffer stays a
	/// valid chord while the chord is in progress and the completing stroke reaches the command.
	/// </summary>
	[TestMethod]
	public void Dispatch_ThreeStrokeChord_BuffersEachStrokeOnceAndExecutes()
	{
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(TestCommand.SaveAll, nameof(TestCommand.SaveAll), CommandRemappingPolicy.Remappable,
				new KeyChord(Ctrl(KeyCode.K), Ctrl(KeyCode.S), Ctrl(KeyCode.F)))
		]);

		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateService(catalog: catalog));

		Assert.AreEqual(KeyChordDispatchResult.Pending, dispatcher.Dispatch(Ctrl(KeyCode.K), null));
		Assert.AreEqual(new KeyChord(Ctrl(KeyCode.K)), dispatcher.PendingChord);

		Assert.AreEqual(KeyChordDispatchResult.Pending, dispatcher.Dispatch(Ctrl(KeyCode.S), null));
		Assert.AreEqual(new KeyChord(Ctrl(KeyCode.K), Ctrl(KeyCode.S)), dispatcher.PendingChord);

		Assert.AreEqual(KeyChordDispatchResult.Executed, dispatcher.Dispatch(Ctrl(KeyCode.F), null));
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.HasCount(1, executed);
		Assert.AreEqual(TestCommand.SaveAll, executed[0]);
	}

	[TestMethod]
	public void Dispatch_SecondChordSharingThePrefix_ResolvesToItsOwnCommand()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		dispatcher.Dispatch(Ctrl(KeyCode.K), null);

		Assert.AreEqual(KeyChordDispatchResult.Executed, dispatcher.Dispatch(Ctrl(KeyCode.F), null));
		Assert.AreEqual(TestCommand.Find, executed[0]);
	}

	[TestMethod]
	public void Dispatch_WrongSecondStroke_DropsThePendingChord()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		dispatcher.Dispatch(Ctrl(KeyCode.K), null);
		KeyChordDispatchResult result = dispatcher.Dispatch(new KeyCombo(KeyCode.S, KeyModifierSet.None), null);

		Assert.AreEqual(KeyChordDispatchResult.NotHandled, result);
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.IsEmpty(executed);
	}

	[TestMethod]
	public void Dispatch_SingleStrokeBinding_StillExecutesWithNoPendingChord()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		// Control+S is not a prefix of any chord in this catalog, so it is an ordinary binding.
		Assert.AreEqual(KeyChordDispatchResult.Executed, dispatcher.Dispatch(Ctrl(KeyCode.S), null));
		Assert.AreEqual(TestCommand.Save, executed[0]);
	}

	[TestMethod]
	public void Dispatch_RepeatingThePendingStroke_DropsThePendingChord()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		dispatcher.Dispatch(Ctrl(KeyCode.K), null);
		KeyChordDispatchResult result = dispatcher.Dispatch(Ctrl(KeyCode.K), null);

		Assert.AreEqual(KeyChordDispatchResult.NotHandled, result);
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.IsEmpty(executed);
	}

	[TestMethod]
	public void Dispatch_UninitializedStroke_LeavesThePendingChordUntouched()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		dispatcher.Dispatch(Ctrl(KeyCode.K), null);

		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.Dispatch(default, null));
		Assert.IsTrue(dispatcher.HasPendingChord);
		Assert.IsEmpty(executed);

		// The chord is still completable after the junk stroke.
		Assert.AreEqual(KeyChordDispatchResult.Executed, dispatcher.Dispatch(Ctrl(KeyCode.S), null));
	}

	[TestMethod]
	public void Dispatch_CompletingStrokeWhoseCommandCannotExecute_DropsTheChordWithoutExecuting()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) =
			CreateDispatcher(CreateChordService(), canExecute: _ => false);

		dispatcher.Dispatch(Ctrl(KeyCode.K), null);
		KeyChordDispatchResult result = dispatcher.Dispatch(Ctrl(KeyCode.S), null);

		Assert.AreEqual(KeyChordDispatchResult.NotHandled, result);
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
		Assert.AreEqual(default(KeyChord), dispatcher.PendingChord);

		// Control+S is still an ordinary binding once the chord was abandoned.
		Assert.AreEqual(KeyChordDispatchResult.Executed, dispatcher.Dispatch(Ctrl(KeyCode.S), null));
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

	[TestMethod]
	public void DispatchInPriorityOrder_ContinuationIsNotStolenByAHigherPriorityContext()
	{
		// The global context outranks the editor context and binds Control+S on its own, while the editor
		// context owns the chord. Starting the chord in the lower-priority context must still keep its
		// continuation away from the higher-priority one.
		(KeyBindingDispatcher<TestCommand> globalDispatcher, List<TestCommand> globalExecuted, List<TestCommand> _) = CreateDispatcher(CreateSingleStrokeOwnerService(KeyCode.S));
		(KeyBindingDispatcher<TestCommand> editorDispatcher, List<TestCommand> editorExecuted, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		KeyBindingDispatcher<TestCommand>[] chain = [globalDispatcher, editorDispatcher];

		Assert.AreEqual(KeyChordDispatchResult.Pending, chain.DispatchInPriorityOrder(Ctrl(KeyCode.K), null));
		Assert.IsTrue(editorDispatcher.HasPendingChord);

		Assert.AreEqual(KeyChordDispatchResult.Executed, chain.DispatchInPriorityOrder(Ctrl(KeyCode.S), null));
		Assert.IsEmpty(globalExecuted);
		Assert.AreEqual(TestCommand.SaveAll, editorExecuted[0]);
	}

	[TestMethod]
	public void DispatchInPriorityOrder_WrongContinuation_IsNotOfferedToAnotherContext()
	{
		// The higher-priority context binds Control+Q, so it would happily run its own command if a wrong
		// stroke ended the chord: it must not be offered the stroke at all.
		(KeyBindingDispatcher<TestCommand> globalDispatcher, List<TestCommand> globalExecuted, List<TestCommand> _) = CreateDispatcher(CreateSingleStrokeOwnerService(KeyCode.Q));
		(KeyBindingDispatcher<TestCommand> editorDispatcher, List<TestCommand> editorExecuted, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		KeyBindingDispatcher<TestCommand>[] chain = [globalDispatcher, editorDispatcher];

		chain.DispatchInPriorityOrder(Ctrl(KeyCode.K), null);

		KeyChordDispatchResult result = chain.DispatchInPriorityOrder(Ctrl(KeyCode.Q), null);

		Assert.AreEqual(KeyChordDispatchResult.NotHandled, result);
		Assert.IsEmpty(globalExecuted);
		Assert.IsEmpty(editorExecuted);
		Assert.IsFalse(editorDispatcher.HasPendingChord);
	}

	[TestMethod]
	public void DispatchInPriorityOrder_ContextChangeBetweenStrokes_AbandonsTheChordAndWalksTheChain()
	{
		// The editor context owns a chord that only exists in the geometry context, while the
		// higher-priority context binds the continuation stroke in the texture context. Switching context
		// between the two strokes must abandon the buffered chord and dispatch the stroke as a fresh one,
		// instead of letting the abandoned buffer swallow it.
		(KeyBindingDispatcher<TestCommand> globalDispatcher, List<TestCommand> globalExecuted, List<TestCommand> _) =
			CreateDispatcher(CreateContextSingleStrokeService(KeyCode.S, TextureContext));
		(KeyBindingDispatcher<TestCommand> editorDispatcher, List<TestCommand> editorExecuted, List<TestCommand> _) =
			CreateDispatcher(CreateModeChordService());

		KeyBindingDispatcher<TestCommand>[] chain = [globalDispatcher, editorDispatcher];

		Assert.AreEqual(KeyChordDispatchResult.Pending, chain.DispatchInPriorityOrder(Ctrl(KeyCode.K), GeometryContext));
		Assert.IsTrue(editorDispatcher.HasPendingChord);

		Assert.AreEqual(KeyChordDispatchResult.Executed, chain.DispatchInPriorityOrder(Ctrl(KeyCode.S), TextureContext));

		Assert.AreEqual(TestCommand.Build, globalExecuted[0]);
		Assert.IsEmpty(editorExecuted);
		Assert.IsFalse(editorDispatcher.HasPendingChord);
	}

	// ---- Contexts ----

	/// <summary>
	/// Pins the host-misuse branch the chain remarks document: when two dispatchers both hold a pending
	/// chord under the active context, only the first in priority order receives the stroke and the
	/// dispatchers behind it are ignored and keep their buffer.
	/// </summary>
	[TestMethod]
	public void DispatchInPriorityOrder_TwoPendingChords_OnlyTheFirstMatchingDispatchersReceivesTheStroke()
	{
		(KeyBindingDispatcher<TestCommand> first, List<TestCommand> firstExecuted, List<TestCommand> _) = CreateDispatcher(CreateChordService());
		(KeyBindingDispatcher<TestCommand> second, List<TestCommand> secondExecuted, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		KeyBindingDispatcher<TestCommand>[] chain = [first, second];

		first.Dispatch(Ctrl(KeyCode.K), GeometryContext);
		second.Dispatch(Ctrl(KeyCode.K), GeometryContext);

		Assert.AreEqual(KeyChordDispatchResult.Executed, chain.DispatchInPriorityOrder(Ctrl(KeyCode.S), GeometryContext));

		Assert.AreEqual(TestCommand.SaveAll, firstExecuted[0]);
		Assert.IsEmpty(secondExecuted);
		Assert.IsFalse(first.HasPendingChord);
		Assert.IsTrue(second.HasPendingChord);
	}

	/// <summary>
	/// Pins that a pending chord is abandoned and a stroke is taken over by the first dispatcher whose
	/// pending context is the active one.
	/// </summary>
	[TestMethod]
	public void DispatchInPriorityOrder_PendingUnderAnInactiveContext_IsCancelledAndTheActiveContextReceives()
	{
		(KeyBindingDispatcher<TestCommand> inactive, List<TestCommand> inactiveExecuted, List<TestCommand> _) = CreateDispatcher(CreateChordService());
		(KeyBindingDispatcher<TestCommand> active, List<TestCommand> activeExecuted, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		KeyBindingDispatcher<TestCommand>[] chain = [inactive, active];

		inactive.Dispatch(Ctrl(KeyCode.K), GeometryContext);
		active.Dispatch(Ctrl(KeyCode.K), TextureContext);

		Assert.AreEqual(KeyChordDispatchResult.Executed, chain.DispatchInPriorityOrder(Ctrl(KeyCode.S), TextureContext));

		Assert.AreEqual(TestCommand.SaveAll, activeExecuted[0]);
		Assert.IsEmpty(inactiveExecuted);
		Assert.IsFalse(inactive.HasPendingChord);
		Assert.IsFalse(active.HasPendingChord);
	}

	[TestMethod]
	public void Dispatch_ChordDeclaredInAnotherContext_ReportsNotHandled()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateContextService());

		// E builds in the geometry context and finds in the texture context, so each context resolves its
		// own command and neither sees the other's binding.
		Assert.AreEqual(KeyChordDispatchResult.Executed, dispatcher.Dispatch(new KeyCombo(KeyCode.E, KeyModifierSet.None), GeometryContext));
		Assert.AreEqual(TestCommand.Build, executed[0]);

		Assert.AreEqual(KeyChordDispatchResult.Executed, dispatcher.Dispatch(new KeyCombo(KeyCode.E, KeyModifierSet.None), TextureContext));
		Assert.AreEqual(TestCommand.Find, executed[1]);

		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.Dispatch(new KeyCombo(KeyCode.E, KeyModifierSet.None), "lighting"));
		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.Dispatch(new KeyCombo(KeyCode.E, KeyModifierSet.None), null));
		Assert.HasCount(2, executed);
	}

	[TestMethod]
	public void Dispatch_ContextChangeBetweenStrokes_DropsTheBufferedChordAndStartsFresh()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateModeChordService());

		Assert.AreEqual(KeyChordDispatchResult.Pending, dispatcher.Dispatch(Ctrl(KeyCode.K), GeometryContext));

		// The same chord is bound to Find in the texture context, but the buffered strokes were started in
		// the geometry context, so they are abandoned instead of completing a command the user never
		// started in texture mode. The stroke is then considered on its own.
		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.Dispatch(Ctrl(KeyCode.S), TextureContext));
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.IsEmpty(executed);
	}

	[TestMethod]
	public void Dispatch_SameContextBetweenStrokes_CompletesTheChord()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher(CreateModeChordService());

		Assert.AreEqual(KeyChordDispatchResult.Pending, dispatcher.Dispatch(Ctrl(KeyCode.K), GeometryContext));
		Assert.AreEqual(KeyChordDispatchResult.Executed, dispatcher.Dispatch(Ctrl(KeyCode.S), GeometryContext));

		Assert.AreEqual(TestCommand.SaveAll, executed[0]);
	}
}
