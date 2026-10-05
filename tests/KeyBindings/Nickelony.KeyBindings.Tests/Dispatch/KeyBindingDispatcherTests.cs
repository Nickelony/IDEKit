using System.Diagnostics.CodeAnalysis;
using Nickelony.KeyBindings.Testing;
using static Nickelony.KeyBindings.Testing.KeyBindingTestHelpers;

namespace Nickelony.KeyBindings.Tests;

[TestClass]
public sealed class KeyBindingDispatcherTests
{
	[TestMethod]
	public void Dispatch_BoundAndExecutable_ExecutesAndReportsExecuted()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> checkedCommands) = CreateDispatcher();

		KeyStrokeDispatchOutcome outcome = dispatcher.Dispatch(new KeyStroke(KeyCode.S, KeyModifiers.Control), null);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, outcome);
		Assert.HasCount(1, executed);
		Assert.AreEqual(TestCommand.Save, executed[0]);
		Assert.HasCount(1, checkedCommands);
		Assert.AreEqual(TestCommand.Save, checkedCommands[0]);
	}

	[TestMethod]
	public void Dispatch_BoundButCannotExecute_ReportsNotHandledAndPassesResolvedCommand()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> checkedCommands) = CreateDispatcher(
			canExecute: _ => false
		);

		KeyStrokeDispatchOutcome outcome = dispatcher.Dispatch(new KeyStroke(KeyCode.Z, KeyModifiers.Control), null);

		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, outcome);
		Assert.IsEmpty(executed);
		Assert.HasCount(1, checkedCommands);
		Assert.AreEqual(TestCommand.Undo, checkedCommands[0]);
	}

	[TestMethod]
	public void Dispatch_UnboundStroke_ReportsNotHandledAndDoesNotCheckExecution()
	{
		(KeyBindingDispatcher<TestCommand> dispatcher, List<TestCommand> executed, List<TestCommand> checkedCommands) = CreateDispatcher();

		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, dispatcher.Dispatch(new KeyStroke(KeyCode.Q, KeyModifiers.Control), null));
		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, dispatcher.Dispatch(new KeyStroke(KeyCode.S, KeyModifiers.None), null));

		Assert.IsEmpty(executed);
		Assert.IsEmpty(checkedCommands);
	}

	[TestMethod]
	public void Constructor_NullLookupOrHooksWithANullCallback_Throws()
	{
		var lookup = new StubOverlappingChordLookup(default, default);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new KeyBindingDispatcher<TestCommand>(null!, new KeyBindingDispatcherHooks<TestCommand>(_ => true, _ => { }))
		);

		Assert.ThrowsExactly<ArgumentNullException>(() => new KeyBindingDispatcher<TestCommand>(lookup, default));

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new KeyBindingDispatcher<TestCommand>(lookup, new KeyBindingDispatcherHooks<TestCommand>(null!, _ => { }))
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new KeyBindingDispatcher<TestCommand>(lookup, new KeyBindingDispatcherHooks<TestCommand>(_ => true, null!))
		);
	}

	/// <summary>
	/// Pins that a command that throws is the host's exception to handle: it propagates instead of being swallowed, and the dispatcher is not left
	/// holding a half-finished chord.
	/// </summary>
	[TestMethod]
	public void Dispatch_ExecuteThrows_PropagatesAndLeavesNoPendingChord()
	{
		var thrown = new InvalidOperationException("The command failed.");

		KeyBindingDispatcher<TestCommand> dispatcher = CreateDispatcher(CreateChordService(), execute: _ => throw thrown).Dispatcher;

		Assert.AreEqual(KeyStrokeDispatchOutcome.Pending, dispatcher.Dispatch(new KeyStroke(KeyCode.K, KeyModifiers.Control), null));

		InvalidOperationException actual = Assert.ThrowsExactly<InvalidOperationException>(() =>
			dispatcher.Dispatch(new KeyStroke(KeyCode.S, KeyModifiers.Control), null)
		);

		Assert.AreSame(thrown, actual);
		Assert.IsFalse(dispatcher.HasPendingChord);
	}

	/// <summary>
	/// Pins that the dispatcher survives a throwing command: the buffered chord was already released, so the next chord starts from a clean buffer
	/// and still dispatches.
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
			}
		).Dispatcher;

		dispatcher.Dispatch(new KeyStroke(KeyCode.K, KeyModifiers.Control), null);

		Assert.ThrowsExactly<InvalidOperationException>(() => dispatcher.Dispatch(new KeyStroke(KeyCode.S, KeyModifiers.Control), null));

		Assert.AreEqual(KeyStrokeDispatchOutcome.Pending, dispatcher.Dispatch(new KeyStroke(KeyCode.K, KeyModifiers.Control), null));
		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, dispatcher.Dispatch(new KeyStroke(KeyCode.S, KeyModifiers.Control), null));

		Assert.HasCount(2, attempts);
		Assert.AreEqual(TestCommand.SaveAll, attempts[1]);
	}

	/// <summary>
	/// Pins the edge where the candidate chord is bound to a command that cannot execute and is also a strict prefix of a longer bound chord
	/// whose command can: the continuation is the more specific intent, so the stroke is buffered (<see cref="KeyStrokeDispatchOutcome.Pending"/>)
	/// instead of being dropped.
	/// </summary>
	[TestMethod]
	public void Dispatch_CompletedButCannotExecuteAndIsAlsoAPrefixOfAnExecutableChord_ReportsPending()
	{
		var bound = new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control));
		var longer = new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.Control));
		var executed = new List<TestCommand>();

		var dispatcher = new KeyBindingDispatcher<TestCommand>(
			new StubOverlappingChordLookup(bound, longer),
			new KeyBindingDispatcherHooks<TestCommand>(CanExecuteCommand: command => command == TestCommand.SaveAll, ExecuteCommand: executed.Add)
		);

		KeyStrokeDispatchOutcome outcome = dispatcher.Dispatch(new KeyStroke(KeyCode.K, KeyModifiers.Control), null);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Pending, outcome);
		Assert.IsTrue(dispatcher.HasPendingChord);
		Assert.IsEmpty(executed);
	}

	/// <summary>
	/// Pins the mirror of the pending edge: when neither the candidate chord's command nor the command of the longer chord it prefixes can execute,
	/// the stroke is not part of a chord in progress, so it reports <see cref="KeyStrokeDispatchOutcome.NotHandled"/> and buffers nothing.
	/// </summary>
	[TestMethod]
	public void Dispatch_CompletedButCannotExecuteAndNoCommandUnderItCanExecute_ReportsNotHandled()
	{
		var bound = new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control));
		var longer = new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.Control));
		var executed = new List<TestCommand>();

		var dispatcher = new KeyBindingDispatcher<TestCommand>(
			new StubOverlappingChordLookup(bound, longer),
			new KeyBindingDispatcherHooks<TestCommand>(CanExecuteCommand: _ => false, ExecuteCommand: executed.Add)
		);

		KeyStrokeDispatchOutcome outcome = dispatcher.Dispatch(new KeyStroke(KeyCode.K, KeyModifiers.Control), null);

		Assert.AreEqual(KeyStrokeDispatchOutcome.NotHandled, outcome);
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.IsEmpty(executed);
	}

	/// <summary>
	/// Pins the mirror of the cannot-execute prefix edge: when the candidate chord completes an executable command and is also a strict prefix
	/// of a longer bound chord, the command runs instead of the stroke being buffered, because a command that can execute wins over a merely possible
	/// longer chord.
	/// </summary>
	[TestMethod]
	public void Dispatch_CompletedAndIsAlsoAPrefix_ExecutesTheCompletedCommand()
	{
		var bound = new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control));
		var longer = new KeyChord(new KeyStroke(KeyCode.K, KeyModifiers.Control), new KeyStroke(KeyCode.S, KeyModifiers.Control));
		var executed = new List<TestCommand>();

		var dispatcher = new KeyBindingDispatcher<TestCommand>(
			new StubOverlappingChordLookup(bound, longer),
			new KeyBindingDispatcherHooks<TestCommand>(CanExecuteCommand: _ => true, ExecuteCommand: executed.Add)
		);

		KeyStrokeDispatchOutcome outcome = dispatcher.Dispatch(new KeyStroke(KeyCode.K, KeyModifiers.Control), null);

		Assert.AreEqual(KeyStrokeDispatchOutcome.Executed, outcome);
		Assert.IsFalse(dispatcher.HasPendingChord);
		Assert.HasCount(1, executed);
		Assert.AreEqual(TestCommand.Save, executed[0]);
	}

	/// <summary>
	/// A minimal <see cref="IKeyBindingResolver{TCommandId}"/> that binds one chord to <see cref="TestCommand.Save"/> and a longer chord it prefixes
	/// to <see cref="TestCommand.SaveAll"/>, so the dispatcher's completed-and-is-a-prefix edge is reachable.
	/// </summary>
	private sealed class StubOverlappingChordLookup(KeyChord bound, KeyChord longer) : IKeyBindingResolver<TestCommand>
	{
		public bool TryGetCommand(KeyChord keyChord, string? context, [NotNullWhen(true)] out TestCommand command)
		{
			if (keyChord.Equals(bound))
			{
				command = TestCommand.Save;
				return true;
			}

			if (keyChord.Equals(longer))
			{
				command = TestCommand.SaveAll;
				return true;
			}

			command = default;
			return false;
		}

		public bool IsChordPrefix(KeyChord keyChord, string? context, Func<TestCommand, bool> commandPredicate) =>
			keyChord.Equals(bound) && commandPredicate(TestCommand.SaveAll);
	}
}
