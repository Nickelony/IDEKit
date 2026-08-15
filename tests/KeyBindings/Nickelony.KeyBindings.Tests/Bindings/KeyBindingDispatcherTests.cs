using Nickelony.KeyBindings.Testing;
using System.Diagnostics.CodeAnalysis;
using static Nickelony.KeyBindings.Testing.KeyBindingTestFixtures;

namespace Nickelony.KeyBindings.Tests;

[TestClass]
public class KeyBindingDispatcherTests
{
	[TestMethod]
	public void Dispatch_BoundAndExecutable_ExecutesAndReportsExecuted()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> checkedCommands) = CreateDispatcher();

		KeyChordDispatchResult result = dispatcher.Dispatch(new KeyCombo(KeyCode.S, KeyModifierSet.Control), null);

		Assert.AreEqual(KeyChordDispatchResult.Executed, result);
		Assert.HasCount(1, executed);
		Assert.AreEqual(TestCommand.Save, executed[0]);
		Assert.HasCount(1, checkedCommands);
		Assert.AreEqual(TestCommand.Save, checkedCommands[0]);
	}

	[TestMethod]
	public void Dispatch_BoundButCannotExecute_ReportsNotHandledAndPassesResolvedCommand()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> checkedCommands) = CreateDispatcher(canExecute: _ => false);

		KeyChordDispatchResult result = dispatcher.Dispatch(new KeyCombo(KeyCode.Z, KeyModifierSet.Control), null);

		Assert.AreEqual(KeyChordDispatchResult.NotHandled, result);
		Assert.IsEmpty(executed);
		Assert.HasCount(1, checkedCommands);
		Assert.AreEqual(TestCommand.Undo, checkedCommands[0]);
	}

	[TestMethod]
	public void Dispatch_UnboundCombo_ReportsNotHandledAndDoesNotCheckExecution()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> checkedCommands) = CreateDispatcher();

		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.Dispatch(new KeyCombo(KeyCode.Q, KeyModifierSet.Control), null));
		Assert.AreEqual(KeyChordDispatchResult.NotHandled, dispatcher.Dispatch(new KeyCombo(KeyCode.S, KeyModifierSet.None), null));

		Assert.IsEmpty(executed);
		Assert.IsEmpty(checkedCommands);
	}

	[TestMethod]
	public void DispatchInPriorityOrder_FirstContextThatHandlesTheComboWins()
	{
		(KeyBindingDispatcher<TestCommand> editorDispatcher, List<TestCommand> editorExecuted, List<TestCommand> editorChecked) = CreateDispatcher();
		(KeyBindingDispatcher<TestCommand> globalDispatcher, List<TestCommand> globalExecuted, List<TestCommand> globalChecked) = CreateDispatcher();

		KeyBindingDispatcher<TestCommand>[] chain = [editorDispatcher, globalDispatcher];

		KeyChordDispatchResult result = chain.DispatchInPriorityOrder(new KeyCombo(KeyCode.S, KeyModifierSet.Control), null);

		// Only the highest-priority context that handles the combo runs; the contexts behind it are not even
		// asked whether their command can execute.
		Assert.AreEqual(KeyChordDispatchResult.Executed, result);
		Assert.AreEqual(TestCommand.Save, editorExecuted[0]);
		Assert.HasCount(1, editorChecked);
		Assert.IsEmpty(globalExecuted);
		Assert.IsEmpty(globalChecked);
	}

	[TestMethod]
	public void DispatchInPriorityOrder_ContextThatCannotExecuteFallsThroughToTheNext()
	{
		(KeyBindingDispatcher<TestCommand> editorDispatcher, List<TestCommand> editorExecuted, List<TestCommand> editorChecked) = CreateDispatcher(canExecute: _ => false);
		(KeyBindingDispatcher<TestCommand> globalDispatcher, List<TestCommand> globalExecuted, List<TestCommand> _) = CreateDispatcher();

		KeyBindingDispatcher<TestCommand>[] chain = [editorDispatcher, globalDispatcher];

		KeyChordDispatchResult result = chain.DispatchInPriorityOrder(new KeyCombo(KeyCode.S, KeyModifierSet.Control), null);

		// A context shadows the combo only while its command can execute, so the next context gets the turn and
		// the blocked one still reports the command it resolved.
		Assert.AreEqual(KeyChordDispatchResult.Executed, result);
		Assert.IsEmpty(editorExecuted);
		Assert.AreEqual(TestCommand.Save, editorChecked[0]);
		Assert.AreEqual(TestCommand.Save, globalExecuted[0]);
	}

	[TestMethod]
	public void DispatchInPriorityOrder_NoContextHandlesTheCombo_ReportsNotHandled()
	{
		(KeyBindingDispatcher<TestCommand> editorDispatcher, List<TestCommand> editorExecuted, List<TestCommand> editorChecked) = CreateDispatcher();
		(KeyBindingDispatcher<TestCommand> globalDispatcher, List<TestCommand> globalExecuted, List<TestCommand> globalChecked) = CreateDispatcher();

		KeyBindingDispatcher<TestCommand>[] chain = [editorDispatcher, globalDispatcher];

		KeyChordDispatchResult result = chain.DispatchInPriorityOrder(new KeyCombo(KeyCode.Q, KeyModifierSet.Control), null);

		Assert.AreEqual(KeyChordDispatchResult.NotHandled, result);
		Assert.IsEmpty(editorExecuted);
		Assert.IsEmpty(editorChecked);
		Assert.IsEmpty(globalExecuted);
		Assert.IsEmpty(globalChecked);
	}

	[TestMethod]
	public void DispatchInPriorityOrder_NullChainOrNullElement_ThrowsBeforeDispatching()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> _) = CreateDispatcher();

		KeyBindingDispatcher<TestCommand>[]? nullChain = null;

		Assert.ThrowsExactly<ArgumentNullException>(
			() => nullChain!.DispatchInPriorityOrder(new KeyCombo(KeyCode.S, KeyModifierSet.Control), null));

		// The null element is rejected before any dispatcher runs, so a malformed chain cannot execute part
		// of itself and then throw.
		KeyBindingDispatcher<TestCommand>[] chain = [dispatcher, null!];

		Assert.ThrowsExactly<ArgumentNullException>(
			() => chain.DispatchInPriorityOrder(new KeyCombo(KeyCode.S, KeyModifierSet.Control), null));

		Assert.IsEmpty(executed);
	}

	/// <summary>
	/// Pins that a command that throws is the host's exception to handle: it propagates instead of being
	/// swallowed, and the dispatcher is not left holding a half-finished chord.
	/// </summary>
	[TestMethod]
	public void Dispatch_ExecuteThrows_PropagatesAndLeavesNoPendingChord()
	{
		var thrown = new InvalidOperationException("The command failed.");

		KeyBindingDispatcher<TestCommand> dispatcher = CreateDispatcher(
			CreateChordService(),
			execute: _ => throw thrown).Dispatcher;

		Assert.AreEqual(KeyChordDispatchResult.Pending, dispatcher.Dispatch(new KeyCombo(KeyCode.K, KeyModifierSet.Control), null));

		InvalidOperationException actual = Assert.ThrowsExactly<InvalidOperationException>(
			() => dispatcher.Dispatch(new KeyCombo(KeyCode.S, KeyModifierSet.Control), null));

		Assert.AreSame(thrown, actual);
		Assert.IsFalse(dispatcher.HasPendingChord);
	}

	/// <summary>
	/// Pins that the dispatcher survives a throwing command: the buffered chord was already released, so
	/// the next chord starts from a clean buffer and still dispatches.
	/// </summary>
	[TestMethod]
	public void Dispatch_AfterExecuteThrows_KeepsDispatching()
	{
		var attempts = new List<TestCommand>();

		KeyBindingDispatcher<TestCommand> dispatcher = CreateDispatcher(
			CreateChordService(),
			execute: command =>
			{
				attempts.Add(command);

				if (attempts.Count == 1)
					throw new InvalidOperationException("The first attempt fails.");
			}).Dispatcher;

		dispatcher.Dispatch(new KeyCombo(KeyCode.K, KeyModifierSet.Control), null);

		Assert.ThrowsExactly<InvalidOperationException>(
			() => dispatcher.Dispatch(new KeyCombo(KeyCode.S, KeyModifierSet.Control), null));

		Assert.AreEqual(KeyChordDispatchResult.Pending, dispatcher.Dispatch(new KeyCombo(KeyCode.K, KeyModifierSet.Control), null));
		Assert.AreEqual(KeyChordDispatchResult.Executed, dispatcher.Dispatch(new KeyCombo(KeyCode.S, KeyModifierSet.Control), null));

		Assert.HasCount(2, attempts);
		Assert.AreEqual(TestCommand.SaveAll, attempts[1]);
	}

	/// <summary>
	/// Pins the edge where the candidate chord is bound to a command that cannot execute and is also a
	/// strict prefix of a longer bound chord: the continuation is the more specific intent, so the stroke
	/// is buffered (<see cref="KeyChordDispatchResult.Pending"/>) instead of being dropped.
	/// </summary>
	[TestMethod]
	public void Dispatch_CompletedButCannotExecuteAndIsAlsoAPrefix_ReportsPending()
	{
		var bound = new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control));
		var longer = new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control), new KeyCombo(KeyCode.S, KeyModifierSet.Control));
		var executed = new List<TestCommand>();

		var dispatcher = new KeyBindingDispatcher<TestCommand>(
			new OverlappingChordService(bound, longer),
			new KeyBindingDispatcherHooks<TestCommand>(
				canExecuteCommand: _ => false,
				executeCommand: executed.Add));

		KeyChordDispatchResult result = dispatcher.Dispatch(new KeyCombo(KeyCode.K, KeyModifierSet.Control), null);

		Assert.AreEqual(KeyChordDispatchResult.Pending, result);
		Assert.IsTrue(dispatcher.HasPendingChord);
		Assert.IsEmpty(executed);
	}

	/// <summary>
	/// Pins the mirror of the cannot-execute prefix edge: when the candidate chord completes an
	/// executable command and is also a strict prefix of a longer bound chord, the command runs instead
	/// of the stroke being buffered, because a command that can execute wins over a merely possible
	/// longer chord.
	/// </summary>
	[TestMethod]
	public void Dispatch_CompletedAndIsAlsoAPrefix_ExecutesTheCompletedCommand()
	{
		var bound = new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control));
		var longer = new KeyChord(new KeyCombo(KeyCode.K, KeyModifierSet.Control), new KeyCombo(KeyCode.S, KeyModifierSet.Control));
		var executed = new List<TestCommand>();

		var dispatcher = new KeyBindingDispatcher<TestCommand>(
			new OverlappingChordService(bound, longer),
			new KeyBindingDispatcherHooks<TestCommand>(
				canExecuteCommand: _ => true,
				executeCommand: executed.Add));

		KeyChordDispatchResult result = dispatcher.Dispatch(new KeyCombo(KeyCode.K, KeyModifierSet.Control), null);

		Assert.AreEqual(KeyChordDispatchResult.Executed, result);
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.HasCount(1, executed);
		Assert.AreEqual(TestCommand.Save, executed[0]);
	}

	/// <summary>
	/// A minimal <see cref="IKeyBindingService{TCommandId}"/> that reports one chord as bound (to a
	/// command the dispatcher is told cannot execute) and as a prefix of a longer bound chord, so the
	/// dispatcher's completed-but-cannot-execute-and-is-a-prefix edge is reachable. The real service
	/// rejects that declaration, so a test double is the only way to pin the edge.
	/// </summary>
	private sealed class OverlappingChordService : IKeyBindingService<TestCommand>
	{
		private readonly KeyChord _bound;
		private readonly KeyChord _longer;

		public OverlappingChordService(KeyChord bound, KeyChord longer)
		{
			_bound = bound;
			_longer = longer;
		}

		public event EventHandler<KeyBindingsChangedEventArgs<TestCommand>>? BindingsChanged
		{
			add { }
			remove { }
		}

		public IReadOnlySet<string> Contexts { get; } = new HashSet<string>();

		public bool TryGetCommand(KeyChord keyChord, string? context, [NotNullWhen(true)] out TestCommand command)
		{
			if (keyChord.Equals(_bound) || keyChord.Equals(_longer))
			{
				command = TestCommand.Save;
				return true;
			}

			command = default;
			return false;
		}

		public bool IsChordPrefix(KeyChord keyChord, string? context) => keyChord.Equals(_bound);

		public IReadOnlyList<KeyChord> GetBindings(TestCommand command) => [];

		public string GetDisplayText(TestCommand command, string fallbackDisplayText = "") => fallbackDisplayText;

		public KeyBindingOutcome Validate(TestCommand command, IReadOnlyList<KeyChord> bindings, KeyBindingConflictPolicy conflictPolicy = KeyBindingConflictPolicy.Reject)
			=> KeyBindingOutcome.Succeeded;

		public KeyBindingOutcome Apply(TestCommand command, IReadOnlyList<KeyChord> bindings, KeyBindingConflictPolicy conflictPolicy = KeyBindingConflictPolicy.Reject)
			=> KeyBindingOutcome.Succeeded;

		public KeyBindingOutcome Reset(TestCommand command) => KeyBindingOutcome.Succeeded;

		public KeyBindingOutcome ResetAll() => KeyBindingOutcome.Succeeded;
	}
}
