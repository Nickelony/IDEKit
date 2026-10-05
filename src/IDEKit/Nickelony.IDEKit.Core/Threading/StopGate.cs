using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.Core.Threading;

/// <summary>
/// Admits operations until its owner stops, tracks the operations in flight, memoizes the owner's stop task, and, unless the owner's stop
/// returns without waiting, refuses a stop requested from inside an operation.
/// </summary>
/// <typeparam name="TOperation">The owner's operation type. Operations compare by reference.</typeparam>
/// <remarks>
/// <para>
/// The gate has no lock of its own. The owner passes the lock that already orders its other state, and every member that reads or changes the gate's
/// state requires the caller to hold it and throws <see cref="InvalidOperationException"/> otherwise, so admitting an operation, changing other owner
/// state and checking for a stop are one atomic step.
/// </para>
/// <para>
/// The gate holds the state every owner that stops this way needs. Each owner keeps its own teardown: what it awaits, releases and disposes
/// once the in-flight operations have completed.
/// </para>
/// <para>
/// A stop waits for every in-flight operation, so a stop requested from inside one of them can never complete.
/// <see cref="Admit"/> marks the asynchronous flow of the operation, and <see cref="TryBeginStop"/> throws for a caller whose flow
/// carries the mark of an operation still in flight. The mark reaches everything the operation starts, a <see cref="Task.Run(Action)"/> included,
/// and everything that runs on a thread while the marked code is on its stack. A stop requested after the operation completed is not affected.
/// An owner that hands work to a dispatcher that may not flow the execution context wraps it with <see cref="Carry"/>.
/// </para>
/// <para>
/// An owner whose stop returns without waiting, because it releases its resources in a continuation after the last operation
/// in flight has left, builds the gate with <c>stopWaitsForOperations</c> set to <see langword="false"/>. Such a stop cannot wait for the operation
/// that requests it, so the gate allows a stop from inside an operation, and the snapshot that the stop returns includes that operation,
/// so the owner's release still follows its exit.
/// </para>
/// </remarks>
public sealed class StopGate<TOperation>
	where TOperation : class
{
	private readonly object _syncRoot;
	private readonly bool _stopWaitsForOperations;
	private readonly HashSet<TOperation> _operations = new(ReferenceEqualityComparer.Instance);
	private readonly AsyncLocal<OperationScope?> _scope = new();

	private Task? _stopTask;
	private bool _stopping;

	/// <summary>Initializes a gate that orders its state under the owner's lock and whose stop waits for every operation in flight.</summary>
	/// <param name="syncRoot">
	/// The owner's lock, a <see cref="Monitor"/> lock taken with <c>lock</c>, held by the caller of every member except <see cref="Carry"/>.
	/// </param>
	public StopGate(object syncRoot)
		: this(syncRoot, stopWaitsForOperations: true) { }

	/// <summary>
	/// Initializes a gate that orders its state under the owner's lock and states whether the owner's stop waits for the operations.
	/// </summary>
	/// <param name="syncRoot">
	/// The owner's lock, a <see cref="Monitor"/> lock taken with <c>lock</c>, held by the caller of every member except <see cref="Carry"/>.
	/// </param>
	/// <param name="stopWaitsForOperations">
	/// <see langword="true"/> when the owner's stop waits for every operation in flight, so the gate refuses a stop requested
	/// from inside one of them; <see langword="false"/> when the owner's stop returns without waiting, so the gate allows it.
	/// </param>
	public StopGate(object syncRoot, bool stopWaitsForOperations)
	{
		ArgumentNullException.ThrowIfNull(syncRoot);

		_syncRoot = syncRoot;
		_stopWaitsForOperations = stopWaitsForOperations;
	}

	/// <summary>Indicates whether the owner began stopping.</summary>
	/// <exception cref="InvalidOperationException">The caller does not hold the owner's lock.</exception>
	public bool IsStopping
	{
		get
		{
			RequireLockHeld();
			return _stopping;
		}
	}

	/// <summary>Throws when the owner began stopping.</summary>
	/// <param name="owner">The owner named by the exception.</param>
	/// <exception cref="ObjectDisposedException">The owner began stopping.</exception>
	/// <exception cref="InvalidOperationException">The caller does not hold the owner's lock.</exception>
	public void ThrowIfStopping(object owner)
	{
		ArgumentNullException.ThrowIfNull(owner);
		RequireLockHeld();

		ObjectDisposedException.ThrowIf(_stopping, owner);
	}

	/// <summary>Registers an operation that the stop waits for and marks the calling flow as running inside it.</summary>
	/// <param name="operation">The operation to track until <see cref="Complete"/>.</param>
	/// <param name="owner">The owner named by the exception.</param>
	/// <exception cref="ObjectDisposedException">The owner began stopping.</exception>
	/// <exception cref="InvalidOperationException">The caller does not hold the owner's lock.</exception>
	/// <remarks>
	/// <para>
	/// A caller that already acquired a resource for the operation checks for a stop earlier in the same critical section, because a throw
	/// here would leave the resource held.
	/// </para>
	/// <para>
	/// The mark stays in the flow that continues the caller's asynchronous method. It does not reach the caller of that method.
	/// </para>
	/// </remarks>
	public void Admit(TOperation operation, object owner)
	{
		ArgumentNullException.ThrowIfNull(operation);
		ThrowIfStopping(owner);

		_operations.Add(operation);
		_scope.Value = new OperationScope(operation, _scope.Value);
	}

	/// <summary>Wraps an action so that it runs inside the operations the calling flow is inside, on whichever thread runs it.</summary>
	/// <param name="action">The action to hand to a dispatcher.</param>
	/// <returns>The action itself when the calling flow is inside no operation; otherwise, a wrapper that restores the mark around it.</returns>
	/// <remarks>
	/// A dispatcher that queues the action to a thread of its own, such as a loop that drains a queue, may not flow the execution context,
	/// so the action would not carry the mark that <see cref="Admit"/> set. A stop requested from that action is as unable to complete
	/// as one requested inline.
	/// </remarks>
	public Action Carry(Action action)
	{
		ArgumentNullException.ThrowIfNull(action);

		OperationScope? scope = _scope.Value;

		if (scope is null)
			return action;

		return () =>
		{
			OperationScope? previous = _scope.Value;

			_scope.Value = scope;

			try
			{
				action();
			}
			finally
			{
				_scope.Value = previous;
			}
		};
	}

	/// <summary>Stops tracking an operation that finished.</summary>
	/// <param name="operation">The operation passed to <see cref="Admit"/>.</param>
	/// <exception cref="InvalidOperationException">The caller does not hold the owner's lock.</exception>
	public void Complete(TOperation operation)
	{
		ArgumentNullException.ThrowIfNull(operation);
		RequireLockHeld();

		_operations.Remove(operation);
	}

	/// <summary>Begins the stop unless one already began.</summary>
	/// <param name="stopTask">Receives the memoized stop task when a stop already began.</param>
	/// <param name="operations">Receives the operations in flight when this call began the stop.</param>
	/// <returns>
	/// <see langword="true"/> when this call began the stop and the caller must record its stop task; otherwise, <see langword="false"/>.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// The caller does not hold the owner's lock, or the gate's stop waits for operations and the calling flow runs inside an operation
	/// that is still in flight.
	/// </exception>
	/// <remarks>
	/// A new operation cannot be admitted after this call returns <see langword="true"/>, so the returned operations are the complete
	/// set to wait for. The caller records its stop task with <see cref="RecordStopTask"/> in the same critical section. The exception applies
	/// to a repeated stop as well, because the memoized stop task waits for the same operation.
	/// </remarks>
	public bool TryBeginStop([NotNullWhen(false)] out Task? stopTask, out IReadOnlyList<TOperation> operations)
	{
		RequireLockHeld();

		if (_stopWaitsForOperations)
			ThrowIfCalledFromAnOperation();

		if (_stopping)
		{
			// The first stop records its task in the same critical section, so a missing task means the owner skipped RecordStopTask;
			// starting a second stop would be worse than failing.
			stopTask = _stopTask ?? throw new InvalidOperationException("The first stop did not record its stop task.");
			operations = [];
			return false;
		}

		_stopping = true;
		stopTask = null;
		operations = [.. _operations];
		return true;
	}

	/// <summary>Memoizes the stop task so a later stop call observes the same completion.</summary>
	/// <param name="stopTask">The owner's stop task.</param>
	/// <exception cref="InvalidOperationException">The caller does not hold the owner's lock.</exception>
	public void RecordStopTask(Task stopTask)
	{
		ArgumentNullException.ThrowIfNull(stopTask);
		RequireLockHeld();

		_stopTask = stopTask;
	}

	private void ThrowIfCalledFromAnOperation()
	{
		for (OperationScope? scope = _scope.Value; scope is not null; scope = scope.Outer)
		{
			if (_operations.Contains(scope.Operation))
			{
				throw new InvalidOperationException(
					"A stop or disposal cannot be requested from inside an operation it waits for, because it would wait for that operation. "
						+ "Request it from outside the operation."
				);
			}
		}
	}

	private void RequireLockHeld()
	{
		if (!Monitor.IsEntered(_syncRoot))
			throw new InvalidOperationException("The caller must hold the owner's lock.");
	}

	// The operations a flow runs inside, innermost first. The chain is immutable, so a flow that copies the execution context shares it safely.
	private sealed class OperationScope(TOperation operation, OperationScope? outer)
	{
		internal TOperation Operation { get; } = operation;
		internal OperationScope? Outer { get; } = outer;
	}
}
