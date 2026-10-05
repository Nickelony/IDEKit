using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestHelpers;

namespace Nickelony.KeyBindings.Tests;

/// <summary>
/// Covers the dispatcher chain: the priority walk over several contexts, the chord rules across dispatchers, and the copy of its input.
/// </summary>
[TestClass]
public sealed class KeyBindingDispatcherChainTests
{
	private static KeyStroke Ctrl(KeyCode key) => new(key, KeyModifiers.Control);

	/// <summary>
	/// Builds a service that binds a single Control-modified stroke to <see cref="TestCommand.Build"/>, so a context that must not steal a chord
	/// continuation has something of its own to run.
	/// </summary>
	private static KeyBindingService<TestCommand> CreateSingleStrokeOwnerService(KeyCode key)
	{
		var catalog = new CommandCatalog<TestCommand>([
			new CommandDescriptor<TestCommand>(
				TestCommand.Build,
				nameof(TestCommand.Build),
				CommandRemappingPolicy.Remappable,
				new KeyStroke(key, KeyModifiers.Control)
			),
		]);

		return CreateService(catalog: catalog);
	}

	[TestMethod]
	public void Chain_FirstContextThatHandlesTheStrokeWins()
	{
		(KeyBindingDispatcher<TestCommand> editorDispatcher, List<TestCommand> editorExecuted, List<TestCommand> editorChecked) = CreateDispatcher();
		(KeyBindingDispatcher<TestCommand> globalDispatcher, List<TestCommand> globalExecuted, List<TestCommand> globalChecked) = CreateDispatcher();

		var chain = new KeyBindingDispatcherChain<TestCommand>([editorDispatcher, globalDispatcher]);

		KeyStrokeDispatchOutcome outcome = chain.Dispatch(new KeyStroke(KeyCode.S, KeyModifiers.Control), null);

		// Only the highest-priority context that handles the stroke runs; the contexts behind it are not even
		// asked whether their command can execute.
		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, outcome);
		Assert.AreEqual(TestCommand.Save, editorExecuted[0]);
		Assert.HasCount(1, editorChecked);
		Assert.IsEmpty(globalExecuted);
		Assert.IsEmpty(globalChecked);
	}

	[TestMethod]
	public void Chain_ContextThatCannotExecuteFallsThroughToTheNext()
	{
		(KeyBindingDispatcher<TestCommand> editorDispatcher, List<TestCommand> editorExecuted, List<TestCommand> editorChecked) = CreateDispatcher(
			canExecute: _ => false
		);

		(KeyBindingDispatcher<TestCommand> globalDispatcher, List<TestCommand> globalExecuted, List<TestCommand> _) = CreateDispatcher();

		var chain = new KeyBindingDispatcherChain<TestCommand>([editorDispatcher, globalDispatcher]);

		KeyStrokeDispatchOutcome outcome = chain.Dispatch(new KeyStroke(KeyCode.S, KeyModifiers.Control), null);

		// A context shadows the stroke only while its command can execute, so the next context gets the turn and
		// the blocked one still reports the command it resolved.
		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, outcome);
		Assert.IsEmpty(editorExecuted);
		Assert.AreEqual(TestCommand.Save, editorChecked[0]);
		Assert.AreEqual(TestCommand.Save, globalExecuted[0]);
	}

	[TestMethod]
	public void Chain_FirstStrokeOfAChordNoCommandCanExecute_FallsThroughToTheNextContext()
	{
		// The editor context owns a Control+K chord that cannot run, so its first stroke is not consumed and the
		// global context runs its own Control+K binding.
		(KeyBindingDispatcher<TestCommand> editorDispatcher, List<TestCommand> editorExecuted, List<TestCommand> _) = CreateDispatcher(
			CreateChordService(),
			canExecute: _ => false
		);

		(KeyBindingDispatcher<TestCommand> globalDispatcher, List<TestCommand> globalExecuted, List<TestCommand> _) = CreateDispatcher(
			CreateSingleStrokeOwnerService(KeyCode.K)
		);

		var chain = new KeyBindingDispatcherChain<TestCommand>([editorDispatcher, globalDispatcher]);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, chain.Dispatch(Ctrl(KeyCode.K), null));
		Assert.IsFalse(editorDispatcher.HasPendingChord);
		Assert.IsFalse(chain.HasPendingChord);
		Assert.IsEmpty(editorExecuted);
		Assert.HasCount(1, globalExecuted);
	}

	[TestMethod]
	public void Chain_FirstStrokeOfAChordACommandCanExecute_IsConsumedByTheContextThatOwnsIt()
	{
		(KeyBindingDispatcher<TestCommand> editorDispatcher, List<TestCommand> _, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		(KeyBindingDispatcher<TestCommand> globalDispatcher, List<TestCommand> globalExecuted, List<TestCommand> _) = CreateDispatcher(
			CreateSingleStrokeOwnerService(KeyCode.K)
		);

		var chain = new KeyBindingDispatcherChain<TestCommand>([editorDispatcher, globalDispatcher]);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Pending, chain.Dispatch(Ctrl(KeyCode.K), null));
		Assert.IsTrue(editorDispatcher.HasPendingChord);
		Assert.IsEmpty(globalExecuted);
	}

	[TestMethod]
	public void Chain_NoContextHandlesTheStroke_ReportsNotHandled()
	{
		(KeyBindingDispatcher<TestCommand> editorDispatcher, List<TestCommand> editorExecuted, List<TestCommand> editorChecked) = CreateDispatcher();
		(KeyBindingDispatcher<TestCommand> globalDispatcher, List<TestCommand> globalExecuted, List<TestCommand> globalChecked) = CreateDispatcher();

		var chain = new KeyBindingDispatcherChain<TestCommand>([editorDispatcher, globalDispatcher]);

		KeyStrokeDispatchOutcome outcome = chain.Dispatch(new KeyStroke(KeyCode.Q, KeyModifiers.Control), null);

		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, outcome);
		Assert.IsEmpty(editorExecuted);
		Assert.IsEmpty(editorChecked);
		Assert.IsEmpty(globalExecuted);
		Assert.IsEmpty(globalChecked);
	}

	[TestMethod]
	public void Chain_EmptyChain_ReportsNotHandled()
	{
		var chain = new KeyBindingDispatcherChain<TestCommand>([]);
		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, chain.Dispatch(new KeyStroke(KeyCode.S, KeyModifiers.Control), null));
	}

	[TestMethod]
	public void Constructor_NullChainOrNullElement_Throws()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher();

		Assert.ThrowsExactly<ArgumentNullException>(() => new KeyBindingDispatcherChain<TestCommand>(null!));

		// The null element is rejected when the chain is built, so no stroke can reach a dispatcher of a malformed chain.
		ArgumentNullException exception = Assert.ThrowsExactly<ArgumentNullException>(() =>
			new KeyBindingDispatcherChain<TestCommand>([dispatcher, null!])
		);

		Assert.AreEqual("dispatchers[1]", exception.ParamName);
		Assert.IsEmpty(executed);
	}

	[TestMethod]
	public void Chain_DispatchersAreCopiedWhenTheChainIsBuilt()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher();
		List<KeyBindingDispatcher<TestCommand>> source = [dispatcher];

		var chain = new KeyBindingDispatcherChain<TestCommand>(source);
		source.Clear();

		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, chain.Dispatch(new KeyStroke(KeyCode.S, KeyModifiers.Control), null));

		Assert.HasCount(1, executed);
	}

	[TestMethod]
	public void Chain_ContinuationIsNotStolenByAHigherPriorityContext()
	{
		// The global context outranks the editor context and binds Control+S on its own, while the editor
		// context owns the chord. Starting the chord in the lower-priority context must still keep its
		// continuation away from the higher-priority one.
		(KeyBindingDispatcher<TestCommand> globalDispatcher, List<TestCommand> globalExecuted, List<TestCommand> _) = CreateDispatcher(
			CreateSingleStrokeOwnerService(KeyCode.S)
		);

		(KeyBindingDispatcher<TestCommand> editorDispatcher, List<TestCommand> editorExecuted, List<TestCommand> _) = CreateDispatcher(
			CreateChordService()
		);

		var chain = new KeyBindingDispatcherChain<TestCommand>([globalDispatcher, editorDispatcher]);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Pending, chain.Dispatch(Ctrl(KeyCode.K), null));
		Assert.IsTrue(editorDispatcher.HasPendingChord);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, chain.Dispatch(Ctrl(KeyCode.S), null));
		Assert.IsEmpty(globalExecuted);
		Assert.AreEqual(TestCommand.SaveAll, editorExecuted[0]);
	}

	[TestMethod]
	public void Chain_ReportsAndCancelsTheChordOfAnyOfItsDispatchers()
	{
		(KeyBindingDispatcher<TestCommand> globalDispatcher, List<TestCommand> _, List<TestCommand> _) = CreateDispatcher(
			CreateSingleStrokeOwnerService(KeyCode.S)
		);

		(KeyBindingDispatcher<TestCommand> editorDispatcher, List<TestCommand> editorExecuted, List<TestCommand> _) = CreateDispatcher(
			CreateChordService()
		);

		var chain = new KeyBindingDispatcherChain<TestCommand>([globalDispatcher, editorDispatcher]);
		Assert.IsFalse(chain.HasPendingChord);

		chain.Dispatch(Ctrl(KeyCode.K), null);
		Assert.IsTrue(chain.HasPendingChord);

		chain.CancelPendingChord();

		Assert.IsFalse(chain.HasPendingChord);
		Assert.IsFalse(editorDispatcher.HasPendingChord);

		// With the chord gone, the continuation is a fresh stroke that the higher-priority context owns.
		chain.Dispatch(Ctrl(KeyCode.S), null);
		Assert.IsEmpty(editorExecuted);
	}

	[TestMethod]
	public void Chain_WrongContinuation_IsNotOfferedToAnotherContext()
	{
		// The higher-priority context binds Control+Q, so it would happily run its own command if a wrong
		// stroke ended the chord: it must not be offered the stroke at all.
		(KeyBindingDispatcher<TestCommand> globalDispatcher, List<TestCommand> globalExecuted, List<TestCommand> _) = CreateDispatcher(
			CreateSingleStrokeOwnerService(KeyCode.Q)
		);

		(KeyBindingDispatcher<TestCommand> editorDispatcher, List<TestCommand> editorExecuted, List<TestCommand> _) = CreateDispatcher(
			CreateChordService()
		);

		var chain = new KeyBindingDispatcherChain<TestCommand>([globalDispatcher, editorDispatcher]);

		chain.Dispatch(Ctrl(KeyCode.K), null);

		KeyStrokeDispatchOutcome outcome = chain.Dispatch(Ctrl(KeyCode.Q), null);

		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, outcome);
		Assert.IsEmpty(globalExecuted);
		Assert.IsEmpty(editorExecuted);
		Assert.IsFalse(editorDispatcher.HasPendingChord);
	}

	[TestMethod]
	public void Chain_ContextChangeBetweenStrokes_AbandonsTheChordAndWalksTheChain()
	{
		// The editor context owns a chord that only exists in the geometry context, while the
		// higher-priority context binds the continuation stroke in the texture context. Switching context
		// between the two strokes must abandon the buffered chord and dispatch the stroke as a fresh one,
		// instead of letting the abandoned buffer swallow it.
		(KeyBindingDispatcher<TestCommand> globalDispatcher, List<TestCommand> globalExecuted, List<TestCommand> _) = CreateDispatcher(
			CreateContextSingleStrokeService(KeyCode.S, TextureContext)
		);

		(KeyBindingDispatcher<TestCommand> editorDispatcher, List<TestCommand> editorExecuted, List<TestCommand> _) = CreateDispatcher(
			CreateModeChordService()
		);

		var chain = new KeyBindingDispatcherChain<TestCommand>([globalDispatcher, editorDispatcher]);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Pending, chain.Dispatch(Ctrl(KeyCode.K), GeometryContext));
		Assert.IsTrue(editorDispatcher.HasPendingChord);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, chain.Dispatch(Ctrl(KeyCode.S), TextureContext));

		Assert.AreEqual(TestCommand.Build, globalExecuted[0]);
		Assert.IsEmpty(editorExecuted);
		Assert.IsFalse(editorDispatcher.HasPendingChord);
	}

	/// <summary>
	/// Pins the host-misuse branch: when two dispatchers both hold a pending chord under the active context, only the first in priority order
	/// receives the stroke and the dispatchers behind it are ignored and keep their buffer.
	/// </summary>
	[TestMethod]
	public void Chain_TwoPendingChords_OnlyTheFirstMatchingDispatcherReceivesTheStroke()
	{
		(KeyBindingDispatcher<TestCommand> first, List<TestCommand> firstExecuted, List<TestCommand> _) = CreateDispatcher(CreateChordService());
		(KeyBindingDispatcher<TestCommand> second, List<TestCommand> secondExecuted, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		var chain = new KeyBindingDispatcherChain<TestCommand>([first, second]);

		first.Dispatch(Ctrl(KeyCode.K), GeometryContext);
		second.Dispatch(Ctrl(KeyCode.K), GeometryContext);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, chain.Dispatch(Ctrl(KeyCode.S), GeometryContext));

		Assert.AreEqual(TestCommand.SaveAll, firstExecuted[0]);
		Assert.IsEmpty(secondExecuted);
		Assert.IsFalse(first.HasPendingChord);
		Assert.IsTrue(second.HasPendingChord);
	}

	/// <summary>
	/// Pins that a pending chord is abandoned and a stroke is taken over by the first dispatcher whose pending context is the active one.
	/// </summary>
	[TestMethod]
	public void Chain_PendingUnderAnInactiveContext_IsCanceledAndTheActiveContextReceives()
	{
		(KeyBindingDispatcher<TestCommand> inactive, List<TestCommand> inactiveExecuted, List<TestCommand> _) = CreateDispatcher(
			CreateChordService()
		);

		(KeyBindingDispatcher<TestCommand> active, List<TestCommand> activeExecuted, List<TestCommand> _) = CreateDispatcher(CreateChordService());

		var chain = new KeyBindingDispatcherChain<TestCommand>([inactive, active]);

		inactive.Dispatch(Ctrl(KeyCode.K), GeometryContext);
		active.Dispatch(Ctrl(KeyCode.K), TextureContext);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, chain.Dispatch(Ctrl(KeyCode.S), TextureContext));

		Assert.AreEqual(TestCommand.SaveAll, activeExecuted[0]);
		Assert.IsEmpty(inactiveExecuted);
		Assert.IsFalse(inactive.HasPendingChord);
		Assert.IsFalse(active.HasPendingChord);
	}

	[TestMethod]
	public void Chain_AndDispatcher_AreInterchangeableThroughTheDispatchInterface()
	{
		(KeyBindingDispatcher<TestCommand> single, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher();

		IKeyBindingDispatcher[] dispatchers = [single, new KeyBindingDispatcherChain<TestCommand>([single])];

		foreach (IKeyBindingDispatcher dispatcher in dispatchers)
			Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, dispatcher.Dispatch(Ctrl(KeyCode.S), null));

		Assert.HasCount(2, executed);
	}
}
